module Paket2Nix.Main

open Paket2Nix.Core

[<EntryPoint>]
let main args =
  parseLockFile "paket.lock"
  0
  
