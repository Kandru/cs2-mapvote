using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using MapVote.Config;
using MapVote.Maps;
using MapVote.Players;
using MapVote.Utils;
using MapVote.Voting;

namespace MapVote.Huds
{
    public static class VoteHud
    {
        public const string Panel = VoteIds.Root;
        public const int SlotCount = VoteIds.SlotCount;
        public const int VotedSlotCount = VoteIds.VotedSlotCount;
        public const int FilterTypeSlots = VoteIds.FilterTypeSlots;
        public const int FilterSortSlots = VoteIds.FilterSortSlots;

        private const int MaxSlots = 65;
        /// <summary>Native writes per server tick, shared by every player and caller.</summary>
        private const int PaintWriteBudget = 128;
        /// <summary>Voted strip shows only the best-voted maps (already sorted by count). Shared/global.</summary>
        public const int MaxVotedMaps = VotedSlotCount;

        private enum Mode
        {
            Vote,
            Browse,
        }

        private static Mode _mode = Mode.Vote;
        private static VoteSession? _session;
        private static List<string> _voteOrder = [];
        private static List<string> _browseOrder = [];
        private static FiltersConfig _filters = new();
        private static PlayerArchive? _archive;
        private static MapPlayCounts? _plays;
        private static Func<CCSPlayerController, string, string>? _text;
        private static Dictionary<string, MapOrigin> _origins = new(StringComparer.Ordinal);
        private static Func<string, string> _formatMapName = static name => name;
        private static string? _currentMap;
        private static MapOrigin _currentOrigin = MapOrigin.Local;
        private static bool _revealing;
        private static string? _winner;
        private static int _lastVotedWidth = -1;

        private static readonly bool[] Minimized = new bool[MaxSlots];
        private static readonly bool[] Dirty = new bool[MaxSlots];
        private static readonly HashSet<string>?[] FavoriteCache = new HashSet<string>?[MaxSlots];
        /// <summary>Reveal reached this open player while the tick budget was spent.</summary>
        private static readonly bool[] RevealPending = new bool[MaxSlots];
        /// <summary>Opens waiting for tick budget, first in first out. Queued players are not open.</summary>
        private static readonly List<int> PendingOpens = [];
        private static readonly bool[] Pending = new bool[MaxSlots];
        private static readonly bool[] PendingCapture = new bool[MaxSlots];
        private static int _dirtyCount;
        private static int _typeSelection;
        private static int _sortSelection = -1;
        private static readonly string?[,] VotedMaps = new string?[MaxSlots, VotedSlotCount];
        private static readonly string?[,] GridMaps = new string?[MaxSlots, SlotCount];
        private static readonly HashSet<string> NoFavorites = new(StringComparer.Ordinal);

        public static void Bind(
            VoteSession session,
            FiltersConfig filters,
            PlayerArchive archive,
            MapPlayCounts plays,
            Func<CCSPlayerController, string, string> text,
            IReadOnlyDictionary<string, MapOrigin> origins,
            string? currentMap,
            MapOrigin currentOrigin,
            bool cleanMapNames)
        {
            _mode = Mode.Vote;
            _session = session;
            _voteOrder = Shuffle(session.GridOrder());
            _browseOrder = [];
            _filters = filters;
            _archive = archive;
            _plays = plays;
            _text = text;
            _origins = new Dictionary<string, MapOrigin>(origins, StringComparer.Ordinal);
            _formatMapName = cleanMapNames ? MapNameDisplay.Format : static name => name;
            _currentMap = string.IsNullOrWhiteSpace(currentMap) ? null : currentMap;
            _currentOrigin = currentOrigin;
            ResetAllSlots();
            _revealing = false;
            _winner = null;
        }

