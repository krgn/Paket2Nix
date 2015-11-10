
{ stdenv, fetchgit, fetchurl, unzip }:

stdenv.mkDerivation {
  name = "paket.core-2.23.0";

  src = fetchurl {
    url    = "https://api.nuget.org/packages/paket.core.2.23.0.nupkg";
    sha256 = "66cb064e0cd37c448c48d81fe3f07ecaa5cfe1434117ccbbe840506b92d35660";
  };

  phases = [ "unpackPhase" ];

  buildInputs = [ unzip ];

  unpackPhase = ''
    mkdir -p "$out/lib/mono/packages/paket.core-2.23.0/Paket.Core";
    unzip -x "$src" -d "$out/lib/mono/packages/paket.core-2.23.0/Paket.Core";
  '';
}
