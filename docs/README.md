# Developer docs

Compressed reference for people changing MapVote and for AI agents. Server owners: start at the [root README](../README.md).

| Doc | Contents |
| --- | --- |
| [config.md](config.md) | `MapVote.json` keys |
| [behavior.md](behavior.md) | Vote flow, RCON catalog, RTV, HUD wiring, entry points |
| [images.md](images.md) | Workshop map tiles, extractor, class naming, custom addons |

Panorama binding rules (layouts, classes, transmit, overflows): [stuff/PANORAMA_RULESET.md](../stuff/PANORAMA_RULESET.md). Cursor rule: `.cursor/rules/panorama.mdc`.

## Build

`make debug`, `make release`, `make restore`, `make clean`, and `make test` use a local .NET 10 SDK when `dotnet --list-sdks` shows one. Otherwise they run the same commands in `mcr.microsoft.com/dotnet/sdk:10.0` (host UID/GID, NuGet cache `~/.nuget`, optional absolute `<OutputPath>` bind-mounted). If neither the SDK nor Docker is installed, those targets stop with an error. `make help` still runs and says the toolchain is missing.

`make debug`, `make release`, and `make test` run `make panorama` first.

| Target | Effect |
| --- | --- |
| `make help` | Targets grouped by Build, Test, Panorama, Images, and which runner is selected |
| `make debug` | `dotnet build` Debug |
| `make release` | `dotnet publish` Release |
| `make test` | unit tests (Release) |
| `make panorama` | `tools/validate_panorama.py` (+ preview sync) |
| `make restore` | NuGet restore |
| `make clean` | clean plugin + tests |
| `make images` | extract map tiles (`WORKSHOP_DIR` and/or `GAME_DIR`) |
| `make images-check` | rewrite tiles that are not 350×197 PNG |
| `make images-css` | append missing `maps_v1.vcss` stock/palette entries from PNGs on disk |
| `make images-prune` | remove `maps_v1.vcss` entries without a stock-list/on-disk image |
| `make images-opt` | lossless-compress PNG files in place (needs oxipng) |

See [images.md](images.md) for extractor setup (`SOURCE2VIEWER_CLI`, Pillow).
