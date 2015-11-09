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

  with
    override self.ToString () =
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
"
  |> spliceFields
       [ ("$pkgname",  (name.ToLower()))
       ; ("$name"   ,  name            )
       ; ("$version",  version         )
       ; ("$method" ,  meth.ToString() )
       ]

(*----------------------------------------------------------------------------*)
type NixPkgDep =
  { Name         : Name
  ; Version      : Version
  ; Method       : Method
  }
  with
   override self.ToString () =
      depPkgTmpl self.Name
                 self.Version
                 self.Method


(*----------------------------------------------------------------------------*)
let private collapse (sep : string)  =
  List.fold (fun m p -> m + sep + p) ""

let private storePath =
  sprintf @"${%s}/lib/mono/packages/%s-%s"
   
(*----------------------------------------------------------------------------*)
type NixPkg =
  { Type         : ProjectOutputType
  ; Name         : Name
  ; AssemblyName : Name
  ; Version      : SemVerInfo
  ; Method       : Method
  ; Authors      : string list
  ; Description  : string option
  ; Dependencies : NixPkgDep list
  }
  with
    member self.GetUrl ()  =
      match self.Method with
        | Nuget(url, _) -> url
        | Github(url, _, _) -> url

    member self.StorePath () =
      storePath <| sanitize self.Name
                <| self.Name.ToLower()
                <| self.Version.ToString()

    member self.LinkCmds () =
      self.Dependencies
      |> List.map
        (fun pkg -> 
          storePath (sanitize pkg.Name) (pkg.Name.ToLower()) pkg.Version
          |> sprintf @"    ln -s ""%s/*"" ""$src/packages/""")
      |> List.fold (fun m cmd -> m + cmd + "\n") ""
            
    member self.ExeCmd () =
      match self.Type with
        | ProjectOutputType.Exe ->
          sprintf "#!/bin/sh\n ${mono}/bin/mono %s/%s/%s" (self.StorePath()) self.Name self.AssemblyName
        | _ -> ""

    member self.DepNames () =
      List.map (fun (p : NixPkgDep)-> sanitize p.Name) self.Dependencies
   
    override self.ToString () =
      @"
{ stdenv, fetchgit, fetchurl, mono, unzip $dependencies }:

