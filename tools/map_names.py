"""Shared map-name rules for the Python image/CSS tools.

Keep in sync with ``MapNameFilter`` in ``src/MapVote/Maps/MapNameFilter.cs``:
a map id is playable only when it is a plain map name (no path), not an engine
helper/preview entry, not a vanity/skybox background and matches the Source map
name pattern.
"""
from __future__ import annotations

import re

# Source-style map names: lowercase start, letters/digits/underscore, max 49 chars.
MAP_NAME_RE = re.compile(r"^[a-z0-9][a-z0-9_]{0,48}$")

DENIED_IDS = frozenset(
    {
        "error",
        "graphics_settings",
        "lobby",
        "lobby_mapveto",
    }
)
DENIED_PREFIXES = ("workshop_preview_",)
DENIED_SUFFIXES = ("_vanity", "_skybox")

DEFAULT_STEM = "default"
STOCK_IMAGE_PREFIX = "map_"
WORKSHOP_IMAGE_PREFIX = "ws_"
IMAGE_PREFIXES = (STOCK_IMAGE_PREFIX, WORKSHOP_IMAGE_PREFIX)
MAP_EXTENSIONS = (".vpk", ".bsp")


def normalize_map_id(raw: str) -> str:
    """Trim, lowercase and strip a single map archive extension."""
    name = raw.strip().lower()
    for suffix in MAP_EXTENSIONS:
        if name.endswith(suffix):
            return name[: -len(suffix)]
    return name


def is_playable_map_id(raw: str) -> bool:
    """Mirror of ``MapNameFilter.IsPlayableMapId`` for a raw slug or line."""
    name = normalize_map_id(raw)
    if not name or "/" in name or "\\" in name:
        return False
    if name in DENIED_IDS:
        return False
    if any(name.startswith(prefix) for prefix in DENIED_PREFIXES):
        return False
    if any(name.endswith(suffix) for suffix in DENIED_SUFFIXES):
        return False
    return MAP_NAME_RE.fullmatch(name) is not None


def ignored_stock_map(name: str) -> bool:
    """Official screenshots that are not map tiles (random option, lobby art)."""
    return name == "random" or name.startswith("lobby_")


def image_stem_map_name(stem: str) -> str | None:
    """Return the map name behind ``map_<name>`` / ``ws_<name>``, if any."""
    if stem == DEFAULT_STEM:
        return DEFAULT_STEM
    for prefix in IMAGE_PREFIXES:
        if stem.startswith(prefix):
            return stem[len(prefix) :]
    return None


def is_playable_image_stem(stem: str) -> bool:
    """True for the default tile or an image stem whose map id is playable."""
    name = image_stem_map_name(stem)
    return name is not None and is_playable_map_id(name)
