(*
  Copyright (C) 2015 Karsten Gebbert

  This program is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  This program is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with this program.  If not, see <http://www.gnu.org/licenses/>.
*)

module Paket2Nix.Core

open System
open System.IO
open System.Reflection
open System.Security.Cryptography
open Microsoft.FSharp.Control 
open System.Net
open Paket
open Paket.Domain

type Name    = string
type Version = string
type Url     = string
type Sha256  = string
type Rev     = string

type Dependency = (string * string)

type INixExpr =
  abstract member ToNix : unit -> string 
  abstract member Name  : string

type AppCnf =
  { Root        : string
  ; Url         : string option
  ; Verbose     : bool
  ; Checksum    : bool
  ; Destination : string
  }

(*----------------------------------------------------------------------------*)
let bail str =
  printfn "%s" str
  exit 1

(*----------------------------------------------------------------------------*)
let private sanitize (str : string) : string = str.Replace(".","")


(*----------------------------------------------------------------------------*)
let serializeType (t : ProjectOutputType) : string =
  match t with
    | ProjectOutputType.Exe     -> "exe"
    | ProjectOutputType.Library -> "library"

(*----------------------------------------------------------------------------*)
let internal replace (from : string) (two : string) (target : string) =
  target.Replace(from,two)

(*----------------------------------------------------------------------------*)
let internal spliceFields (attrs : (string * string) list) (tmpl : string) : string =
  List.fold (fun m (expr, value) -> replace expr value m) tmpl attrs

(*----------------------------------------------------------------------------*)
let internal gitTmpl (url : Url) (sha : Sha256) (rev : Rev) =
  @"fetchgit {
    url    = ""$url"";
    sha256 = ""$sha"";
    rev    = ""$rev"";
  }"
  |> spliceFields [("$url", url); ("$sha", sha); ("$rev", rev)]

(*----------------------------------------------------------------------------*)
let internal nugetTmpl (url : Url) (sha : Sha256) =
  @"fetchurl {
    url    = ""$url"";
    sha256 = ""$sha"";
  }"
  |> spliceFields [("$url", url); ("$sha", sha)]


(*----------------------------------------------------------------------------*)
type Method =
  | Nuget  of url : Url * sha256 : Sha256
  | Github of url : Url * sha256 : Sha256 * rev : Rev

  override self.ToString() =
    match self with
      | Nuget(u, s)     -> nugetTmpl u s
      | Github(u, s, r) -> gitTmpl u s r

(*----------------------------------------------------------------------------*)
let internal depPkgTmpl (name : Name) (version : Version) (meth : Method) =
  @"
{ stdenv, fetchgit, fetchurl, unzip }:

stdenv.mkDerivation {
  name = ""$pkgname-$version"";

  src = $method;

  phases = [ ""unpackPhase"" ];

  buildInputs = [ unzip ];

  unpackPhase = ''
    mkdir -p ""$out/lib/mono/packages/$pkgname-$version/$name"";
    unzip -x ""$src"" -d ""$out/lib/mono/packages/$pkgname-$version/$name"";
  '';
}
" |> spliceFields
       [ ("$pkgname", name.ToLower())
       ; ("$name"   , name)
       ; ("$version", version)
       ; ("$method" , meth.ToString())
       ]

(*----------------------------------------------------------------------------*)
type NixPkgDep =
  { Name         : Name
  ; Version      : Version
  ; Method       : Method
  }
  interface INixExpr with
    member self.Name = self.Name
    member self.ToNix () =
      depPkgTmpl self.Name
                 self.Version
                 self.Method


(*----------------------------------------------------------------------------*)
let private collapse (sep : string)  =
  List.fold (fun m p -> m + sep + p) ""


(*----------------------------------------------------------------------------*)
let private quoted =
  List.fold (fun m p -> m + (sprintf @" ""%s""" p)) ""


(*----------------------------------------------------------------------------*)
let private storePath =
  sprintf @"${%s}/lib/mono/packages/%s-%s"

let getUrl meth = 
  match meth with
    | Nuget(url, _)     -> url
    | Github(url, _, _) -> url

