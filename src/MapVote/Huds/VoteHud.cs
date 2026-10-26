using CounterStrikeSharp.API.Core;
using MapVote.Config;
using MapVote.Maps;
using MapVote.Players;
using MapVote.Utils;
using MapVote.Voting;
using PlayerUtil = MapVote.Utils.Players;

namespace MapVote.Huds
{
    public static class VoteHud
    {
        public const string Panel = VoteIds.Root;
        public const int SlotCount = VoteIds.SlotCount;
        public const int CatalogCols = VoteIds.CatalogCols;
        public const int CatalogRowCount = VoteIds.CatalogRowCount;
        public const int FilterTypeSlots = VoteIds.FilterTypeSlots;
        public const int FilterSortSlots = VoteIds.FilterSortSlots;

        private const int MaxSlots = 65;
        private const int PaintWriteBudget = 128;

        private static VoteSession? _session;
        private static FiltersConfig _filters = new();
        private static PlayerArchive? _archive;
        private static MapPlayCounts? _plays;
        private static Func<CCSPlayerController, string, string>? _text;
        private static Dictionary<string, MapOrigin> _origins = new(StringComparer.Ordinal);
        private static bool _revealing;
        private static string? _winner;

        private static readonly int[] LastVotedWidth = new int[MaxSlots];
        private static readonly int[] LastCatalogRows = new int[MaxSlots];
        private static readonly int[,] LastCatalogCols = new int[MaxSlots, CatalogRowCount];
        private static readonly bool[] Minimized = new bool[MaxSlots];
        private static readonly bool[] Dirty = new bool[MaxSlots];
        private static readonly int[] TypeSelection = new int[MaxSlots];
        private static readonly int[] SortSelection = new int[MaxSlots];
        private static readonly string?[,] VotedMaps = new string?[MaxSlots, SlotCount];
        private static readonly string?[,] GridMaps = new string?[MaxSlots, SlotCount];

        public static void Bind(
            VoteSession session,
            FiltersConfig filters,
            PlayerArchive archive,
            MapPlayCounts plays,
            Func<CCSPlayerController, string, string> text,
            IReadOnlyDictionary<string, MapOrigin> origins)
        {
            _session = session;
            _filters = filters;
            _archive = archive;
            _plays = plays;
            _text = text;
            _origins = new Dictionary<string, MapOrigin>(origins, StringComparer.Ordinal);
            ResetAllSlots();
            _revealing = false;
            _winner = null;
        }

        public static void Clear()
        {
            _session = null;
            _archive = null;
            _plays = null;
            _text = null;
            _origins = new Dictionary<string, MapOrigin>(StringComparer.Ordinal);
            _revealing = false;
            _winner = null;
            ResetAllSlots();
        }

        public static void WriteDefaults(CCSPlayerController player)
        {
            if (player.Slot is >= 0 and < MaxSlots)
            {
                ResetSlotState(player.Slot);
            }

            CustomHud.SetText(player, Panel, "mv_title", T(player, "hud.title"));
            CustomHud.SetText(player, Panel, "mv_hint", T(player, "hud.hint"));
            CustomHud.SetText(player, Panel, "mv_chip", T(player, "hud.chip"));
            CustomHud.SetText(player, Panel, "mv_empty", T(player, "hud.empty"));
            CustomHud.SetHasClass(player, Panel, "is-minimized", false);
            CustomHud.SetHasClass(player, VoteIds.VotedSection, "has-votes", false);
            CustomHud.SetHasClass(player, VoteIds.VotedHint, "is-off", false);
            CustomHud.SetHasClass(player, VoteIds.Empty, "is-off", true);
            CustomHud.SetHasClass(player, VoteIds.Filters, "is-off", true);
            SetVotedWidth(player, 0);
            SetCatalogRows(player, 0);
            for (int r = 0; r < CatalogRowCount; r++)
            {
                SetCatalogRow(player, r, 0);
            }

            for (int i = 0; i < FilterTypeSlots; i++)
            {
                WriteChip(player, VoteIds.TypeChipIds[i], VoteIds.TypeVars[i], string.Empty, off: true);
            }

            WriteChip(player, VoteIds.FavoritesSort, VoteIds.FavoritesSortVar, string.Empty, off: true);

            for (int i = 0; i < FilterSortSlots; i++)
            {
                WriteChip(player, VoteIds.SortChipIds[i], VoteIds.SortVars[i], string.Empty, off: true);
            }

            for (int i = 0; i < SlotCount; i++)
            {
                PaintEmpty(player, Pool.Voted, i);
                PaintEmpty(player, Pool.Grid, i);
            }
        }

