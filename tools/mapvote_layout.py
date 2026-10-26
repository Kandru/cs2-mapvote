"""Shared layout pool sizes and versioned asset names (keep in sync with VoteIds.cs / Version.cs)."""

# Must match MapVote.PanoramaVersion in src/MapVote/Version.cs
PANORAMA_VERSION = "v1"

SLOT_COUNT = 100
COLS = 10
ROW_COUNT = (SLOT_COUNT + COLS - 1) // COLS  # 10
FILTER_TYPE_SLOTS = 8
FILTER_SORT_SLOTS = 4
VOTED_SLOT_PITCH_PX = 126  # 110 tile + 8 + 8 margin
CATALOG_ROW_HEIGHT_PX = 213
# Cap one catalog tile at ~18% of the row (≈330px @ 16:9). Short rows stay centered.
# Ladder stops at the first n where width hits 100% (cols-6).
COL_WIDTH_PERCENT = 18
COLS_WIDTH_MAX = (100 + COL_WIDTH_PERCENT - 1) // COL_WIDTH_PERCENT

VOTE_XML_NAME = f"vote_{PANORAMA_VERSION}.xml"
HUD_VCSS_NAME = f"hud_{PANORAMA_VERSION}.vcss"
HUD_CSS_NAME = f"hud_{PANORAMA_VERSION}.css"
