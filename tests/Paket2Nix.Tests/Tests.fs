module Paket2Nix.Tests

open Paket2Nix.Core
open NUnit.Framework

open System.IO

let lf1 =
  @"NUGET
  remote: https://nuget.org/api/v2
  specs:
    Newtonsoft.Json (7.0.1)"

let nf1 =
  @"with import <nixpkgs> {};

stdenv.mkDerivation {
  name = ""newtonsoft.json-7.0.1"";

  src = fetchurl {
    url = ""https://api.nuget.org/packages/newtonsoft.json.7.0.1.nupkg"";
    sha256 = ""ff2a9942325b22cccfe3e505ac8abdf46b071bcc60ef44da464df929c60fc846"";
  };

  phases = [ ""unpackPhase"" ];

  buildInputs = [ unzip ];

  unpackPhase = ''
    mkdir -p ""$out/lib/mono/packages/newtonsoft.json-7.0.1""
    unzip -x $src -d ""$out/lib/mono/packages/newtonsoft.json-7.0.1""
  '';
}
"

[<Test>]
let ``github dependency should serialize correctly`` () =
  let path = Path.GetTempFileName()
  use tmpFile = File.OpenWrite path
  tmpFile.Write (System.Text.Encoding.ASCII.GetBytes(lf1), 0, 0)
  tmpFile.Close ()

  let nix = paket2Nix tmpFile

  Assert.AreEqual(42,42)

[<Test>]
let ``nuget dependency should serialize correctly`` () =
  let path = Path.GetTempFileName();
  use tmpFile = File.OpenWrite path
  tmpFile.Write (System.Text.Encoding.ASCII.GetBytes(lf1), 0, 0)
  tmpFile.Close ()

  let nix = paket2Nix tmpFile

  Assert.AreEqual(42,42)

[<Test>]
let ``should synthesise correct sha256 hash for a package`` () =
  let expect = "ff2a9942325b22cccfe3e505ac8abdf46b071bcc60ef44da464df929c60fc846"
  let result = fetchSha256 "https://www.nuget.org/api/v2/package/Newtonsoft.Json/7.0.1"
  Assert.AreEqual(expect,result)