        /// <summary>
        /// Binds the same overlay to a plain map list. No vote session, current-map card,
        /// or voted strip is published; only filters and favorites stay interactive.
        /// </summary>
        public static void BindBrowse(
            FiltersConfig filters,
            PlayerArchive archive,
            MapPlayCounts plays,
            Func<CCSPlayerController, string, string> text,
            IReadOnlyDictionary<string, MapOrigin> origins,
            IReadOnlyList<string> maps,
            bool cleanMapNames)
        {
            _mode = Mode.Browse;
            _session = null;
            _voteOrder = [];
            _browseOrder = Shuffle(maps);
            _filters = filters;
            _archive = archive;
            _plays = plays;
            _text = text;
            _origins = new Dictionary<string, MapOrigin>(origins, StringComparer.Ordinal);
            _formatMapName = cleanMapNames ? MapNameDisplay.Format : static name => name;
            _currentMap = null;
            _currentOrigin = MapOrigin.Local;
            ResetAllSlots();
            _revealing = false;
            _winner = null;
        }

        public static bool IsBrowsing => _mode == Mode.Browse;

        private static bool IsBound => _mode == Mode.Browse || _session is not null;

        private static IReadOnlyList<string> SourceOrder =>
            _mode == Mode.Browse ? _browseOrder : _voteOrder;

        private static List<string> Shuffle(IReadOnlyList<string> maps)
        {
            List<string> order = [.. maps];
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            return order;
        }

        private static void Clear()
        {
            _mode = Mode.Vote;
            _session = null;
            _voteOrder = [];
            _browseOrder = [];
            _archive = null;
            _plays = null;
            _text = null;
            _origins = new Dictionary<string, MapOrigin>(StringComparer.Ordinal);
            _formatMapName = static name => name;
            _currentMap = null;
            _currentOrigin = MapOrigin.Local;
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

            bool browsing = _mode == Mode.Browse;
            CustomHud.SetText(player, Panel, VoteIds.TitleVar, T(player, browsing ? "hud.maps_title" : "hud.title"));
            CustomHud.SetText(player, Panel, VoteIds.HintVar, T(player, "hud.hint"));
            CustomHud.SetText(player, Panel, VoteIds.ChipVar, T(player, browsing ? "hud.close" : "hud.chip"));
            CustomHud.SetText(player, Panel, VoteIds.EmptyVar, T(player, "hud.empty"));
            CustomHud.SetText(player, Panel, VoteIds.CurrentTagVar, T(player, "hud.current"));
            CustomHud.SetText(player, Panel, VoteIds.CurrentNameVar, string.Empty);
            CustomHud.SetText(player, Panel, VoteIds.CurrentRatingVar, "0");
            CustomHud.SetHasClass(player, Panel, "is-minimized", false);
            CustomHud.SetHasClass(player, Panel, "is-browse", browsing);
            CustomHud.SetHasClassGlobal(VoteIds.VotedSection, "has-votes", false);
            CustomHud.SetHasClassGlobal(VoteIds.VotedHint, "is-off", browsing);
            CustomHud.SetHasClass(player, VoteIds.Empty, "is-off", true);
            CustomHud.SetHasClass(player, VoteIds.Filters, "is-off", true);
            CustomHud.SetHasClass(player, VoteIds.Current, "is-off", browsing || _currentMap is null);
            CustomHud.SetHasClass(player, VoteIds.CurrentImage, "is-fav", false);
            CustomHud.SetHasClass(player, VoteIds.CurrentRating, "is-positive", false);
            CustomHud.SetHasClass(player, VoteIds.CurrentRating, "is-negative", false);
            CustomHud.SetHasClass(player, VoteIds.CurrentFav, "is-on", false);
            CustomHud.SetHasClass(player, VoteIds.CurrentLike, "is-on", false);
            CustomHud.SetHasClass(player, VoteIds.CurrentDislike, "is-on", false);
            CustomHud.SetMapImage(player, VoteIds.CurrentShot, _currentMap, _currentOrigin);
            SetVotedWidth(0);

            for (int i = 0; i < FilterTypeSlots; i++)
            {
                WriteChip(player, VoteIds.TypeChipIds[i], VoteIds.TypeVars[i], string.Empty, off: true);
            }

            WriteChip(player, VoteIds.FavoritesSort, VoteIds.FavoritesSortVar, string.Empty, off: true);

            for (int i = 0; i < FilterSortSlots; i++)
            {
                WriteChip(player, VoteIds.SortChipIds[i], VoteIds.SortVars[i], string.Empty, off: true);
            }

            // Empty slots are already hidden by the XML defaults. PaintPool
            // hides the tail that the current filter/limit does not fill.
        }

