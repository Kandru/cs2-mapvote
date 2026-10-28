PROJECT   := src/MapVote/MapVote.csproj
TEST_PROJECT := src/MapVote.Tests/MapVote.Tests.csproj
SDK_IMAGE := mcr.microsoft.com/dotnet/sdk:10.0
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

.DEFAULT_GOAL := help

.PHONY: help debug release restore clean dirs panorama images images-check images-opt test

help: ## Show this help
	@echo "CS2 MapVote"
	@echo ""
	@echo "Usage: make <target>"
	@echo ""
	@awk 'BEGIN {FS = ":.*?## "} /^[a-zA-Z_-]+:.*?## / {printf "  \033[36m%-12s\033[0m %s\n", $$1, $$2}' $(MAKEFILE_LIST)

dirs:
	@mkdir -p "$(HOME)/.nuget"
ifneq ($(DEBUG_OUTPUT),)
	@mkdir -p "$(DEBUG_OUTPUT)"
endif

debug: dirs panorama ## Build Debug (development game server)
	$(DOCKER_RUN) dotnet build $(PROJECT) -c Debug

release: dirs panorama ## Publish Release (production game server)
	$(DOCKER_RUN) dotnet publish $(PROJECT) -c Release

restore: dirs ## Restore NuGet packages
	$(DOCKER_RUN) dotnet restore $(PROJECT)

clean: dirs ## Remove build artifacts
	$(DOCKER_RUN) dotnet clean $(PROJECT)
	$(DOCKER_RUN) dotnet clean $(TEST_PROJECT)

panorama: ## Validate HUD Panorama XML/CSS and sync browser preview
	python3 tools/validate_panorama.py

images: ## Extract map images (WORKSHOP_DIR and/or GAME_DIR=…/game/csgo)
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

images-check: ## Rewrite map tiles that are not 350×197 PNG (center-crop, no stretch)
	python3 tools/check_map_images.py

# Lossless recompress (deflate + filters). Pixels stay identical; safe metadata is stripped.
images-opt: ## Lossless-compress PNG files in place (needs oxipng)
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

images-css: ## Rebuild stock/palette VCSS (+ preview) from PNGs on disk only
	python3 tools/extract_workshop_images.py --css-only
	python3 tools/sync_preview.py

test: dirs panorama ## Run unit tests
	$(DOCKER_RUN) dotnet test $(TEST_PROJECT) -c Release --nologo
