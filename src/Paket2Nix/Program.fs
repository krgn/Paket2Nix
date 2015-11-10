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

open System.IO
open Paket
open Paket2Nix.Core

[<EntryPoint>]
let main _ =
  let root = "."
  let destination = Path.Combine(root, "nix")

  if not <| File.Exists (Path.Combine(root, Constants.LockFileName))
  then failwith "paket.lock file not found! Please run from project root."

  let packages = 
    deps2Nix root
    |> Async.RunSynchronously

  let projects = 
    listProjects root packages
    |> Async.Parallel
    |> Async.RunSynchronously

  writeFiles destination projects packages
  createTopLevel destination projects

  0
