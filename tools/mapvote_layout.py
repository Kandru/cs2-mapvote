"""Layout names and pool sizes read from the C# source of truth.

`Version.cs` owns the layout version; `VoteIds.cs` owns every panel/dialog pool
size and generated ladder. No pool value is duplicated in Python.
"""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VERSION_CS = ROOT / "src" / "MapVote" / "Version.cs"
VOTE_IDS_CS = ROOT / "src" / "MapVote" / "Huds" / "VoteIds.cs"


def _int_const(path: Path, name: str) -> int:
    text = path.read_text(encoding="utf-8")
    match = re.search(rf"public const int {name} = (\d+);", text)
    if not match:
        raise RuntimeError(f"missing public const int {name} in {path.relative_to(ROOT)}")
    return int(match.group(1))


def _string_const(path: Path, name: str) -> str:
    text = path.read_text(encoding="utf-8")
    match = re.search(rf'public const string {name} = "([^"]+)";', text)
    if not match:
        raise RuntimeError(f"missing public const string {name} in {path.relative_to(ROOT)}")
    return match.group(1)


PANORAMA_VERSION = _string_const(VERSION_CS, "PanoramaVersion")

VOTE_XML_NAME = f"vote_{PANORAMA_VERSION}.xml"
HUD_VCSS_NAME = f"hud_{PANORAMA_VERSION}.vcss"
HUD_CSS_NAME = f"hud_{PANORAMA_VERSION}.css"
# Map image classes keep their own version so image packs survive layout bumps.
MAPS_VCSS_NAME = "maps_v1.vcss"
MAPS_CSS_NAME = "maps_v1.css"

GRID_SLOTS = _int_const(VOTE_IDS_CS, "SlotCount")
RANK_SLOTS = _int_const(VOTE_IDS_CS, "RankSlots")
FILTER_TYPE_SLOTS = _int_const(VOTE_IDS_CS, "FilterTypeSlots")
FILTER_SORT_SLOTS = _int_const(VOTE_IDS_CS, "FilterSortSlots")
SPAN_MIN = _int_const(VOTE_IDS_CS, "SpanMin")
SPAN_MAX = _int_const(VOTE_IDS_CS, "SpanMax")
SHARE_STEP = _int_const(VOTE_IDS_CS, "ShareStep")