        public static void ReleaseSlot(int slot)
        {
            if (slot is >= 0 and < MaxSlots)
            {
                ResetSlotState(slot);
                if (Pending[slot])
                {
                    Pending[slot] = false;
                    PendingOpens.Remove(slot);
                }
            }
        }

        /// <summary>
        /// Opens now when the tick budget allows and nobody is queued ahead,
        /// otherwise queues the open for <see cref="OnTick"/>.
        /// </summary>
        public static void OpenFor(CCSPlayerController player, bool captureMouse = true)
        {
            if (!IsBound || !Humans.IsHumanViewer(player) || player.Slot is < 0 or >= MaxSlots)
            {
                return;
            }

            int slot = player.Slot;
            PendingCapture[slot] = captureMouse;
            if (!Pending[slot])
            {
                Pending[slot] = true;
                PendingOpens.Add(slot);
            }

            DrainPendingOpens();
            if (Pending[slot])
            {
                PluginLog.Debug($"hud open queued: {Humans.Label(player)} slot={slot} queue={PendingOpens.Count}");
            }
        }

        private static void DrainPendingOpens()
        {
            while (PendingOpens.Count > 0 && CustomHud.TickWrites < PaintWriteBudget)
            {
                int slot = PendingOpens[0];
                PendingOpens.RemoveAt(0);
                Pending[slot] = false;
                if (Utilities.GetPlayerFromSlot(slot) is not { } player || !Humans.IsHumanViewer(player))
                {
                    continue;
                }

                if (_revealing)
                {
                    // Reveal started while queued: open like a joiner during reveal.
                    OpenNow(player, captureMouse: false);
                    ApplyRevealPaint(player);
                }
                else
                {
                    OpenNow(player, PendingCapture[slot]);
                }
            }
        }

        private static void OpenNow(CCSPlayerController player, bool captureMouse)
        {
            WriteDefaults(player);
            Paint(player);
            HudMenu.Open(player, new HudMenuOpenOptions { OnButton = OnButton, CaptureMouse = captureMouse });
            PluginLog.Debug($"hud open: {Humans.Label(player)} slot={player.Slot}");
        }

        public static void PaintRevealFor(CCSPlayerController player, string winner)
        {
            if (_mode != Mode.Vote || _session is null || !Humans.IsHumanViewer(player))
            {
                return;
            }

            _revealing = true;
            _winner = winner;
            if (player.Slot is >= 0 and < MaxSlots && Pending[player.Slot])
            {
                // Not open yet; DrainPendingOpens opens it in the reveal state.
                return;
            }

            ApplyRevealPaint(player);
        }

        public static void PaintAll()
        {
            if (!IsBound)
            {
                return;
            }

            ForEachOpen(player =>
            {
                if (player.Slot is >= 0 and < MaxSlots)
                {
                    SetDirty(player.Slot, true);
                }

                Paint(player);
            });
        }

        public static void Paint(CCSPlayerController player)
        {
            if (!IsBound || !Humans.IsHumanViewer(player) || player.Slot is < 0 or >= MaxSlots || Pending[player.Slot])
            {
                return;
            }

            if (CustomHud.TickWrites >= PaintWriteBudget)
            {
                // Postponed, not partial: keep the click tables of a finished grid.
                SetDirty(player.Slot, true);
                return;
            }

            if (RevealPending[player.Slot])
            {
                ApplyRevealPaint(player);
                return;
            }

            PaintCore(player);
            SetDirty(player.Slot, CustomHud.TickWrites >= PaintWriteBudget);
        }

