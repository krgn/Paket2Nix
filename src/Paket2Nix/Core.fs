module Paket2Nix.Core

open Paket

let hello str = failwith str

let parseLockFile path =
  LockFile.LoadFrom path
  |> (fun f -> f.ToString ())
  |> printfn "%s"
