# Build

Dockerized .NET via `mcr.microsoft.com/dotnet/sdk:10.0` (no project Dockerfile). Host user UID/GID; NuGet cache `~/.nuget`. Optional absolute `<OutputPath>` in the csproj is bind-mounted for direct plugin deploy.

`make debug`, `make release`, and `make test` run `make panorama` first.

| Target | Effect |
| --- | --- |
| `make debug` | `dotnet build` Debug |
| `make release` | `dotnet publish` Release |
| `make test` | unit tests (Release) |
| `make panorama` | `tools/validate_panorama.py` (+ preview sync) |
| `make restore` | NuGet restore |
| `make clean` | clean plugin + tests |
| `make images` | extract map tiles (`WORKSHOP_DIR` and/or `GAME_DIR`) |
| `make images-check` | rewrite tiles that are not 350×197 PNG |
| `make images-css` | rebuild stock/palette VCSS from PNGs on disk |

See [images.md](images.md) for extractor setup (`SOURCE2VIEWER_CLI`, Pillow).
