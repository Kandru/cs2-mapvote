"""Shared layout pool sizes and versioned asset names (keep in sync with VoteIds.cs / Version.cs)."""

# Must match MapVote.PanoramaVersion in src/MapVote/Version.cs
PANORAMA_VERSION = "v1"

# Panel pool, not catalog size. The layout shows at most 20 shuffled maps;
# the catalog may be larger. Every painted slot is serialized for every
# connected player on one shared entity, so keep this pool small.
SLOT_COUNT = 20
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