        public static void OnTick()
        {
            if (!IsBound || (_dirtyCount == 0 && PendingOpens.Count == 0))
            {
                return;
            }

            DrainPendingOpens();
            if (_dirtyCount == 0 || CustomHud.TickWrites >= PaintWriteBudget)
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
            if (_mode != Mode.Vote)
            {
                return;
            }

            _revealing = true;
            _winner = winner;
            ForEachOpen(ApplyRevealPaint);
        }

        public static void CloseAll()
        {
            foreach (CCSPlayerController player in Humans.GetHumans().ToList())
            {
                HudMenu.Close(player);
            }

            Clear();
        }

        /// <summary>Closes every browse overlay and resets the shared HUD state.</summary>
        public static void CloseBrowse()
        {
            if (_mode == Mode.Browse)
            {
                CloseAll();
            }
        }

        private static void ApplyRevealPaint(CCSPlayerController player)
        {
            if (player.Slot is >= 0 and < MaxSlots)
            {
                if (CustomHud.TickWrites >= PaintWriteBudget)
                {
                    // Paint (via OnTick) finishes the reveal once budget is back.
                    RevealPending[player.Slot] = true;
                    SetDirty(player.Slot, true);
                    return;
                }

                RevealPending[player.Slot] = false;
            }

            HudMenu.DropCapture(player);
            if (player.Slot is >= 0 and < MaxSlots)
            {
                // Reveal must show the whole catalog, even if the player was filtered or minimized.
                Minimized[player.Slot] = false;
                _typeSelection = 0;
                _sortSelection = -1;
                FavoriteCache[player.Slot] = null;
                SetDirty(player.Slot, true);
            }

            CustomHud.SetHasClass(player, Panel, "is-minimized", false);
            Paint(player);
        }