let exeTmpl = @"
    mkdir -p ""$out/bin"";
    cat > ""$out/bin/$script"" <<-WRAPPER
    #!/bin/sh
    ${mono}/bin/mono $out/lib/mono/packages/$pkgname-$version/$name/$exe ""\$@""
    WRAPPER
    chmod a+x ""$out/bin/$script"" "

let exeCmd t (name : string) version (assembly : string) =
  match t with
    | ProjectOutputType.Exe ->
      exeTmpl
      |> spliceFields [ ("$name",    sanitize name)
                      ; ("$pkgname", name.ToLower())
                      ; ("$version", version.ToString())
                      ; ("$script",  name.ToLower() |> sanitize)
                      ; ("$exe",     assembly) ]
    | _ -> ""


let private pkgTemplate = @"
{ stdenv, fetchgit, fetchurl, fsharp, mono $dependencies }:

stdenv.mkDerivation {
  name = ""$pkgname-$version"";

  src = $method;

  meta = {
    homepage = ""$homepage"";
    description = ""$description"";
    maintainers = [ $maintainers ];
  };

  phases = [ ""unpackPhase"" ""patchPhase"" ""buildPhase"" ""installPhase"" ];

  buildInputs = [ fsharp mono $inputs ];

  patchPhase = ''
    mkdir -p packages

    find . -type f -iname '*.fsproj' -exec sed -i '/paket.targets/'d '{}'  \;
    find . -type f -iname '*.csproj' -exec sed -i '/paket.targets/'d '{}'  \;
    find . -type f -iname '*.vbproj' -exec sed -i '/paket.targets/'d '{}'  \;

$linkcmds
  '';

  buildPhase = ''
    export FSharpTargetsPath=${fsharp}/lib/mono/4.5/Microsoft.FSharp.Targets
    export TargetFSharpCorePath=${fsharp}/lib/mono/4.5/FSharp.Core.dll
    xbuild /nologo /verbosity:minimal /p:Configuration=""Release"" $name.sln
  '';

  installPhase = ''
    mkdir -p ""$out/lib/mono/packages/$pkgname-$version"";
    cp -rv $outputdir ""$out/lib/mono/packages/$pkgname-$version/$name""
    $exe
  '';
}"

(*----------------------------------------------------------------------------*)
type NixPkg =
  { Type         : ProjectOutputType
  ; Name         : Name
  ; AssemblyName : Name
  ; Version      : SemVerInfo
  ; Method       : Method
  ; Authors      : string list
  ; OutputDir    : string
  ; Description  : string option
  ; Dependencies : NixPkgDep list
  }

  member self.LinkCmds = 
    self.Dependencies
    |> List.map
      (fun pkg -> 
        let s = storePath (sanitize pkg.Name) (pkg.Name.ToLower()) pkg.Version
        sprintf @"    ln -s ""%s/%s"" ""packages/%s""" s pkg.Name pkg.Name)
    |> List.fold (fun m cmd -> m + cmd + "\n") ""

  member self.Names () =
    List.map (fun (p : NixPkgDep)-> p.Name) self.Dependencies

  member self.DepNames () =
    List.map (fun p -> sanitize p) <| self.Names()
  
  interface INixExpr with
    member self.Name  = self.Name
    member self.ToNix () =
      pkgTemplate
      |> spliceFields
          [ ("$name",         self.Name)
          ; ("$pkgname",      self.Name.ToLower())
          ; ("$version",      self.Version.ToString())
          ; ("$homepage",     getUrl self.Method)
          ; ("$maintainers",  quoted self.Authors)
          ; ("$description",  defaultArg self.Description "<empty>")
          ; ("$dependencies", collapse ", " <| self.DepNames())
          ; ("$inputs",       collapse " "  <| self.DepNames())
          ; ("$method",       self.Method.ToString())
          ; ("$linkcmds",     self.LinkCmds)
          ; ("$outputdir",    self.OutputDir)
          ; ("$exe",          exeCmd self.Type self.Name self.Version self.AssemblyName)
          ]


