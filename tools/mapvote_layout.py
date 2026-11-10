"""Shared layout pool sizes and versioned asset names (keep in sync with VoteIds.cs / Version.cs)."""

# Must match MapVote.PanoramaVersion in src/MapVote/Version.cs
PANORAMA_VERSION = "v1"

# Panel pools, not catalog size. The grid is written globally; the voted
# strip only shows the best-voted maps.
GRID_SLOT_COUNT = 100
VOTED_SLOT_COUNT = 10
FILTER_TYPE_SLOTS = 8
FILTER_SORT_SLOTS = 4

# Catalog and voted strip share one card metric. The catalog wraps by viewport width.
SLOT_WIDTH_PX = 110
SLOT_MARGIN_PX = 8
SLOT_PITCH_PX = SLOT_WIDTH_PX + SLOT_MARGIN_PX * 2  # 126

VOTE_XML_NAME = f"vote_{PANORAMA_VERSION}.xml"
HUD_VCSS_NAME = f"hud_{PANORAMA_VERSION}.vcss"
HUD_CSS_NAME = f"hud_{PANORAMA_VERSION}.css"
MAPS_VCSS_NAME = f"maps_{PANORAMA_VERSION}.vcss"
MAPS_CSS_NAME = f"maps_{PANORAMA_VERSION}.css"
