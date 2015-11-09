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

let serializeType (t : ProjectOutputType) : string =
  match t with
    | ProjectOutputType.Exe -> "exe"
    | ProjectOutputType.Library -> "library"

type Project =
  {  Type                     : ProjectOutputType
  ;  Name                     : string
  ;  AssemblyName             : string
  ;  Owners                   : string list
  ;  Authors                  : string list
  ;  Url                      : string option
  ;  IconUrl                  : string option
  ;  LicenseUrl               : string option
  ;  RequireLicenseAcceptance : bool
  ;  Copyright                : string option
  ;  Tags                     : string list
  ;  Summary                  : string option
  ;  Description              : string option
  ;  Dependencies             : (string * string * string) list
  }
  with
    override self.ToString () =
      sprintf @"
      --------------------------
      Type                     = %s
      Name                     = %s
      AssemblyName             = %s
      Owners                   = %s
      Authors                  = %s
      Url                      = %s
      IconUrl                  = %s
      LicenseUrl               = %s
      RequireLicenseAcceptance = %s
      Copyright                = %s
      Tags                     = %s
      Summary                  = %s
      Description              = %s
      Dependencies             = %s
      --------------------------
      "
        <| serializeType self.Type
        <| self.Name
        <| self.AssemblyName
        <| self.Owners.ToString()
        <| self.Authors.ToString()
        <| self.Url.ToString()
        <| self.IconUrl.ToString()
        <| self.LicenseUrl.ToString()
        <| self.RequireLicenseAcceptance.ToString()
        <| self.Copyright.ToString()
        <| self.Tags.ToString()
        <| self.Summary.ToString()
        <| self.Description.ToString()
        <| self.Dependencies.ToString()
   

(* slightly nicer way of replacing strings *)
let internal replace (from : string) (two : string) (target : string) =
  target.Replace(from,two)

(*
  Template function for a git-based `src` section.
*)
let internal gitTmpl (url : Url) (sha : Sha256) (rev : Rev) =
  @"fetchgit {
    url    = ""$url"";
    sha256 = ""$sha"";
    rev    = ""$rev"";
  }"
  |> replace "$url" url
  |> replace "$sha" sha
  |> replace "$rev" rev

(*
  Template function for a nuget-based `src` section.
*)
let internal nugetTmpl (url : Url) (sha : Sha256) =
  @"fetchurl {
    url    = ""$url"";
    sha256 = ""$sha"";
  }"
  |> replace "$url" url
  |> replace "$sha" sha


(* A type to encode the different available repository methods. *)  
type Method =
  | Nuget  of url : Url * sha256 : Sha256
  | Github of url : Url * sha256 : Sha256 * rev : Rev

  with
    override self.ToString () =
      match self with
        | Nuget(u, s)     -> nugetTmpl u s
        | Github(u, s, r) -> gitTmpl u s r

(*----------------------------------------------------------------------------*)
let internal nixPkgTmpl (name : Name) (version : Version) (meth : Method) (deps : string list) =
  @"
{ stdenv, fetchgit, fetchurl, unzip $args }:

stdenv.mkDerivation {
  name = ""$pkgname-$version"";

  src = $method;

  phases = [ ""unpackPhase"" ];

  buildInputs = [ unzip $inputs ];

  unpackPhase = ''
    mkdir -p ""$out/lib/mono/packages/$pkgname-$version/$name"";
    unzip -x ""$src"" -d ""$out/lib/mono/packages/$pkgname-$version/$name"";
  '';
}
"
  |> replace "$args"    (List.fold (fun m i -> m + ", " + i) "" deps)
  |> replace "$inputs"  (List.fold (fun m i -> m + " " + i)  "" deps)
  |> replace "$pkgname" (name.ToLower())
  |> replace "$name"    name
  |> replace "$version" version
  |> replace "$method"  (meth.ToString())


(*----------------------------------------------------------------------------*)
type NixPkg =
  { name    : Name
  ; version : Version
  ; meth    : Method
  ; deps    : NixPkg list
  }
  with
   override self.ToString () =
      nixPkgTmpl self.name
                 self.version
                 self.meth
                 (List.map (fun pkg -> pkg.name) self.deps)


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
let getUrl (pkgres : PackageResolver.ResolvedPackage) : string =
  let version = pkgres.Version.ToString()
  let name =
    match pkgres.Name with
      | PackageName(_, l) -> l
  sprintf "https://api.nuget.org/packages/%s.%s.nupkg" name version


(*----------------------------------------------------------------------------*)
let pkgToNix (pkgres : PackageResolver.ResolvedPackage) : Async<NixPkg> =
  async {
    let name =
      match pkgres.Name with
        | PackageName(u, _) -> u

    let version = pkgres.Version.ToString()
    let url = getUrl pkgres

    printfn "downloading resource: %s" url

    let! sha = fetchSha256 url

    return { name    = name
           ; version = version
           ; meth    = Nuget(url, sha)
           ; deps    = List.empty }
  }