        public static void OnButton(CCSPlayerController player, string buttonId)
        {
            bool browsing = _mode == Mode.Browse;
            bool interactive = browsing || _session is { IsOpen: true };
            if (!interactive || player.Slot is < 0 or >= MaxSlots)
            {
                return;
            }

            if (buttonId == VoteIds.Minimize)
            {
                if (browsing)
                {
                    HudMenu.Close(player);
                    return;
                }

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

            if (buttonId == VoteIds.CurrentFav)
            {
                ToggleFavorite(player, _currentMap);
                return;
            }

            if (buttonId == VoteIds.CurrentLike)
            {
                ToggleRating(player, _currentMap, PlayerRating.Like);
                return;
            }

            if (buttonId == VoteIds.CurrentDislike)
            {
                ToggleRating(player, _currentMap, PlayerRating.Dislike);
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

            if (browsing)
            {
                // Browse has no vote path: map tiles are not clickable.
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

            ulong steamId = player.AuthorizedSteamID.SteamId64;
            VoteSession session = _session!;
            string? previous = session.VoteOf(steamId);
            if (session.Cast(steamId, votedMap))
            {
                string label = Humans.Label(player, steamId);
                PluginLog.Debug(previous is null
                    ? $"cast {label} → {votedMap}"
                    : $"cast {label} {previous} → {votedMap}");
                PaintAll();
            }
        }

        private static void PaintCore(CCSPlayerController player)
        {
            HashSet<string> favorites = FavoritesOf(player);
            bool favoritesOnly = FavoritesOnly();

            string titleKey = _mode == Mode.Browse
                ? "hud.maps_title"
                : _revealing ? "hud.winner" : "hud.title";
            CustomHud.SetText(player, Panel, VoteIds.TitleVar, T(player, titleKey));

            if (_mode == Mode.Browse)
            {
                PaintFilterBar(player, favorites);
                IReadOnlyList<string> browseGrid = FilteredGrid();
                CustomHud.SetHasClass(player, VoteIds.Empty, "is-off",
                    VisibleGridCount(browseGrid, favorites, favoritesOnly) > 0);
                PaintPool(player, Pool.Grid, browseGrid, mine: null, favorites, SlotCount, favoritesOnly);
                return;
            }

            string? mine = player.AuthorizedSteamID is { } steam
                ? _session!.VoteOf(steam.SteamId64)
                : null;

            PaintCurrent(player, favorites);

            IReadOnlyList<string> voted = _session!.VotedOrder();
            CustomHud.SetHasClassGlobal(VoteIds.VotedSection, "has-votes", voted.Count > 0);
            CustomHud.SetHasClassGlobal(VoteIds.VotedHint, "is-off", voted.Count > 0 || _revealing);
            SetVotedWidth(Math.Min(voted.Count, MaxVotedMaps));
            PaintPool(player, Pool.Voted, voted, mine, favorites, MaxVotedMaps, favoritesOnly: false);

            PaintFilterBar(player, favorites);

            IReadOnlyList<string> grid = FilteredGrid();
            CustomHud.SetHasClass(player, VoteIds.Empty, "is-off",
                VisibleGridCount(grid, favorites, favoritesOnly) > 0);
            PaintPool(player, Pool.Grid, grid, mine, favorites, SlotCount, favoritesOnly);
        }

        private static void PaintCurrent(CCSPlayerController player, HashSet<string> favorites)
        {
            if (_currentMap is null)
            {
                CustomHud.SetHasClass(player, VoteIds.Current, "is-off", true);
                return;
            }

            string current = _currentMap;
            bool isFav = favorites.Contains(current);
            int score = _archive?.RatingScore(current) ?? 0;
            CustomHud.SetHasClass(player, VoteIds.Current, "is-off", false);
            CustomHud.SetHasClass(player, VoteIds.CurrentImage, "is-fav", isFav);
            CustomHud.SetHasClass(player, VoteIds.CurrentRating, "is-positive", score > 0);
            CustomHud.SetHasClass(player, VoteIds.CurrentRating, "is-negative", score < 0);
            CustomHud.SetText(player, Panel, VoteIds.CurrentNameVar, MapNameDisplay.ForHud(_formatMapName(current)));
            CustomHud.SetText(player, Panel, VoteIds.CurrentRatingVar, FormatRating(score));
            CustomHud.SetMapImage(player, VoteIds.CurrentShot, current, _currentOrigin);

            string? steamId = player.AuthorizedSteamID?.SteamId64.ToString();
            PlayerRating rating = _archive?.RatingOf(steamId, current) ?? PlayerRating.None;
            CustomHud.SetHasClass(player, VoteIds.CurrentFav, "is-on", isFav);
            CustomHud.SetHasClass(player, VoteIds.CurrentLike, "is-on", rating == PlayerRating.Like);
            CustomHud.SetHasClass(player, VoteIds.CurrentDislike, "is-on", rating == PlayerRating.Dislike);
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
            int activeType = Math.Clamp(_typeSelection, 0, hasTypes ? typeCount : 0);

            // Slot 0 = All when any type is configured.
            if (hasTypes)
            {
                if (activeType > 0
                    && activeType <= typeCount
                    && !TypeHasMaps(_filters.Types[activeType - 1]))
                {
                    activeType = 0;
                    _typeSelection = 0;
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

            int activeSort = _sortSelection;
            if (activeSort >= 0
                && activeSort < _filters.Sorts.Count
                && !MapSort.ChipEnabled(_filters.Sorts[activeSort], favorites))
            {
                // The shared selection stays; this viewer just cannot use it.
                activeSort = -1;
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
            if (!IsBound)
            {
                return false;
            }

            IReadOnlyList<string> source = SourceOrder;
            for (int i = 0; i < source.Count; i++)
            {
                if (MapTypeFilter.MatchesType(source[i], type, _filters.Types))
                {
                    return true;
                }
            }

            return false;
        }

        private static IReadOnlyList<string> FilteredGrid()
        {
            IReadOnlyList<string> source = SourceOrder;
            List<string> maps = [.. source];

            int typeCount = Math.Min(_filters.Types.Count, FilterTypeSlots - 1);
            int activeType = _typeSelection;
            if (_filters.Enabled && typeCount > 0 && activeType > 0 && activeType <= typeCount)
            {
                FilterTypeConfig type = _filters.Types[activeType - 1];
                maps = maps.Where(m => MapTypeFilter.MatchesType(m, type, _filters.Types)).ToList();
            }

            int activeSort = _sortSelection;
            if (_filters.Enabled
                && activeSort >= 0
                && activeSort < _filters.Sorts.Count
                && !_filters.Sorts[activeSort].Only)
            {
                maps = MapSort.Apply(
                    maps,
                    source,
                    _filters.Sorts[activeSort],
                    m => _plays?.CountOf(m) ?? 0,
                    m => _archive?.RatingScore(m) ?? 0,
                    NoFavorites,
                    _formatMapName);
            }

            return maps;
        }

        private static bool FavoritesOnly() =>
            _filters.Enabled
            && _sortSelection >= 0
            && _sortSelection < _filters.Sorts.Count
            && _filters.Sorts[_sortSelection].Only;

        private static int VisibleGridCount(IReadOnlyList<string> grid, HashSet<string> favorites, bool favoritesOnly)
        {
            int shown = Math.Min(grid.Count, SlotCount);
            if (!favoritesOnly)
            {
                return shown;
            }

            int count = 0;
            for (int i = 0; i < shown; i++)
            {
                if (favorites.Contains(grid[i]))
                {
                    count++;
                }
            }

            return count;
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

            // Clicking the active type again falls back to All (slot 0).
            _typeSelection = _typeSelection == chipIndex ? 0 : chipIndex;
            PaintAll();
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

            _sortSelection = _sortSelection == sortIndex ? -1 : sortIndex;
            PaintAll();
        }

        private static void ToggleFavorite(CCSPlayerController player, string? map)
        {
            if (_archive is null || player.AuthorizedSteamID is null || map is null)
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
            if (player.Slot is >= 0 and < MaxSlots)
            {
                FavoriteCache[player.Slot] = null;
                SetDirty(player.Slot, true);
            }

            Paint(player);
        }

        private static void ToggleRating(CCSPlayerController player, string? map, PlayerRating rating)
        {
            if (_archive is null || player.AuthorizedSteamID is null || map is null)
            {
                return;
            }

            string steamId = player.AuthorizedSteamID.SteamId64.ToString();
            PlayerState state = _archive.GetOrCreate(steamId);
            if (state.Username.Length == 0 || state.Username == steamId)
            {
                state.Username = player.PlayerName ?? steamId;
            }

            PlayerRating next = PlayerArchive.RatingOf(state, map) == rating
                ? PlayerRating.None
                : rating;
            if (_archive.SetRating(steamId, map, next))
            {
                // Counts are server-wide, so every open HUD needs the new totals.
                PaintAll();
            }
        }

        private static HashSet<string> FavoritesOf(CCSPlayerController player)
        {
            if (player.Slot is >= 0 and < MaxSlots && FavoriteCache[player.Slot] is HashSet<string> cached)
            {
                return cached;
            }

            HashSet<string> built;
            if (_archive is null || player.AuthorizedSteamID is null)
            {
                built = new HashSet<string>(StringComparer.Ordinal);
            }
            else
            {
                PlayerState? state = _archive.TryGet(player.AuthorizedSteamID.SteamId64.ToString());
                built = state is null || state.Favorites.Count == 0
                    ? new HashSet<string>(StringComparer.Ordinal)
                    : new HashSet<string>(state.Favorites, StringComparer.Ordinal);
            }

            if (player.Slot is >= 0 and < MaxSlots)
            {
                FavoriteCache[player.Slot] = built;
            }

            return built;
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

        private static void SetVotedWidth(int count) =>
            SetLadder(VoteIds.VotedStrip, VoteIds.VwClasses, count);

        /// <summary>Exclusive ladder class swap (vw-*). Global: the voted strip is shared.</summary>
        private static void SetLadder(string panelId, string[] classes, int value)
        {
            int n = Math.Clamp(value, 0, classes.Length - 1);
            if (_lastVotedWidth == n)
            {
                return;
            }

            if (_lastVotedWidth >= 0)
            {
                CustomHud.SetHasClassGlobal(panelId, classes[_lastVotedWidth], false);
            }

            CustomHud.SetHasClassGlobal(panelId, classes[n], true);
            _lastVotedWidth = n;
        }

        private static void PaintPool(
            CCSPlayerController player,
            Pool pool,
            IReadOnlyList<string> order,
            string? mine,
            HashSet<string> favorites,
            int limit,
            bool favoritesOnly)
        {
            int slots = pool == Pool.Voted ? VotedSlotCount : SlotCount;
            int painted = Math.Min(order.Count, limit);
            for (int i = 0; i < slots; i++)
            {
                if (CustomHud.TickWrites >= PaintWriteBudget)
                {
                    InvalidateRememberFrom(player, pool, i);
                    return;
                }

                bool hidden = i >= painted
                    || (pool == Pool.Grid && favoritesOnly && !favorites.Contains(order[i]));
                if (hidden)
                {
                    // Tail is hidden for everyone; a favorites-only filter hides per player.
                    PaintEmpty(player, pool, i, shared: i >= painted);
                    continue;
                }

                int votes = _mode == Mode.Browse ? 0 : _session?.CountOf(order[i]) ?? 0;
                PaintTile(player, pool, i, order[i], votes, mine, favorites);
            }
        }

        private static void InvalidateRememberFrom(CCSPlayerController player, Pool pool, int startIndex)
        {
            int slots = pool == Pool.Voted ? VotedSlotCount : SlotCount;
            for (int i = startIndex; i < slots; i++)
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
            bool shared = pool == Pool.Voted;

            if (shared)
            {
                CustomHud.SetHasClassGlobal(slotId, "is-off", false);
            }
            else
            {
                // Grid visibility is global; per-player only clears a
                // favorites-only hide from an earlier paint.
                CustomHud.SetHasClassGlobal(slotId, "is-off", false);
                if (CustomHud.TryGetPlayerClass(player, slotId, "is-off", out bool hiddenForPlayer) && hiddenForPlayer)
                {
                    CustomHud.SetHasClass(player, slotId, "is-off", false);
                }
            }

            CustomHud.SetHasClassGlobal(tileId, "is-voted", votes > 0);
            CustomHud.SetHasClassGlobal(tileId, "is-done", isWinner);
            CustomHud.SetHasClassGlobal(tileId, "is-lost", isLost);
            CustomHud.SetTextGlobal(Panel, VoteVar(pool, index), votes.ToString());
            CustomHud.SetTextGlobal(Panel, NameVar(pool, index), MapNameDisplay.ForHud(_formatMapName(map)));
            PaintRating(index, pool, map);
            _origins.TryGetValue(map, out MapOrigin origin);
            CustomHud.SetMapImageGlobal(ShotId(pool, index), map, origin);

            // These depend on the viewer, even on the shared grid.
            CustomHud.SetHasClass(player, tileId, "is-fav", isFav);
            CustomHud.SetHasClass(player, tileId, "is-self", !_revealing && mine == map);
            CustomHud.SetHasClass(player, favId, "is-on", isFav);
            Remember(player, pool, index, map);
        }

        /// <summary>Net like/dislike badge in the tile's top-left; hidden until a map has any rating.</summary>
        private static void PaintRating(int index, Pool pool, string? map)
        {
            string ratingId = RatingId(pool, index);
            int total = map is null ? 0 : (_archive?.RatingTotal(map) ?? 0);
            int score = map is null ? 0 : (_archive?.RatingScore(map) ?? 0);
            bool rated = total > 0;

            CustomHud.SetHasClassGlobal(ratingId, "is-off", !rated);
            CustomHud.SetHasClassGlobal(ratingId, "is-positive", rated && score > 0);
            CustomHud.SetHasClassGlobal(ratingId, "is-negative", rated && score < 0);
            CustomHud.SetTextGlobal(Panel, RatingVar(pool, index), rated ? FormatRating(score) : string.Empty);
        }

        /// <summary>Compact rating text; 100+ saturates so the 28px badge never clips.</summary>
        private static string FormatRating(int score) =>
            score >= 100 ? "99+"
            : score <= -100 ? "-99"
            : score > 0 ? $"+{score}"
            : score.ToString();

        private static void PaintEmpty(CCSPlayerController player, Pool pool, int index, bool shared)
        {
            // The tile is hidden, so its text/classes/image can keep their old
            // values until a later map reuses the slot; repainting then writes
            // the differences from these caches.
            string slotId = SlotId(pool, index);
            if (pool == Pool.Voted || shared)
            {
                CustomHud.SetHasClassGlobal(slotId, "is-off", true);
                // A per-player false from an earlier paint would still win over the global hide.
                if (pool == Pool.Grid
                    && CustomHud.TryGetPlayerClass(player, slotId, "is-off", out bool shownForPlayer)
                    && !shownForPlayer)
                {
                    CustomHud.SetHasClass(player, slotId, "is-off", true);
                }
            }
            else
            {
                CustomHud.SetHasClass(player, slotId, "is-off", true);
            }

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

        private static void SetDirty(int slot, bool dirty)
        {
            if (slot is < 0 or >= MaxSlots || Dirty[slot] == dirty)
            {
                return;
            }

            Dirty[slot] = dirty;
            _dirtyCount += dirty ? 1 : -1;
        }

        private static void ResetSlotState(int slot)
        {
            Minimized[slot] = false;
            SetDirty(slot, false);
            FavoriteCache[slot] = null;
            RevealPending[slot] = false;

            for (int i = 0; i < SlotCount; i++)
            {
                GridMaps[slot, i] = null;
            }

            for (int i = 0; i < VotedSlotCount; i++)
            {
                VotedMaps[slot, i] = null;
            }
        }

        private static void ResetAllSlots()
        {
            Array.Fill(Minimized, false);
            Array.Fill(Dirty, false);
            Array.Fill(FavoriteCache, null);
            Array.Fill(RevealPending, false);
            Array.Fill(Pending, false);
            PendingOpens.Clear();
            _typeSelection = 0;
            _sortSelection = -1;
            _dirtyCount = 0;
            for (int s = 0; s < MaxSlots; s++)
            {
                for (int i = 0; i < SlotCount; i++)
                {
                    GridMaps[s, i] = null;
                }

                for (int i = 0; i < VotedSlotCount; i++)
                {
                    VotedMaps[s, i] = null;
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

        private static string RatingId(Pool pool, int index) =>
            pool == Pool.Voted ? VoteIds.VotedRatingIds[index] : VoteIds.GridRatingIds[index];

        private static string RatingVar(Pool pool, int index) =>
            pool == Pool.Voted ? VoteIds.VotedRatingVars[index] : VoteIds.GridRatingVars[index];

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
                && index is >= 0 and < VotedSlotCount)
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
                return int.TryParse(rest, out index) && index is >= 0 and < VotedSlotCount;
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
            foreach (CCSPlayerController player in Humans.GetHumans())
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
