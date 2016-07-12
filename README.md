[![Issue Stats](http://issuestats.com/github/krgn/Paket2Nix/badge/issue)](http://issuestats.com/github/krgn/Paket2Nix)
[![Issue Stats](http://issuestats.com/github/krgn/Paket2Nix/badge/pr)](http://issuestats.com/github/krgn/Paket2Nix)
[![Build status](https://ci.appveyor.com/api/projects/status/5yu6k3vjsdnmhlcj?svg=true)](https://ci.appveyor.com/project/krgn/paket2nix)

# Paket2Nix

Create [`nix`](https://nixos.org/nix/) expressions for projects managed with
[`Paket`](https://github.com/fsprojects/Paket) for ad-hoc packaging of `.NET` applications
and libraries.

`Paket2Nix` resolves all dependencies via `Pakets` lock file, collects relevant
metadata and finally writes out `nix` expressions to disk. It also attempts to
calculate `sha256` checksums for all dependencies and the main package.

# Usage:

TLDR;

```shell
$ cd /path/to/project/root
$ paket2nix
$ cd ./nix
$ nix-build -K default.nix -A $PROJECT_NAME
$ nix-env -i -f default.nix -A $PROJECT_NAME
```

# Feedback

If you'd like improvements to this tool, please don't hesitate to open an issue
or even PR!
