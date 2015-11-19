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

module Paket2Nix.Main

open System
open System.IO
open Paket
open Paket2Nix.Core
open Nessos.Argu

type Args =
  | Checksum 
  | Verbose
  | Download_Url of string
  | Dest_Dir     of string
  | Project_Dir  of string
  with
    interface IArgParserTemplate with
      member s.Usage =
        match s with
        | Project_Dir  _ -> "specify a working directory."
        | Dest_Dir     _ -> "specify an output direcory for created derivations (defaults to ./nix)"
        | Download_Url _ -> "where to download a tarball from"
        | Verbose      _ -> "verbose output"
        | Checksum     _ -> "attempt to download and calculate checksums for packages"

let parser = ArgumentParser.Create<Args>()

// get usage text
let usage = parser.Usage()

[<EntryPoint>]
let main raw =

  let args =
    try parser.Parse(raw)
    with
      | exn -> bail exn.Message

  let config =
    let root = args.GetResult(<@ Project_Dir @>, defaultValue = ".")
    { Root        = root
    ; Url         = if args.Contains <@ Download_Url @> then Some(args.GetResult(<@ Download_Url @>)) else None
    ; Verbose     = args.Contains <@ Verbose @>
    ; Checksum    = args.Contains <@ Checksum @>
    ; Destination = args.GetResult(<@ Dest_Dir @>, defaultValue = Path.Combine(root, "nix"))
    }

  if not <| File.Exists (Path.Combine(config.Root, Constants.LockFileName))
  then bail "paket.lock file not found! Please specify --working-directory=/path/to/project or run from project root."

  let packages = 
    deps2Nix config
    |> Async.RunSynchronously

  let projects = 
    listProjects config packages
    |> Async.Parallel
    |> Async.RunSynchronously

  writeFiles config projects packages
  createTopLevel config projects

  0
