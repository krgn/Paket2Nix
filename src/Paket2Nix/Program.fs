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
open Paket2Nix.Core

[<EntryPoint>]
let main _ =
  if not <| File.Exists "./paket.lock"
  then failwith "paket.lock file not found! Are you in the project root?"

  let lockFile = parseLockFile "./paket.lock"

  let packages = 
    paket2Nix lockFile
    |> Async.RunSynchronously

  let dest = "./nix"

  writeToDisk dest packages
  createTopLevel dest packages
  
  0
