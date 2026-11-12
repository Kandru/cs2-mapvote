"""Shared layout pool sizes and versioned asset names (keep in sync with VoteIds.cs / Version.cs)."""

# Must match MapVote.PanoramaVersion in src/MapVote/Version.cs
PANORAMA_VERSION = "v2"
# Legacy layout v1 stays built and validated under its own name until it is deleted.
V1_VERSION = "v1"

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

VOTE_XML_NAME = f"vote_{V1_VERSION}.xml"
HUD_VCSS_NAME = f"hud_{V1_VERSION}.vcss"
HUD_CSS_NAME = f"hud_{V1_VERSION}.css"
# Map image classes (mv-map-*) stay in maps_v1 for every layout version, so image packs keep working.
MAPS_VCSS_NAME = "maps_v1.vcss"
MAPS_CSS_NAME = "maps_v1.css"

# ---- Layout v2 (T6): the layout the plugin spawns (PANORAMA_VERSION). ----
V2_VERSION = "v2"
VOTE_V2_XML_NAME = f"vote_{V2_VERSION}.xml"
HUD_V2_VCSS_NAME = f"hud_{V2_VERSION}.vcss"
V2_GRID_SLOTS = GRID_SLOT_COUNT  # = VoteIds.SlotCount
V2_RANK_SLOTS = 6
V2_FILTER_TYPE_SLOTS = FILTER_TYPE_SLOTS  # = VoteIds.FilterTypeSlots
V2_FILTER_SORT_SLOTS = FILTER_SORT_SLOTS  # = VoteIds.FilterSortSlots
V2_SPAN_MIN = 3  # countdown classes span-3 … span-90 (seconds)
V2_SPAN_MAX = 90
V2_SHARE_STEP = 10  # ranking share ladder p0, p10 … p100