stdenv.mkDerivation {
  name = ""$pkgname-$version"";

  src = $method;

  meta = {
    homepage = ""$homepage"";
    description = ""$description"";
    maintainers = [ $maintainers ];
  };

  phases = [ ""patchPhase"" ""buildPhase"" ""installPhase"" ];

  buildInputs = [ mono unzip $inputs ];

  patchPhase = ''
$linkcmds
  '';

  buildPhase = ''
    ./build.sh
  '';

  installPhase = ''
    mkdir -p ""$out/lib/mono/packages/$pkgname-$version"";
    cp -rv ./bin/* ""$out/lib/mono/packages/$pkgname-$version/$name""
    $exe
  '';
}"
     |> spliceFields
         [ ("$name",         self.Name)
         ; ("$pkgname",      self.Name.ToLower())
         ; ("$version",      self.Version.ToString())
         ; ("$homepage",     self.GetUrl())
         ; ("$description",  defaultArg self.Description "<empty>")
         ; ("$dependencies", collapse ", " <| self.DepNames())
         ; ("$inputs",       collapse " "  <| self.DepNames())
         ; ("$method",       self.Method.ToString())
         ; ("$linkcmds",     self.LinkCmds())
         ; ("$exe",          self.ExeCmd())
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
let pkgToNix (pkgres : PackageResolver.ResolvedPackage) : Async<NixPkgDep> =
  async {
    let name =
      match pkgres.Name with
        | PackageName(u, _) -> u

    let version = pkgres.Version.ToString()
    let url = downloadUrl pkgres

    printfn "%s: building checksum" url

    let! result = Async.Catch(fetchSha256 url) 

    let sha =
      match result with
        | Choice1Of2 sha -> sha
        | Choice2Of2 exn -> exn.Message

    printfn "%s: %s" url sha

    return { Name    = name
           ; Version = version
           ; Method  = Nuget(url, sha) }
  }


(*----------------------------------------------------------------------------*)
let parseGroup (group : LockFileGroup) : Async<NixPkgDep> seq =
  List.map (snd >> pkgToNix) (Map.toList group.Resolution)
  |> List.toSeq


(*----------------------------------------------------------------------------*)
let deps2Nix (root : String) : Async<NixPkgDep []> =
  Path.Combine(root, Constants.LockFileName)
  |> parseLockFile
  |> (fun lockFile -> Map.toSeq lockFile.Groups)
  |> Seq.map (snd >> parseGroup)
  |> Seq.fold (fun m l -> Seq.append m l) Seq.empty
  |> Async.Parallel


(*----------------------------------------------------------------------------*)
let writeFiles (dest : string) (projs : NixPkg array) (deps : NixPkgDep array) : unit =
  printfn "Writing out dependencies"
  deps
  |> Array.map
    (fun p ->
     let target = Path.Combine(dest, p.Name)
     if not <| Directory.Exists target
     then Directory.CreateDirectory target |> ignore
     (Path.Combine(target, "default.nix"), p.ToString()))
  |> Array.iter File.WriteAllText
  |> ignore

  printfn "... and projects ..."
  projs
  |> Array.map
    (fun p ->
     let target = Path.Combine(dest, p.Name)
     if not <| Directory.Exists target
     then Directory.CreateDirectory target |> ignore
     (Path.Combine(target, "default.nix"), p.ToString()))
  |> Array.iter File.WriteAllText
  |> ignore

  printfn "done!"


(*----------------------------------------------------------------------------*)
let mkNixPkg (t, n : string, an : string, v, a, (u : string), d, ds) : Async<NixPkg> = 
  async {
    let url = if u.Contains("github") then u + "/archive/master.tar.gz" else u
    let! res = Async.Catch(fetchSha256 url)

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
           ; Dependencies = ds
           }
    }

(*----------------------------------------------------------------------------*)
let readProject (tmpl : TemplateFile, project : ProjectFile, deps : NixPkgDep list) : Async<NixPkg> =
  let defVersion = SemVer.Parse("0.0.1")

  match tmpl.Contents with
    | CompleteInfo(core, optInfo) ->
      ( project.OutputType
      , project.NameWithoutExtension
      , project.GetAssemblyName()
      , defaultArg core.Version defVersion
      , core.Authors
      , defaultArg optInfo.ProjectUrl "<empty>"
      , Some(core.Description)
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
      , deps
      )
  |> mkNixPkg


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
let listProjects (root : string) (pkgs : NixPkgDep array) : Async<NixPkg> seq =
  let deps = new Dependencies(Path.Combine(root, Constants.DependenciesFileName))

  (deps.ListTemplateFiles(), ProjectFile.FindAllProjects(root))
  |> (fun (tmpls, projs) ->
      List.map (fun tmpl -> (tmpl, findProject tmpl projs, getDeps tmpl deps pkgs)) tmpls)
  |> List.map readProject
  |> List.toSeq


(*----------------------------------------------------------------------------*)
let internal body = @"
with import <nixpkgs> {};
{
  $deps
}"


(*----------------------------------------------------------------------------*)
let private callPackage (san, norm, args) =
  sprintf "%s = callPackage ./%s %s;" san norm args

let private mkDeps (deps : string list) : string =
  sprintf "{ %s }" <| List.fold (fun m n -> m + " " + n) "" deps

(*----------------------------------------------------------------------------*)
let createTopLevel (dest : string) (projs : NixPkg array) (deps : NixPkgDep array) : unit =
  let namePairs =
    Array.append <| Array.map (fun (p : NixPkgDep) -> (sanitize p.Name, p.Name, "{}")) deps
                 <| Array.map (fun (p : NixPkg)    -> (sanitize p.Name, p.Name, mkDeps (p.DepNames()))) projs
  let topLevel =
    Array.map callPackage namePairs
    |> Array.toSeq
    |> String.concat "\n"
    |> (fun it -> body.Replace("$deps", it))

  File.WriteAllText(Path.Combine(dest, "top.nix"), topLevel)
