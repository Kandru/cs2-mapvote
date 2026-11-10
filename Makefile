PROJECT   := src/MapVote/MapVote.csproj
TEST_PROJECT := src/MapVote.Tests/MapVote.Tests.csproj
SDK_IMAGE := mcr.microsoft.com/dotnet/sdk:10.0
SDK_MAJOR := 10
# Command-line overrides (including empty) skip detection. Used to test the missing-toolchain error.
HAS_DOTNET_10 ?= $(shell dotnet --list-sdks 2>/dev/null | awk '$$1 ~ /^$(SDK_MAJOR)\./ { found=1 } END { exit !found }' && echo yes)
HAS_DOCKER ?= $(shell command -v docker >/dev/null 2>&1 && echo yes)
UID       := $(shell id -u)
GID       := $(shell id -g)
DEBUG_OUTPUT := $(shell sed -n 's/.*<OutputPath>\(.*\)<\/OutputPath>.*/\1/p' $(PROJECT))
WORKSHOP_DIR ?=
GAME_DIR ?=
SOURCE2VIEWER_CLI ?= $(CURDIR)/tools/.cache/Source2Viewer-CLI

# Optional absolute OutputPath in the csproj (for example a local game-server
# plugins folder). When unset, build output stays under the repo bind mount.
ifneq ($(DEBUG_OUTPUT),)
OUTPUT_VOLUME := -v "$(DEBUG_OUTPUT)":"$(DEBUG_OUTPUT)"
endif

# Run the SDK as the host user so files written to bind mounts are not root-owned.
DOCKER_RUN = docker run --rm \
	--user $(UID):$(GID) \
	-e HOME=/tmp \
	-e DOTNET_CLI_HOME=/tmp \
	-e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
	-v "$(CURDIR)":/src \
	-v "$(HOME)/.nuget":/tmp/.nuget \
	$(OUTPUT_VOLUME) \
	-w /src \
	$(SDK_IMAGE)

ifeq ($(HAS_DOTNET_10),yes)
RUNNER := local
RUNNER_LABEL := local .NET SDK $(SDK_MAJOR)
DOTNET_CMD = dotnet
else ifeq ($(HAS_DOCKER),yes)
RUNNER := docker
RUNNER_LABEL := Docker ($(SDK_IMAGE))
DOTNET_CMD = $(DOCKER_RUN) dotnet
else
RUNNER := missing
RUNNER_LABEL := missing — install the .NET $(SDK_MAJOR) SDK or Docker
DOTNET_CMD = $(error .NET SDK $(SDK_MAJOR) was not found and docker is not installed. Install the .NET $(SDK_MAJOR) SDK or Docker, then re-run make)
endif

.DEFAULT_GOAL := help

.PHONY: images-missing images-missing-ws images-missing-stock

ifeq ($(RUNNER),missing)
require-sdk:
	$(error .NET SDK $(SDK_MAJOR) was not found and docker is not installed. Install the .NET $(SDK_MAJOR) SDK or Docker, then re-run make)
else
require-sdk: ;
endif

help: ## Help: Show targets and the selected .NET runner
	@echo "CS2 MapVote — end-match map vote plugin"
	@echo ""
	@echo "Toolchain: .NET SDK $(SDK_MAJOR) when installed, otherwise Docker ($(SDK_IMAGE))."
	@echo "Selected: $(RUNNER_LABEL)"
	@echo ""
	@echo "Usage: make <target>"
	@echo ""
	@awk 'BEGIN {FS = ":.*?## "} /^[a-zA-Z0-9_.-]+:.*?## / { \
		split($$2, parts, ": "); \
		group = parts[1]; \
		sub("^[^:]+: ", "", $$2); \
		if (group != last) { if (last != "") printf "\n"; printf "%s\n", group; last = group } \
		printf "  %-14s %s\n", $$1, $$2 \
	}' $(MAKEFILE_LIST)

dirs:
	@mkdir -p "$(HOME)/.nuget"
ifneq ($(DEBUG_OUTPUT),)
	@mkdir -p "$(DEBUG_OUTPUT)"
endif

debug: require-sdk dirs panorama ## Build: Debug build for a development server
	$(DOTNET_CMD) build $(PROJECT) -c Debug

release: require-sdk dirs panorama ## Build: Publish Release for a production server
	$(DOTNET_CMD) publish $(PROJECT) -c Release

restore: require-sdk dirs ## Build: Restore NuGet packages
	$(DOTNET_CMD) restore $(PROJECT)

clean: require-sdk dirs ## Build: Remove plugin and test build artifacts
	$(DOTNET_CMD) clean $(PROJECT)
	$(DOTNET_CMD) clean $(TEST_PROJECT)

test: require-sdk dirs panorama ## Test: Run unit tests
	$(DOTNET_CMD) test $(TEST_PROJECT) -c Release --nologo

panorama: ## Panorama: Validate HUD XML/CSS and sync the browser preview
	python3 tools/validate_panorama.py

images: ## Images: Extract map images (WORKSHOP_DIR and/or GAME_DIR=…/game/csgo)
	@if [ -z "$(WORKSHOP_DIR)" ] && [ -z "$(GAME_DIR)" ]; then \
		echo "Usage:"; \
		echo "  make images WORKSHOP_DIR=/path/to/workshop/or/maps"; \
		echo "  make images GAME_DIR=/path/to/game/csgo"; \
		echo "  make images WORKSHOP_DIR=… GAME_DIR=…/game/csgo"; \
		exit 1; \
	fi
	SOURCE2VIEWER_CLI="$(SOURCE2VIEWER_CLI)" python3 tools/extract_workshop_images.py \
		$(if $(WORKSHOP_DIR),"$(WORKSHOP_DIR)",) \
		$(if $(GAME_DIR),--game-dir "$(GAME_DIR)",)

images-missing: ## Images: Report missing maps and stub them (auto-detect workshop vs stock)
	@python3 tools/find_missing_images.py

images-missing-ws: ## Images: Report missing maps and always add as workshop (ws_*)
	@python3 tools/find_missing_images.py --type workshop

images-missing-stock: ## Images: Report missing maps and always add as stock (map_*)
	@python3 tools/find_missing_images.py --type stock

images-check: ## Images: Rewrite map tiles that are not 350×197 PNG (center-crop, no stretch)
	python3 tools/check_map_images.py

# Lossless recompress (deflate + filters). Pixels stay identical; safe metadata is stripped.
images-opt: ## Images: Lossless-compress PNG files in place (needs oxipng)
	@command -v oxipng >/dev/null || { \
		echo "oxipng is required (lossless). Install it, then re-run make images-opt."; \
		echo "  Arch: sudo pacman -S oxipng"; \
		exit 1; \
	}
	@find . \
		-path './.git' -prune -o \
		-path './bin' -prune -o \
		-path './obj' -prune -o \
		-path './tools/.cache' -prune -o \
		-type f -name '*.png' -print0 \
	| xargs -0 -r oxipng -o 4 --strip safe --alpha

images-css: ## Images: Append maps stock/palette VCSS and rebuild the preview from PNGs on disk
	python3 tools/extract_workshop_images.py --css-only
	python3 tools/sync_preview.py

images-prune: ## Images: Delete map CSS entries without a stock-list/on-disk image (after uploads)
	python3 tools/extract_workshop_images.py --prune
	python3 tools/sync_preview.py