        public static void ReleaseSlot(int slot)
        {
            if (slot is >= 0 and < MaxSlots)
            {
                ResetSlotState(slot);
            }
        }

        public static void OpenFor(CCSPlayerController player, bool captureMouse = true)
        {
            if (_session is null || !PlayerUtil.IsHumanViewer(player))
            {
                return;
            }

            WriteDefaults(player);
            Paint(player);
            HudMenu.Open(player, new HudMenuOpenOptions { OnButton = OnButton, CaptureMouse = captureMouse });
            PluginLog.Debug($"hud open slot={player.Slot}");
        }

        public static void PaintRevealFor(CCSPlayerController player, string winner)
        {
            if (_session is null || !PlayerUtil.IsHumanViewer(player))
            {
                return;
            }

            _revealing = true;
            _winner = winner;
            ApplyRevealPaint(player);
        }

        public static void PaintAll()
        {
            if (_session is null)
            {
                return;
            }

            ForEachOpen(player =>
            {
                if (player.Slot is >= 0 and < MaxSlots)
                {
                    Dirty[player.Slot] = true;
                }

                Paint(player);
            });
        }

        public static void Paint(CCSPlayerController player)
        {
            if (_session is null || !PlayerUtil.IsHumanViewer(player) || player.Slot is < 0 or >= MaxSlots)
            {
                return;
            }

            CustomHud.BeginWriteBudget();
            try
            {
                PaintCore(player);
                Dirty[player.Slot] = CustomHud.Writes >= PaintWriteBudget;
            }
            finally
            {
                CustomHud.EndWriteBudget();
            }
        }

        public static void OnTick()
        {
            if (_session is null)
            {
                return;
            }

            bool anyDirty = false;
            for (int i = 0; i < MaxSlots; i++)
            {
                if (Dirty[i])
                {
                    anyDirty = true;
                    break;
                }
            }

            if (!anyDirty)
            {
                return;
            }

            ForEachOpen(player =>
            {
                if (player.Slot is >= 0 and < MaxSlots && Dirty[player.Slot])
                {
                    Paint(player);
                }
            });
        }

        public static void BeginReveal(string winner)
        {
            _revealing = true;
            _winner = winner;
            ForEachOpen(ApplyRevealPaint);
        }

        public static void CloseAll()
        {
            foreach (CCSPlayerController player in PlayerUtil.GetHumans().ToList())
            {
                HudMenu.Close(player);
            }

            Clear();
        }

        private static void ApplyRevealPaint(CCSPlayerController player)
        {
            HudMenu.DropCapture(player);
            if (player.Slot is >= 0 and < MaxSlots)
            {
                Dirty[player.Slot] = true;
            }

            Paint(player);
        }

        public static void OnButton(CCSPlayerController player, string buttonId)
        {
            if (_session is null || !_session.IsOpen || player.Slot is < 0 or >= MaxSlots)
            {
                return;
            }

            if (buttonId == VoteIds.Minimize)
            {
                ToggleMinimized(player);
                return;
            }

            if (buttonId == VoteIds.FavoritesSort)
            {
                ApplySortClick(player, OnlySortIndex());
                return;
            }

            if (TryParseFilterChip(buttonId, out bool isType, out int chipIndex))
            {
                ApplyFilterClick(player, isType, chipIndex);
                return;
            }

            if (_revealing || player.AuthorizedSteamID is null)
            {
                return;
            }

            if (Minimized[player.Slot])
            {
                return;
            }

            if (TryParseFavButton(buttonId, out Pool favPool, out int favIndex))
            {
                string? map = favPool == Pool.Voted
                    ? VotedMaps[player.Slot, favIndex]
                    : GridMaps[player.Slot, favIndex];
                if (map is not null)
                {
                    ToggleFavorite(player, map);
                }

                return;
            }

            if (!TryParseTileButton(buttonId, out Pool pool, out int index))
            {
                return;
            }

            string? votedMap = pool == Pool.Voted
                ? VotedMaps[player.Slot, index]
                : GridMaps[player.Slot, index];
            if (votedMap is null)
            {
                return;
            }

            if (_session.Cast(player.AuthorizedSteamID.SteamId64, votedMap))
            {
                PluginLog.Debug($"cast {player.AuthorizedSteamID.SteamId64} → {votedMap}");
                PaintAll();
            }
        }

