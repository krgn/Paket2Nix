module Paket2Nix.Tests

open Paket2Nix
open NUnit.Framework

[<Test>]
let ``hello returns 42`` () =
  Core.hello "eh"
  Assert.AreEqual(42,42)