(*----------------------------------------------------------------------------*)
let parseLockFile path =
  LockFile.LoadFrom path


(*----------------------------------------------------------------------------*)
let fetchSha256 (url : string) : Async<string> = 
  async {
    use wc = new WebClient()
  
    let! bytes = wc.AsyncDownloadData(new Uri(url))

    let sum =
      bytes
      |> HashAlgorithm.Create("SHA256").ComputeHash
      |> BitConverter.ToString
      |> (fun result -> result.Replace("-","").ToLower())

    return sum
  }


(*----------------------------------------------------------------------------*)
let downloadUrl (pkgres : PackageResolver.ResolvedPackage) : string =
  let version = pkgres.Version.ToString()
  let name =
    match pkgres.Name with
      | PackageName(_, l) -> l
  sprintf "https://api.nuget.org/packages/%s.%s.nupkg" name version


(*----------------------------------------------------------------------------*)
let pkgToNix (config : AppCnf) (pkgres : PackageResolver.ResolvedPackage) : Async<NixPkgDep> =
  async {
    let name =
      match pkgres.Name with
        | PackageName(u, _) -> u

    let version = pkgres.Version.ToString()
    let url = downloadUrl pkgres

    if config.Checksum && config.Verbose
    then printfn "%s: building checksum" url

    let! result =
      if config.Checksum
      then Async.Catch(fetchSha256 url)
      else async { return Choice1Of2"<empty>" }

    let sha =
      match result with
        | Choice1Of2 sha -> sha
        | Choice2Of2 exn -> exn.Message

    if config.Checksum && config.Verbose
    then printfn "%s: %s" url sha

    return { Name    = name
           ; Version = version
           ; Method  = Nuget(url, sha) }
  }


(*----------------------------------------------------------------------------*)
let parseGroup (config : AppCnf) (group : LockFileGroup) : Async<NixPkgDep> seq =
  List.map (snd >> pkgToNix config) (Map.toList group.Resolution)
  |> List.toSeq


(*----------------------------------------------------------------------------*)
let deps2Nix (config : AppCnf) : Async<NixPkgDep []> =
  Path.Combine(config.Root, Constants.LockFileName)
  |> parseLockFile
  |> (fun lockFile -> Map.toSeq lockFile.Groups)
  |> Seq.map (snd >> parseGroup config)
  |> Seq.fold (fun m l -> Seq.append m l) Seq.empty
  |> Async.Parallel


let compile dest (p : INixExpr) : (string * string) =
  let target = Path.Combine(dest, p.Name)
  if not <| Directory.Exists target
  then Directory.CreateDirectory target |> ignore
  (Path.Combine(target, "default.nix"), p.ToNix())

(*----------------------------------------------------------------------------*)
let writeFiles (config : AppCnf) (projs : NixPkg array) (deps : NixPkgDep array) : unit =
  deps
  |> Array.map (compile config.Destination)
  |> Array.append (Array.map (compile config.Destination) projs)
  |> Array.iter File.WriteAllText
  |> ignore

  printfn "done!"

(*----------------------------------------------------------------------------*)
let mkNixPkg (cnf : AppCnf) (t, n : string, an : string, v, a, (u : string), d, od, ds) : Async<NixPkg> = 
  async {
    let url =
      if Option.isSome(cnf.Url)
      then Option.get(cnf.Url)
      else if u.Contains("github") then u + "/archive/master.tar.gz" else u

    if cnf.Checksum && cnf.Verbose
    then printfn "downloading and building checksum for %s" n

    let! res =
      if cnf.Checksum
      then Async.Catch(fetchSha256 url)
      else async { return Choice1Of2 "<empty>" }

    let meth =
      match res with
       | Choice1Of2 sha -> Nuget(url, sha)
       | Choice2Of2 _   -> Nuget(url, "<empty>")

    return { Type         = t
           ; Name         = n
           ; AssemblyName = an
           ; Version      = v
           ; Method       = meth
           ; Authors      = a
           ; Description  = d
           ; OutputDir    = od
           ; Dependencies = ds
           }
    }