        private static void PaintCore(CCSPlayerController player)
        {
            string? mine = player.AuthorizedSteamID is { } steam
                ? _session!.VoteOf(steam.SteamId64)
                : null;
            HashSet<string> favorites = FavoritesOf(player);

            CustomHud.SetText(
                player,
                Panel,
                "mv_title",
                _revealing ? T(player, "hud.winner") : T(player, "hud.title"));
            CustomHud.SetText(player, Panel, "mv_hint", T(player, "hud.hint"));
            CustomHud.SetText(player, Panel, "mv_chip", T(player, "hud.chip"));
            CustomHud.SetText(player, Panel, "mv_empty", T(player, "hud.empty"));

            IReadOnlyList<string> voted = _session!.VotedOrder();
            CustomHud.SetHasClass(player, VoteIds.VotedSection, "has-votes", voted.Count > 0);
            CustomHud.SetHasClass(player, VoteIds.VotedHint, "is-off", voted.Count > 0 || _revealing);
            SetVotedWidth(player, voted.Count);
            PaintPool(player, Pool.Voted, voted, mine, favorites);

            PaintFilterBar(player, favorites);

            IReadOnlyList<string> grid = FilteredGrid(player, favorites);
            bool empty = grid.Count == 0;
            CustomHud.SetHasClass(player, VoteIds.Empty, "is-off", !empty);
            int rowCount = empty ? 0 : (grid.Count + CatalogCols - 1) / CatalogCols;
            SetCatalogRows(player, rowCount);
            for (int r = 0; r < CatalogRowCount; r++)
            {
                int visible = r < rowCount
                    ? Math.Min(CatalogCols, grid.Count - r * CatalogCols)
                    : 0;
                SetCatalogRow(player, r, visible);
            }

            PaintPool(player, Pool.Grid, grid, mine, favorites);
        }

        private static void PaintFilterBar(CCSPlayerController player, HashSet<string> favorites)
        {
            bool show = _filters.Enabled
                && (_filters.Types.Count > 0 || _filters.Sorts.Count > 0);
            CustomHud.SetHasClass(player, VoteIds.Filters, "is-off", !show);
            if (!show)
            {
                return;
            }

            int typeCount = Math.Min(_filters.Types.Count, FilterTypeSlots - 1);
            bool hasTypes = typeCount > 0;
            int activeType = Math.Clamp(TypeSelection[player.Slot], 0, hasTypes ? typeCount : 0);

            // Slot 0 = All when any type is configured.
            if (hasTypes)
            {
                if (activeType > 0
                    && activeType <= typeCount
                    && !TypeHasMaps(_filters.Types[activeType - 1]))
                {
                    activeType = 0;
                    TypeSelection[player.Slot] = 0;
                }

                WriteChip(
                    player,
                    VoteIds.TypeChipIds[0],
                    VoteIds.TypeVars[0],
                    T(player, "filter.all"),
                    off: false,
                    active: activeType == 0);
                for (int i = 0; i < typeCount; i++)
                {
                    int chip = i + 1;
                    FilterTypeConfig type = _filters.Types[i];
                    WriteChip(
                        player,
                        VoteIds.TypeChipIds[chip],
                        VoteIds.TypeVars[chip],
                        T(player, $"filter.{type.Id}"),
                        off: false,
                        active: activeType == chip,
                        disabled: !TypeHasMaps(type));
                }

                for (int i = typeCount + 1; i < FilterTypeSlots; i++)
                {
                    WriteChip(player, VoteIds.TypeChipIds[i], off: true);
                }
            }
            else
            {
                for (int i = 0; i < FilterTypeSlots; i++)
                {
                    WriteChip(player, VoteIds.TypeChipIds[i], off: true);
                }
            }

            int activeSort = SortSelection[player.Slot];
            if (activeSort >= 0
                && activeSort < _filters.Sorts.Count
                && !MapSort.ChipEnabled(_filters.Sorts[activeSort], favorites))
            {
                activeSort = -1;
                SortSelection[player.Slot] = -1;
            }

            int onlySortIndex = OnlySortIndex();
            if (onlySortIndex >= 0)
            {
                SortConfig onlySort = _filters.Sorts[onlySortIndex];
                WriteChip(
                    player,
                    VoteIds.FavoritesSort,
                    VoteIds.FavoritesSortVar,
                    T(player, $"sort.{onlySort.Id}"),
                    off: false,
                    active: activeSort == onlySortIndex,
                    disabled: !MapSort.ChipEnabled(onlySort, favorites));
            }
            else
            {
                WriteChip(player, VoteIds.FavoritesSort, off: true);
            }

            // mv-fs-* paints reorder sorts; only-filter sorts sit next to All.
            int sortChip = 0;
            for (int i = 0; i < _filters.Sorts.Count && sortChip < FilterSortSlots; i++)
            {
                SortConfig sort = _filters.Sorts[i];
                if (sort.Only)
                {
                    continue;
                }

                WriteChip(
                    player,
                    VoteIds.SortChipIds[sortChip],
                    VoteIds.SortVars[sortChip],
                    T(player, $"sort.{sort.Id}"),
                    off: false,
                    active: activeSort == i,
                    disabled: !MapSort.ChipEnabled(sort, favorites));
                sortChip++;
            }

            for (; sortChip < FilterSortSlots; sortChip++)
            {
                WriteChip(player, VoteIds.SortChipIds[sortChip], off: true);
            }
        }

