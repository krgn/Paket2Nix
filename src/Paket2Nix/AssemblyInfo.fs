﻿namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("Paket2Nix")>]
[<assembly: AssemblyProductAttribute("Paket2Nix")>]
[<assembly: AssemblyDescriptionAttribute("Convert Paket Projects Into Nix Expressions")>]
[<assembly: AssemblyVersionAttribute("0.0.2")>]
[<assembly: AssemblyFileVersionAttribute("0.0.2")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "0.0.2"
