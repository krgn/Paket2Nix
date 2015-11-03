// Learn more about F# at http://fsharp.org. See the 'F# Tutorial' project
// for more guidance on F# programming.

#r @"../../packages/Paket.Core/lib/net45/Paket.Core.dll"
#load "Core.fs"
open Paket2Nix.Core

parseLockFile @"../../paket.lock"