        private static int OnlySortIndex() =>
            _filters.Sorts.FindIndex(static s => s.Only);

        /// <summary>Map an <c>mv-fs-*</c> chip onto the matching non-only entry in sorts.</summary>
        private static int SortIndexForChip(int chipIndex)
        {
            int chip = 0;
            for (int i = 0; i < _filters.Sorts.Count; i++)
            {
                if (_filters.Sorts[i].Only)
                {
                    continue;
                }

                if (chip == chipIndex)
                {
                    return i;
                }

                chip++;
            }

            return -1;
        }

        private static void WriteChip(
            CCSPlayerController player,
            string panelId,
            bool off,
            bool active = false,
            bool disabled = false)
        {
            CustomHud.SetHasClass(player, panelId, "is-off", off);
            CustomHud.SetHasClass(player, panelId, "is-active", active);
            CustomHud.SetHasClass(player, panelId, "is-disabled", disabled);
        }

        private static void WriteChip(
            CCSPlayerController player,
            string panelId,
            string textVar,
            string text,
            bool off,
            bool active = false,
            bool disabled = false)
        {
            WriteChip(player, panelId, off, active, disabled);
            CustomHud.SetText(player, Panel, textVar, text);
        }

        private static bool TypeHasMaps(FilterTypeConfig type)
        {
            if (_session is null)
            {
                return false;
            }

            IReadOnlyList<string> source = _session.GridOrder();
            for (int i = 0; i < source.Count; i++)
            {
                if (MapTypeFilter.MatchesType(source[i], type, _filters.Types))
                {
                    return true;
                }
            }

            return false;
        }

        private static IReadOnlyList<string> FilteredGrid(CCSPlayerController player, HashSet<string> favorites)
        {
            IReadOnlyList<string> source = _session!.GridOrder();
            List<string> maps = [.. source];

            int typeCount = Math.Min(_filters.Types.Count, FilterTypeSlots - 1);
            int activeType = TypeSelection[player.Slot];
            if (_filters.Enabled && typeCount > 0 && activeType > 0 && activeType <= typeCount)
            {
                FilterTypeConfig type = _filters.Types[activeType - 1];
                maps = maps.Where(m => MapTypeFilter.MatchesType(m, type, _filters.Types)).ToList();
            }

            int activeSort = SortSelection[player.Slot];
            if (_filters.Enabled && activeSort >= 0 && activeSort < _filters.Sorts.Count)
            {
                maps = MapSort.Apply(
                    maps,
                    source,
                    _filters.Sorts[activeSort],
                    m => _plays?.CountOf(m) ?? 0,
                    favorites);
            }

            return maps;
        }

