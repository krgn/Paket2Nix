
{ stdenv, fetchgit, fetchurl, unzip }:

stdenv.mkDerivation {
  name = "fake-4.7.3";

  src = fetchurl {
    url    = "https://api.nuget.org/packages/fake.4.7.3.nupkg";
    sha256 = "1cf2b659cce1b9a4667616e617acc3e57860e697538abb625e7fb1582b769feb";
  };

  phases = [ "unpackPhase" ];

  buildInputs = [ unzip ];

  unpackPhase = ''
    mkdir -p "$out/lib/mono/packages/fake-4.7.3/FAKE";
    unzip -x "$src" -d "$out/lib/mono/packages/fake-4.7.3/FAKE";
  '';
}