(*----------------------------------------------------------------------------*)
let parseGroup (group : LockFileGroup) : seq<Async<NixPkg>> =
  List.map (snd >> pkgToNix) (Map.toList group.Resolution)
  |> List.toSeq


(*----------------------------------------------------------------------------*)
let paket2Nix (lockFile : LockFile) =
  Map.toSeq lockFile.Groups
  |> Seq.map (snd >> parseGroup)
  |> Seq.fold (fun m l -> Seq.append m l) Seq.empty
  |> Async.Parallel


(*----------------------------------------------------------------------------*)
let writeToDisk (dest : string) (pkgs : NixPkg array) : unit =
  pkgs
  |> Array.map
    (fun p ->
     let target = Path.Combine(dest, p.name)
     if not <| Directory.Exists target
     then Directory.CreateDirectory target |> ignore
     (Path.Combine(target, "default.nix"), p.ToString()))
  |> Array.iter File.WriteAllText
  |> ignore


(*----------------------------------------------------------------------------*)
let mkProject (t, n, an, o, a, u, i, l, r, c, ta, s, d, ds) = 
  {  Type                     = t
  ;  Name                     = n
  ;  AssemblyName             = an
  ;  Owners                   = o
  ;  Authors                  = a
  ;  Url                      = u
  ;  IconUrl                  = i
  ;  LicenseUrl               = l
  ;  RequireLicenseAcceptance = r
  ;  Copyright                = c
  ;  Tags                     = ta
  ;  Summary                  = s
  ;  Description              = d
  ;  Dependencies             = ds
  }


(*----------------------------------------------------------------------------*)
let readProject (tmpl : TemplateFile, project : ProjectFile, deps : (string * string * string) list) : Project =
  match tmpl.Contents with
    | CompleteInfo(core, optInfo) ->
      ( project.OutputType
      , project.Name
      , project.GetAssemblyName()
      , optInfo.Owners
      , core.Authors
      , optInfo.ProjectUrl
      , optInfo.IconUrl
      , optInfo.LicenseUrl
      , optInfo.RequireLicenseAcceptance
      , optInfo.Copyright
      , optInfo.Tags
      , optInfo.Summary
      , Some(core.Description)
      , deps
      )
    | ProjectInfo(core, optInfo) ->
      ( project.OutputType
      , project.Name
      , project.GetAssemblyName()
      , optInfo.Owners
      , defaultArg core.Authors List.empty
      , optInfo.ProjectUrl
      , optInfo.IconUrl
      , optInfo.LicenseUrl
      , optInfo.RequireLicenseAcceptance
      , optInfo.Copyright
      , optInfo.Tags
      , optInfo.Summary
      , core.Description
      , deps
      )
  |> mkProject 


(*----------------------------------------------------------------------------*)
let findProject (tmpl : TemplateFile) (projects : ProjectFile array) : ProjectFile =
  let basePath = Path.GetDirectoryName(tmpl.FileName)
  Array.find (fun p -> Path.GetDirectoryName(p.FileName) = basePath) projects

let getDeps (tmpl : TemplateFile) (deps : Dependencies) : (string * string * string) list = 
  let path = Path.Combine(Path.GetDirectoryName(tmpl.FileName), Constants.ReferencesFile)
  if File.Exists path
  then deps.GetDirectDependencies(ReferencesFile.FromFile(path))
  else List.empty

(*----------------------------------------------------------------------------*)
let listProjects (root : string) : Project list =
  let deps = new Dependencies(Path.Combine(root, Constants.DependenciesFileName))

  (deps.ListTemplateFiles(), ProjectFile.FindAllProjects(root))
  |> (fun (tmpls, projs) ->
      List.map (fun tmpl -> (tmpl, findProject tmpl projs, getDeps tmpl deps)) tmpls)
  |> List.map readProject


// let project2Nix (project : Project) : NixPkg =
//   {  Type                     : ProjectOutputType
//   ;  Name                     : string
//   ;  AssemblyName             : string
//   ;  Owners                   : string list
//   ;  Authors                  : string list
//   ;  Url                      : string option
//   ;  IconUrl                  : string option
//   ;  LicenseUrl               : string option
//   ;  RequireLicenseAcceptance : bool
//   ;  Copyright                : string option
//   ;  Tags                     : string list
//   ;  Summary                  : string option
//   ;  Description              : string option
//   ;  Dependencies             : (string * string * string) list
//   }

(*----------------------------------------------------------------------------*)
let internal body = @"
with import <nixpkgs> {};
{
  $deps
}"


(*----------------------------------------------------------------------------*)
let private sanitize (str : string) : string = str.Replace(".","")


(*----------------------------------------------------------------------------*)
let private callPackage pkg =
  sprintf "%s = callPackage ./%s {};" (sanitize pkg.name) pkg.name


(*----------------------------------------------------------------------------*)
let createTopLevel (dest : string) (pkgs : NixPkg array) : unit =

  let topLevel =
    Array.map callPackage (pkgs)
    |> Array.toSeq
    |> String.concat "\n"
    |> (fun it -> body.Replace("$deps", it))

  File.WriteAllText(Path.Combine(dest, "top.nix"), topLevel)