        private static void ApplyFilterClick(CCSPlayerController player, bool isType, int chipIndex)
        {
            if (!_filters.Enabled)
            {
                return;
            }

            if (!isType)
            {
                ApplySortClick(player, SortIndexForChip(chipIndex));
                return;
            }

            int typeCount = Math.Min(_filters.Types.Count, FilterTypeSlots - 1);
            if (chipIndex > typeCount)
            {
                return;
            }

            // Slot 0 = All (always enabled). Other slots follow catalog contents.
            if (chipIndex > 0 && !TypeHasMaps(_filters.Types[chipIndex - 1]))
            {
                return;
            }

            TypeSelection[player.Slot] = chipIndex;
            Dirty[player.Slot] = true;
            Paint(player);
        }

        private static void ApplySortClick(CCSPlayerController player, int sortIndex)
        {
            if (!_filters.Enabled
                || sortIndex < 0
                || sortIndex >= _filters.Sorts.Count
                || !MapSort.ChipEnabled(_filters.Sorts[sortIndex], FavoritesOf(player)))
            {
                return;
            }

            SortSelection[player.Slot] = SortSelection[player.Slot] == sortIndex ? -1 : sortIndex;
            Dirty[player.Slot] = true;
            Paint(player);
        }

        private static void ToggleFavorite(CCSPlayerController player, string map)
        {
            if (_archive is null || player.AuthorizedSteamID is null)
            {
                return;
            }

            string steamId = player.AuthorizedSteamID.SteamId64.ToString();
            PlayerState state = _archive.GetOrCreate(steamId);
            if (state.Username.Length == 0 || state.Username == steamId)
            {
                state.Username = player.PlayerName ?? steamId;
            }

            int idx = state.Favorites.FindIndex(f => string.Equals(f, map, StringComparison.Ordinal));
            if (idx >= 0)
            {
                state.Favorites.RemoveAt(idx);
            }
            else
            {
                state.Favorites.Add(map);
            }

            state.Dirty = true;
            Dirty[player.Slot] = true;
            Paint(player);
        }

        private static HashSet<string> FavoritesOf(CCSPlayerController player)
        {
            if (_archive is null || player.AuthorizedSteamID is null)
            {
                return [];
            }

            PlayerState? state = _archive.TryGet(player.AuthorizedSteamID.SteamId64.ToString());
            if (state is null || state.Favorites.Count == 0)
            {
                return [];
            }

            return new HashSet<string>(state.Favorites, StringComparer.Ordinal);
        }

        private static void ToggleMinimized(CCSPlayerController player)
        {
            if (player.Slot is < 0 or >= MaxSlots)
            {
                return;
            }

            bool next = !Minimized[player.Slot];
            Minimized[player.Slot] = next;
            CustomHud.SetHasClass(player, Panel, "is-minimized", next);
        }

        private static void SetVotedWidth(CCSPlayerController player, int count)
        {
            if (player.Slot is < 0 or >= MaxSlots)
            {
                return;
            }

            SetLadder(player, VoteIds.VotedStrip, VoteIds.VwClasses, ref LastVotedWidth[player.Slot], count);
        }

        private static void SetCatalogRows(CCSPlayerController player, int rowCount)
        {
            if (player.Slot is < 0 or >= MaxSlots)
            {
                return;
            }

            SetLadder(player, VoteIds.Catalog, VoteIds.RowsClasses, ref LastCatalogRows[player.Slot], rowCount);
        }

        /// <summary>Show or hide a catalog row and set its cols-n width from visible slot count.</summary>
        private static void SetCatalogRow(CCSPlayerController player, int row, int visible)
        {
            if (player.Slot is < 0 or >= MaxSlots || row is < 0 or >= CatalogRowCount)
            {
                return;
            }

            CustomHud.SetHasClass(player, VoteIds.CrowIds[row], "is-off", visible <= 0);
            // ColsClasses ends at CatalogColsWidthMax (100%); SetLadder clamps.
            SetLadder(
                player,
                VoteIds.CrowIds[row],
                VoteIds.ColsClasses,
                ref LastCatalogCols[player.Slot, row],
                visible);
        }

