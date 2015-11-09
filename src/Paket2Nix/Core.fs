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

type Project =
  {  Type                     : string
  ;  PackageName              : string
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
  }

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
      nixPkgTmpl self.name self.version self.meth (List.map (fun pkg -> pkg.name) self.deps)


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
let mkProject (n, o, a, u, i, l, r, c, ta, s, d) = 
  {  Type                     = "exe"
  ;  PackageName              = n
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
  }


(*----------------------------------------------------------------------------*)
let readProject (tmpl : TemplateFile) : Project =
  match tmpl.Contents with
    | CompleteInfo(core, optInfo) ->
      ( core.PackageFileName
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
      )
    | ProjectInfo(core, optInfo) ->
      ( defaultArg optInfo.Title "<empty>"
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
      )
  |> mkProject 


(*----------------------------------------------------------------------------*)
let findProjects (root : string) : Project list =
  let deps = new Dependencies(Path.Combine(root, Constants.DependenciesFileName))
  deps.ListTemplateFiles()
  |> List.map readProject


(*----------------------------------------------------------------------------*)
let internal body = @"
with import <nixpkgs> {};
{
  $deps
}"

(*----------------------------------------------------------------------------*)
let createTopLevel (dest : string) (pkgs : NixPkg array) : unit =
  let sanitize (str : string) : string = str.Replace(".","")
  let line pkg = sprintf "%s = callPackage ./%s {};" (sanitize pkg.name) pkg.name
  Array.map line pkgs
  |> Array.toSeq
  |> String.concat "\n"
  |> (fun it -> body.Replace("$deps", it))
  |> (fun res -> File.WriteAllText(Path.Combine(dest, "top.nix"), res))

