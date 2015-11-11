module Paket2Nix.Tests

open Paket2Nix.Core
open NUnit.Framework
open Paket
open System.IO

[<Test>]
let ``should synthesise correct sha256 hash for a package`` () =
  let expect = "ff2a9942325b22cccfe3e505ac8abdf46b071bcc60ef44da464df929c60fc846"
  let result =
    fetchSha256 "https://www.nuget.org/api/v2/package/Newtonsoft.Json/7.0.1"
    |> Async.RunSynchronously
  Assert.AreEqual(expect,result)