        /// <summary>Exclusive ladder class swap (vw-*, rows-*, cols-*). remembered=-1 means unset.</summary>
        private static void SetLadder(
            CCSPlayerController player,
            string panelId,
            string[] classes,
            ref int remembered,
            int value)
        {
            int n = Math.Clamp(value, 0, classes.Length - 1);
            if (remembered == n)
            {
                return;
            }

            if (remembered >= 0)
            {
                CustomHud.SetHasClass(player, panelId, classes[remembered], false);
            }

            CustomHud.SetHasClass(player, panelId, classes[n], true);
            remembered = n;
        }

        private static void PaintPool(
            CCSPlayerController player,
            Pool pool,
            IReadOnlyList<string> order,
            string? mine,
            HashSet<string> favorites)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (CustomHud.Writes >= PaintWriteBudget)
                {
                    InvalidateRememberFrom(player, pool, i);
                    return;
                }

                if (i >= order.Count)
                {
                    PaintEmpty(player, pool, i);
                    continue;
                }

                PaintTile(player, pool, i, order[i], _session!.CountOf(order[i]), mine, favorites);
            }
        }

        private static void InvalidateRememberFrom(CCSPlayerController player, Pool pool, int startIndex)
        {
            for (int i = startIndex; i < SlotCount; i++)
            {
                Remember(player, pool, i, null);
            }
        }

        private static void PaintTile(
            CCSPlayerController player,
            Pool pool,
            int index,
            string map,
            int votes,
            string? mine,
            HashSet<string> favorites)
        {
            string slotId = SlotId(pool, index);
            string tileId = TileId(pool, index);
            string favId = FavId(pool, index);
            bool isWinner = _winner is not null && _winner == map;
            bool isLost = _revealing && _winner is not null && !isWinner;
            bool isFav = favorites.Contains(map);

            CustomHud.SetHasClass(player, slotId, "is-off", false);
            CustomHud.SetHasClass(player, tileId, "is-self", !_revealing && mine == map);
            CustomHud.SetHasClass(player, tileId, "is-done", isWinner);
            CustomHud.SetHasClass(player, tileId, "is-lost", isLost);
            CustomHud.SetHasClass(player, favId, "is-on", isFav);
            CustomHud.SetText(player, Panel, VoteVar(pool, index), votes.ToString());
            CustomHud.SetText(player, Panel, NameVar(pool, index), map);
            _origins.TryGetValue(map, out MapOrigin origin);
            CustomHud.SetMapImage(player, ShotId(pool, index), map, origin);
            Remember(player, pool, index, map);
        }

        private static void PaintEmpty(CCSPlayerController player, Pool pool, int index)
        {
            string slotId = SlotId(pool, index);
            string tileId = TileId(pool, index);
            string favId = FavId(pool, index);
            CustomHud.SetHasClass(player, slotId, "is-off", true);
            CustomHud.SetHasClass(player, tileId, "is-self", false);
            CustomHud.SetHasClass(player, tileId, "is-done", false);
            CustomHud.SetHasClass(player, tileId, "is-lost", false);
            CustomHud.SetHasClass(player, favId, "is-on", false);
            CustomHud.SetText(player, Panel, VoteVar(pool, index), string.Empty);
            CustomHud.SetText(player, Panel, NameVar(pool, index), string.Empty);
            CustomHud.SetMapImage(player, ShotId(pool, index), null);
            Remember(player, pool, index, null);
        }

        private static void Remember(CCSPlayerController player, Pool pool, int index, string? map)
        {
            if (player.Slot is < 0 or >= MaxSlots)
            {
                return;
            }

            if (pool == Pool.Voted)
            {
                VotedMaps[player.Slot, index] = map;
            }
            else
            {
                GridMaps[player.Slot, index] = map;
            }
        }

        private static void ResetSlotState(int slot)
        {
            LastVotedWidth[slot] = -1;
            LastCatalogRows[slot] = -1;
            Minimized[slot] = false;
            Dirty[slot] = false;
            TypeSelection[slot] = 0;
            SortSelection[slot] = -1;
            for (int r = 0; r < CatalogRowCount; r++)
            {
                LastCatalogCols[slot, r] = -1;
            }

            for (int i = 0; i < SlotCount; i++)
            {
                VotedMaps[slot, i] = null;
                GridMaps[slot, i] = null;
            }
        }

        private static void ResetAllSlots()
        {
            Array.Fill(LastVotedWidth, -1);
            Array.Fill(LastCatalogRows, -1);
            Array.Fill(Minimized, false);
            Array.Fill(Dirty, false);
            Array.Fill(TypeSelection, 0);
            Array.Fill(SortSelection, -1);
            for (int s = 0; s < MaxSlots; s++)
            {
                for (int r = 0; r < CatalogRowCount; r++)
                {
                    LastCatalogCols[s, r] = -1;
                }

                for (int i = 0; i < SlotCount; i++)
                {
                    VotedMaps[s, i] = null;
                    GridMaps[s, i] = null;
                }
            }
        }

        private static string SlotId(Pool pool, int index) =>
            pool == Pool.Voted ? VoteIds.VotedSlotIds[index] : VoteIds.GridSlotIds[index];

        private static string TileId(Pool pool, int index) =>
            pool == Pool.Voted ? VoteIds.VotedTileIds[index] : VoteIds.GridTileIds[index];

        private static string ShotId(Pool pool, int index) =>
            pool == Pool.Voted ? VoteIds.VotedShotIds[index] : VoteIds.GridShotIds[index];

        private static string FavId(Pool pool, int index) =>
            pool == Pool.Voted ? VoteIds.VotedFavIds[index] : VoteIds.GridFavIds[index];

        private static string VoteVar(Pool pool, int index) =>
            pool == Pool.Voted ? VoteIds.VotedVoteVars[index] : VoteIds.GridVoteVars[index];

        private static string NameVar(Pool pool, int index) =>
            pool == Pool.Voted ? VoteIds.VotedNameVars[index] : VoteIds.GridNameVars[index];

        private static string T(CCSPlayerController player, string key) =>
            _text is null ? key : _text(player, key);

        private static bool TryParseFilterChip(string buttonId, out bool isType, out int index)
        {
            isType = false;
            index = -1;
            if (buttonId.StartsWith("mv-ft-", StringComparison.Ordinal)
                && int.TryParse(buttonId.AsSpan(6), out index)
                && index is >= 0 and < FilterTypeSlots)
            {
                isType = true;
                return true;
            }

            if (buttonId.StartsWith("mv-fs-", StringComparison.Ordinal)
                && int.TryParse(buttonId.AsSpan(6), out index)
                && index is >= 0 and < FilterSortSlots)
            {
                return true;
            }

            return false;
        }

        private static bool TryParseFavButton(string buttonId, out Pool pool, out int index)
        {
            pool = Pool.Grid;
            index = -1;
            const string votedPrefix = "mv-voted-fav-";
            if (buttonId.StartsWith(votedPrefix, StringComparison.Ordinal)
                && int.TryParse(buttonId.AsSpan(votedPrefix.Length), out index)
                && index is >= 0 and < SlotCount)
            {
                pool = Pool.Voted;
                return true;
            }

            const string gridPrefix = "mv-fav-";
            if (buttonId.StartsWith(gridPrefix, StringComparison.Ordinal)
                && int.TryParse(buttonId.AsSpan(gridPrefix.Length), out index)
                && index is >= 0 and < SlotCount)
            {
                return true;
            }

            return false;
        }

        private static bool TryParseTileButton(string buttonId, out Pool pool, out int index)
        {
            pool = Pool.Grid;
            index = -1;

            const string votedPrefix = "mv-voted-";
            if (buttonId.StartsWith(votedPrefix, StringComparison.Ordinal))
            {
                ReadOnlySpan<char> rest = buttonId.AsSpan(votedPrefix.Length);
                if (rest.IsEmpty || !char.IsAsciiDigit(rest[0]))
                {
                    return false;
                }

                pool = Pool.Voted;
                return int.TryParse(rest, out index) && index is >= 0 and < SlotCount;
            }

            if (!buttonId.StartsWith("mv-", StringComparison.Ordinal)
                || buttonId.Length <= 3
                || !char.IsAsciiDigit(buttonId[3]))
            {
                return false;
            }

            return int.TryParse(buttonId.AsSpan(3), out index)
                && index is >= 0 and < SlotCount;
        }

        private static void ForEachOpen(Action<CCSPlayerController> action)
        {
            foreach (CCSPlayerController player in PlayerUtil.GetHumans())
            {
                if (HudMenu.IsOpen(player))
                {
                    action(player);
                }
            }
        }

        private enum Pool
        {
            Voted,
            Grid,
        }
    }
}
