[![Issue Stats](http://issuestats.com/github/krgn/Paket2Nix/badge/issue)](http://issuestats.com/github/krgn/Paket2Nix)
[![Issue Stats](http://issuestats.com/github/krgn/Paket2Nix/badge/pr)](http://issuestats.com/github/krgn/Paket2Nix)

# Paket2Nix

Create `nix` expressions for projects managed with `Paket`.

# Status:

Currently still mostly a proof of concept, the tool can already be used to take
the edge off of working with `.NET` projects on `nix`/`NixOS`.

It is, however, not quite as pure as I'd like it to be, since dependencies are
not built from source. Its certainly possible to do this, but not practical
at this point and harder to automate.

# Usage:

TLDR;

```shell
$ cd /path/to/project/root
$ Paket2Nix 
$ cd ./nix
$ nix-build -K top.nix -A $PROJECT_NAME
$ nix-env -i -f top.nix -A $PROJECT_NAME
```

Paket2Nix reads `Paket` and project metadata to automate the building of `nix`
expressions as much as possible. It downloads and calculates checksums for all
dependencies and creates expressions accordingly.

It also detects the type of projects (executable/library) and creates wrapper
scripts for executable targets.
