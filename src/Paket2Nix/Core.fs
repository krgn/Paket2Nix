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
open System.Net
open Paket


let parseLockFile path =
  LockFile.LoadFrom path
  |> (fun f -> f.ToString ())
  |> printfn "%s"


let fetchSha256 (url : string) : string = 
  let wc = new WebClient()
  let path = Path.GetTempFileName()

  wc.DownloadFile(url, path)

  File.ReadAllBytes(path)
  |> HashAlgorithm.Create("SHA256").ComputeHash
  |> BitConverter.ToString
  |> (fun result -> result.Replace("-","").ToLower())


let paket2Nix _ = failwith "FIXME"

