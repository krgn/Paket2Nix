
{ stdenv, fetchgit, fetchurl, fsharp, mono , FAKE, FSharpCompilerService, FSharpCore, FSharpFormatting, FSharpVSPowerToolsCore, MicrosoftBcl, MicrosoftBclBuild, MicrosoftNetHttp, NewtonsoftJson, NUnit, NUnitRunners, Octokit, PaketCore, SourceLinkFake }:

stdenv.mkDerivation {
  name = "paket2nix-0.0.1";

  src = fetchurl {
    url    = "http://github.com/krgn/Paket2Nix/archive/master.tar.gz";
    sha256 = "a336b3b3aee3825e41cdca7b55d76ad308648bcfceb7de8a7d967dc096d92c07";
  };

  meta = {
    homepage = "http://github.com/krgn/Paket2Nix/archive/master.tar.gz";
    description = "Helper to turn nuget/paket packages into nix packages.";
    maintainers = [  "Karsten Gebbert" ];
  };

  phases = [ "unpackPhase" "patchPhase" "buildPhase" "installPhase" ];

  buildInputs = [ fsharp mono  FAKE FSharpCompilerService FSharpCore FSharpFormatting FSharpVSPowerToolsCore MicrosoftBcl MicrosoftBclBuild MicrosoftNetHttp NewtonsoftJson NUnit NUnitRunners Octokit PaketCore SourceLinkFake ];

  patchPhase = ''
    mkdir -p packages

    find . -type f -iname '*.fsproj' -exec sed -i '/paket.targets/'d '{}'  \;
    find . -type f -iname '*.csproj' -exec sed -i '/paket.targets/'d '{}'  \;
    find . -type f -iname '*.vbproj' -exec sed -i '/paket.targets/'d '{}'  \;

    ln -s "${FAKE}/lib/mono/packages/fake-4.7.3/FAKE" "packages/FAKE"
    ln -s "${FSharpCompilerService}/lib/mono/packages/fsharp.compiler.service-1.4.0.6/FSharp.Compiler.Service" "packages/FSharp.Compiler.Service"
    ln -s "${FSharpCore}/lib/mono/packages/fsharp.core-4.0.0.1/FSharp.Core" "packages/FSharp.Core"
    ln -s "${FSharpFormatting}/lib/mono/packages/fsharp.formatting-2.12.0/FSharp.Formatting" "packages/FSharp.Formatting"
    ln -s "${FSharpVSPowerToolsCore}/lib/mono/packages/fsharpvspowertools.core-2.1.0/FSharpVSPowerTools.Core" "packages/FSharpVSPowerTools.Core"
    ln -s "${MicrosoftBcl}/lib/mono/packages/microsoft.bcl-1.1.10/Microsoft.Bcl" "packages/Microsoft.Bcl"
    ln -s "${MicrosoftBclBuild}/lib/mono/packages/microsoft.bcl.build-1.0.21/Microsoft.Bcl.Build" "packages/Microsoft.Bcl.Build"
    ln -s "${MicrosoftNetHttp}/lib/mono/packages/microsoft.net.http-2.2.29/Microsoft.Net.Http" "packages/Microsoft.Net.Http"
    ln -s "${NewtonsoftJson}/lib/mono/packages/newtonsoft.json-7.0.1/Newtonsoft.Json" "packages/Newtonsoft.Json"
    ln -s "${NUnit}/lib/mono/packages/nunit-2.6.4/NUnit" "packages/NUnit"
    ln -s "${NUnitRunners}/lib/mono/packages/nunit.runners-2.6.4/NUnit.Runners" "packages/NUnit.Runners"
    ln -s "${Octokit}/lib/mono/packages/octokit-0.16.0/Octokit" "packages/Octokit"
    ln -s "${PaketCore}/lib/mono/packages/paket.core-2.23.0/Paket.Core" "packages/Paket.Core"
    ln -s "${SourceLinkFake}/lib/mono/packages/sourcelink.fake-1.1.0/SourceLink.Fake" "packages/SourceLink.Fake"

  '';

  buildPhase = ''
    export FSharpTargetsPath=${fsharp}/lib/mono/4.5/Microsoft.FSharp.Targets
    export TargetFSharpCorePath=${fsharp}/lib/mono/4.5/FSharp.Core.dll
    xbuild /nologo /verbosity:minimal /p:Configuration="Release" Paket2Nix.sln
  '';

  installPhase = ''
    mkdir -p "$out/lib/mono/packages/paket2nix-0.0.1";
    cp -rv src/Paket2Nix/bin/Release "$out/lib/mono/packages/paket2nix-0.0.1/Paket2Nix"
    
    mkdir -p "$out/bin";
    cat > "$out/bin/Paket2Nix" <<-WRAPPER
    #!/usr/bin/env bash
    ${mono}/bin/mono $out/lib/mono/packages/paket2nix-0.0.1/Paket2Nix/Paket2Nix.exe
    WRAPPER
    chmod a+x "$out/bin/Paket2Nix" 
  '';
}