(*----------------------------------------------------------------------------*)
let readProject (config : AppCnf) (tmpl : TemplateFile, project : ProjectFile, deps : NixPkgDep list) : Async<NixPkg> =
  let relPath =
    Path.GetDirectoryName(project.FileName)
        .Replace(Environment.CurrentDirectory +
                 Path.DirectorySeparatorChar.ToString(),"")

  let apath =
    Path.Combine(relPath,
                 project.GetOutputDirectory "Release",
                 project.GetAssemblyName())

  let defVersion =
    try
      SemVer.Parse(Assembly
                  .LoadFile(apath)
                  .GetName()
                  .Version
                  .ToString())
    with
      | _ -> SemVer.Parse("0.0.0.0")

  match tmpl.Contents with
    | CompleteInfo(core, optInfo) ->
      ( project.OutputType
      , project.NameWithoutExtension
      , project.GetAssemblyName()
      , defaultArg core.Version defVersion
      , core.Authors
      , defaultArg optInfo.ProjectUrl "<empty>"
      , Some(core.Description)
      , Path.Combine(relPath, project.GetOutputDirectory "Release")
      , deps
      )
    | ProjectInfo(core, optInfo) ->
      ( project.OutputType
      , project.NameWithoutExtension
      , project.GetAssemblyName()
      , defaultArg core.Version defVersion
      , defaultArg core.Authors List.empty
      , defaultArg optInfo.ProjectUrl "<empty>"
      , core.Description
      , Path.Combine(relPath, project.GetOutputDirectory "Release")
      , deps 
      )
  |> mkNixPkg config


(*----------------------------------------------------------------------------*)
let findProject (tmpl : TemplateFile) (projects : ProjectFile array) : ProjectFile =
  let basePath = Path.GetDirectoryName(tmpl.FileName)
  Array.find (fun p -> Path.GetDirectoryName(p.FileName) = basePath) projects


(*----------------------------------------------------------------------------*)
let getDeps (tmpl : TemplateFile) (deps : Dependencies) (pkgs : NixPkgDep array) : NixPkgDep list = 
  let path = Path.Combine(Path.GetDirectoryName(tmpl.FileName), Constants.ReferencesFile)
  if File.Exists path
  then
    deps.GetDirectDependencies(ReferencesFile.FromFile(path))
    |> List.map(fun (_, n, _) -> Array.find (fun pkg -> pkg.Name = n) pkgs)
  else List.empty


(*----------------------------------------------------------------------------*)
let listProjects (config : AppCnf) (pkgs : NixPkgDep array) : Async<NixPkg> seq =
  let deps = new Dependencies(Path.Combine(config.Root, Constants.DependenciesFileName))
  (deps.ListTemplateFiles(), ProjectFile.FindAllProjects(config.Root))
  |> (fun (tmpls, projs) ->
      List.map (fun tmpl -> (tmpl, findProject tmpl projs, Array.toList pkgs)) tmpls)
  |> List.map (readProject config)
  |> List.toSeq


(*----------------------------------------------------------------------------*)
let internal body = @"
with import <nixpkgs> {};
{
$deps
}"


(*----------------------------------------------------------------------------*)
let private callPackage (san, norm, args) =
  sprintf "   %s = callPackage ./%s %s;" san norm args


(*----------------------------------------------------------------------------*)
let private mkDeps (dependencies : string list) : string =
  let sep = Environment.NewLine
  List.fold (fun m name -> callPackage (sanitize name, name, "{}")
                           |> sprintf "%s%s %s" m sep)
  |> (fun fold -> fold "" dependencies)
  |> (fun res -> sprintf "{%s %s %s}" sep res sep)


(*----------------------------------------------------------------------------*)
let createTopLevel (config : AppCnf) (projs : NixPkg array) : unit =
  Array.map (fun p -> (sanitize p.Name, p.Name, mkDeps (p.Names()))) projs
  |> Array.map callPackage
  |> Array.toSeq
  |> String.concat Environment.NewLine
  |> (fun it -> body.Replace("$deps", it))
  |> (fun top -> File.WriteAllText(Path.Combine(config.Destination, "default.nix"), top))
