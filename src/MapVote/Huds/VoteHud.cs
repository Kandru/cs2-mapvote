using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using MapVote.Config;
using MapVote.Maps;
using MapVote.Players;
using MapVote.Utils;
using MapVote.Voting;

namespace MapVote.Huds
{
    /// <summary>
    /// View of layout v2. Every viewer has a private layout entity, so the whole overlay is
    /// painted per player; the CustomHud diff caches make unchanged values free.
    /// </summary>
    public static class VoteHud
    {
        public const string Panel = VoteIds.Root;
        public const int SlotCount = VoteIds.SlotCount;
        private const int RankSlots = VoteIds.RankSlots;
        private const int FilterTypeSlots = VoteIds.FilterTypeSlots;
        private const int FilterSortSlots = VoteIds.FilterSortSlots;
        private const int UrgentSeconds = 5;

        private const int MaxSlots = 65;
        /// <summary>Native writes per server tick, shared by every player and caller.</summary>
        private const int PaintWriteBudget = 128;

        private enum Mode
        {
            Vote,
            Browse,
        }

        private static Mode _mode = Mode.Vote;
        private static VoteSession? _session;
        private static FiltersConfig _filters = new();
        private static PlayerArchive? _archive;
        private static MapPlayCounts? _plays;
        private static Func<CCSPlayerController?, string, string>? _text;
        private static Dictionary<string, MapOrigin> _origins = new(StringComparer.Ordinal);
        private static Func<string, string> _formatMapName = static name => name;
        private static string? _currentMap;
        private static MapOrigin _currentOrigin = MapOrigin.Local;
        private static bool _revealing;
        private static string? _winner;
        private static DateTime? _deadlineUtc;
        private static int _lastSecond = -1;

        // Base grid order for the bound list. Sorting is per player on top of it.
        private static List<string> _grid = [];
        private static readonly Dictionary<string, int> Masks = new(StringComparer.Ordinal);
        private static readonly int[] TypeCounts = new int[FilterTypeSlots];
        private static int _typeChips;
        private static List<string> _ranked = [];

        private static readonly int[] SortSelection = BuildFilled(-1);
        private static readonly List<string>?[] GridOrder = new List<string>?[MaxSlots];
        private static readonly bool[] Minimized = new bool[MaxSlots];
        private static readonly bool[] Dirty = new bool[MaxSlots];
        private static readonly HashSet<string>?[] FavoriteCache = new HashSet<string>?[MaxSlots];
        private static readonly int[] TypeSelection = new int[MaxSlots];
        private static readonly bool[] FavoritesOnly = new bool[MaxSlots];
        /// <summary>Grid slot that carries the viewer's is-self, -1 for none.</summary>
        private static readonly int[] SelfSlot = BuildFilled(-1);
        /// <summary>Countdown: 0 idle/running, 1 = set span-N, 2 = set is-running; each step a later tick than <see cref="TimerTick"/>.</summary>
        private static readonly int[] TimerStage = new int[MaxSlots];
        private static readonly int[] TimerTick = BuildFilled(-1);
        /// <summary>Which of the two alternating blocked-sound classes is applied per slot.</summary>
        private static readonly bool[] BlockedSoundSecond = new bool[MaxSlots];
        /// <summary>Reveal reached this open player while the tick budget was spent.</summary>
        private static readonly bool[] RevealPending = new bool[MaxSlots];
        /// <summary>Opens waiting for tick budget, first in first out. Queued players are not open.</summary>
        private static readonly List<int> PendingOpens = [];
        private static readonly bool[] Pending = new bool[MaxSlots];
        private static readonly bool[] PendingCapture = new bool[MaxSlots];
        private static int _dirtyCount;

        private static int[] BuildFilled(int value)
        {
            int[] values = new int[MaxSlots];
            Array.Fill(values, value);
            return values;
        }

        public static void Bind(
            VoteSession session,
            FiltersConfig filters,
            PlayerArchive archive,
            MapPlayCounts plays,
            Func<CCSPlayerController?, string, string> text,
            IReadOnlyDictionary<string, MapOrigin> origins,
            string? currentMap,
            MapOrigin currentOrigin,
            bool cleanMapNames)
        {
            BindCore(Mode.Vote, session, session.GridOrder(), filters, archive, plays, text, origins,
                currentMap, currentOrigin, cleanMapNames);
        }

        /// <summary>
        /// Binds the same overlay to a plain map list (!maps): shared live ranking, sort chips on.
        /// A nomination session makes the cards clickable; null keeps browse-only.
        /// </summary>
        public static void BindBrowse(
            FiltersConfig filters,
            PlayerArchive archive,
            MapPlayCounts plays,
            Func<CCSPlayerController?, string, string> text,
            IReadOnlyDictionary<string, MapOrigin> origins,
            IReadOnlyList<string> maps,
            string? currentMap,
            MapOrigin currentOrigin,
            bool cleanMapNames,
            VoteSession? nominations)
        {
            BindCore(Mode.Browse, nominations, maps, filters, archive, plays, text, origins,
                currentMap, currentOrigin, cleanMapNames);
        }

        private static void BindCore(
            Mode mode,
            VoteSession? session,
            IReadOnlyList<string> source,
            FiltersConfig filters,
            PlayerArchive archive,
            MapPlayCounts plays,
            Func<CCSPlayerController?, string, string> text,
            IReadOnlyDictionary<string, MapOrigin> origins,
            string? currentMap,
            MapOrigin currentOrigin,
            bool cleanMapNames)
        {
            _mode = mode;
            _session = session;
            _filters = filters;
            _archive = archive;
            _plays = plays;
            _text = text;
            _origins = new Dictionary<string, MapOrigin>(origins, StringComparer.Ordinal);
            _formatMapName = cleanMapNames ? MapNameDisplay.Format : static name => name;
            // Selection already caps the source at MapCatalog.MaxMaps (= SlotCount); keep it
            // whole so every sort (especially rating) sees the complete list, not a prefix.
            _grid = MapOrder.Sort(source, _formatMapName);
            _currentMap = string.IsNullOrWhiteSpace(currentMap) ? null : currentMap;
            _currentOrigin = currentOrigin;
            _revealing = false;
            _winner = null;
            _deadlineUtc = null;
            _lastSecond = -1;
            _ranked = [];
            ResetAllSlots();
            RebuildGrid();
        }

        public static bool IsBrowsing => _mode == Mode.Browse;

        private static bool IsBound => _mode == Mode.Browse || _session is not null;

        private static void Clear()
        {
            _mode = Mode.Vote;
            _session = null;
            _grid = [];
            _archive = null;
            _plays = null;
            _text = null;
            _origins = new Dictionary<string, MapOrigin>(StringComparer.Ordinal);
            _formatMapName = static name => name;
            _currentMap = null;
            _currentOrigin = MapOrigin.Local;
            _revealing = false;
            _winner = null;
            _deadlineUtc = null;
            _lastSecond = -1;
            _ranked = [];
            ResetAllSlots();
            RebuildGrid();
        }

        /// <summary>Base grid order, map type masks and counts. Per-player sorts are built on demand.</summary>
        private static void RebuildGrid()
        {
            Masks.Clear();
            _typeChips = _filters.Enabled ? Math.Min(_filters.Types.Count, FilterTypeSlots - 1) : 0;
            Array.Fill(TypeCounts, 0);
            TypeCounts[0] = _grid.Count;
            foreach (string map in _grid)
            {
                int mask = 1;
                for (int t = 0; t < _typeChips; t++)
                {
                    if (MapTypeFilter.MatchesType(map, _filters.Types[t], _filters.Types))
                    {
                        mask |= 1 << (t + 1);
                        TypeCounts[t + 1]++;
                    }
                }

                Masks[map] = mask;
            }
        }

        /// <summary>CustomHud restores XML defaults itself; only the view state of the slot resets here.</summary>
        public static void WriteDefaults(CCSPlayerController player)
        {
            if (player.Slot is >= 0 and < MaxSlots)
            {
                ResetSlotState(player.Slot);
            }
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

        /// <summary>Countdown target (vote: reveal, reveal: changelevel). Restarts the bar of every open viewer.</summary>
        public static void SetDeadline(DateTime deadlineUtc)
        {
            if (_mode != Mode.Vote)
            {
                return;
            }

            _deadlineUtc = deadlineUtc;
            _lastSecond = -1;
            ForEachOpen(StartTimer);
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
            // The layout entity can be missing after a map change or a failed spawn: do not
            // call anything on it and tell the viewer that the overlay could not open.
            if (!CustomHud.EnsureSpawned(player))
            {
                ReportOpenFailure(player);
                return;
            }

            ResetSlotState(player.Slot);
            Paint(player);
            if (!HudMenu.Open(player, new HudMenuOpenOptions
                {
                    OnButton = OnButton,
                    OnClosed = OnClosed,
                    CaptureMouse = captureMouse,
                }))
            {
                ReportOpenFailure(player);
                return;
            }

            StartTimer(player);
            PluginLog.Debug($"hud open: {Humans.Label(player)} slot={player.Slot}");
        }

        private static void ReportOpenFailure(CCSPlayerController player)
        {
            PluginLog.Info($"hud open failed: {Humans.Label(player)} slot={player.Slot}");
            if (player is not { IsValid: true })
            {
                return;
            }

            string message = T(player, "hud.open_failed");
            player.PrintToChat(("{green}[MAPVOTE]{default} " + message).ReplaceColorTags());
        }

        private static void OnClosed(CCSPlayerController player)
        {
            if (player.Slot is >= 0 and < MaxSlots)
            {
                ResetSlotState(player.Slot);
            }
        }

        public static void PaintRevealFor(CCSPlayerController player, string winner)
        {
            if (_mode != Mode.Vote || _session is null || !Humans.IsHumanViewer(player))
            {
                return;
            }

            if (!_revealing || _winner != winner)
            {
                _revealing = true;
                _winner = winner;
                ForEachOpen(SetDirtyOpen);
            }

            if (player.Slot is >= 0 and < MaxSlots && Pending[player.Slot])
            {
                // Not open yet; DrainPendingOpens opens it in the reveal state.
                return;
            }

            ApplyRevealPaint(player);
        }

        private static void SetDirtyOpen(CCSPlayerController player) => SetDirty(player.Slot, true);

        /// <summary>Full repaint of every open viewer; the per-entity caches drop unchanged values.</summary>
        public static void PaintAll()
        {
            if (!IsBound)
            {
                return;
            }

            ForEachOpen(SetDirtyOpen);
            PaintDirty();
        }

        private static void Paint(CCSPlayerController player)
        {
            if (!IsBound || !Humans.IsHumanViewer(player) || player.Slot is < 0 or >= MaxSlots || Pending[player.Slot])
            {
                return;
            }

            if (CustomHud.TickWrites >= PaintWriteBudget)
            {
                // Postponed, not partial: OnTick finishes it once the budget is back.
                SetDirty(player.Slot, true);
                return;
            }

            if (RevealPending[player.Slot])
            {
                ApplyRevealPaint(player);
                return;
            }

            PaintPlayer(player);
            SetDirty(player.Slot, CustomHud.TickWrites >= PaintWriteBudget);
        }

        public static void OnTick()
        {
            if (!IsBound)
            {
                return;
            }

            if (PendingOpens.Count > 0)
            {
                DrainPendingOpens();
            }

            TickCountdown();
            StepTimers();
            if (_dirtyCount > 0)
            {
                PaintDirty();
            }
        }

        private static void PaintDirty()
        {
            if (CustomHud.TickWrites >= PaintWriteBudget)
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
            if (player.Slot is < 0 or >= MaxSlots)
            {
                return;
            }

            if (CustomHud.TickWrites >= PaintWriteBudget)
            {
                // Paint (via OnTick) finishes the reveal once budget is back.
                RevealPending[player.Slot] = true;
                SetDirty(player.Slot, true);
                return;
            }

            RevealPending[player.Slot] = false;
            HudMenu.DropCapture(player);
            Minimized[player.Slot] = false;
            PaintPlayer(player);
            SetDirty(player.Slot, CustomHud.TickWrites >= PaintWriteBudget);
        }

        // ---------------------------------------------------------------- clicks

        private static void OnButton(CCSPlayerController player, string buttonId)
        {
            bool browsing = _mode == Mode.Browse;
            bool interactive = browsing || _session is { IsOpen: true };
            if (!interactive || player.Slot is < 0 or >= MaxSlots)
            {
                return;
            }

            int slot = player.Slot;
            if (buttonId == VoteIds.Minimize)
            {
                if (browsing)
                {
                    HudMenu.Close(player);
                    return;
                }

                Minimized[slot] = !Minimized[slot];
                CustomHud.SetHasClass(player, Panel, "is-minimized", Minimized[slot]);
                CustomHud.SetText(player, Panel, VoteIds.HideVar, HideText(player));
                return;
            }

            if (Minimized[slot])
            {
                return;
            }

            if (TryParseIndex(buttonId, "mv-ft-", FilterTypeSlots, out int chip))
            {
                if (chip <= _typeChips && (chip == 0 || TypeCounts[chip] > 0))
                {
                    TypeSelection[slot] = TypeSelection[slot] == chip ? 0 : chip;
                    Paint(player);
                }

                return;
            }

            if (buttonId == VoteIds.FavOnly)
            {
                if (_filters.Enabled && VoteView.AvailableFavoriteCount(FavoritesOf(player), _grid) > 0)
                {
                    FavoritesOnly[slot] = !FavoritesOnly[slot];
                    Paint(player);
                }

                return;
            }

            if (TryParseIndex(buttonId, "mv-fs-", FilterSortSlots, out int sortChip))
            {
                ApplySortClick(player, SortIndexForChip(sortChip));
                return;
            }

            if (_revealing || player.AuthorizedSteamID is null)
            {
                return;
            }

            if (buttonId == VoteIds.CurrentFav)
            {
                ToggleFavorite(player, _currentMap);
                return;
            }

            if (buttonId == VoteIds.Like || buttonId == VoteIds.Dislike)
            {
                ToggleRating(player, buttonId == VoteIds.Like ? PlayerRating.Like : PlayerRating.Dislike);
                return;
            }

            IReadOnlyList<string> order = OrderFor(slot);
            if (TryParseIndex(buttonId, "mv-fav-", SlotCount, out int favIndex))
            {
                if (favIndex < order.Count)
                {
                    ToggleFavorite(player, order[favIndex]);
                }

                return;
            }

            if (browsing && _session is not { IsOpen: true })
            {
                // Browse-only mode has no vote target: map cards are not clickable.
                return;
            }

            string? map = null;
            if (TryParseIndex(buttonId, "mv-rank-", RankSlots, out int rank))
            {
                map = rank < _ranked.Count ? _ranked[rank] : null;
            }
            else if (TryParseIndex(buttonId, "mv-", SlotCount, out int index))
            {
                map = index < order.Count ? order[index] : null;
            }

            if (map is not null)
            {
                CastVote(player, map);
            }
        }

        private static void CastVote(CCSPlayerController player, string map)
        {
            ulong steamId = player.AuthorizedSteamID!.SteamId64;
            VoteCastResult result = _session!.Cast(steamId, map);
            if (result == VoteCastResult.RateLimited)
            {
                // No visual feedback: just the blocked sound, and no repaint for anyone.
                PlayBlockedSound(player);
                PluginLog.Debug(() => $"vote blocked (cooldown) {Humans.Label(player, steamId)} → {map}");
                return;
            }

            if (result is not (VoteCastResult.Accepted or VoteCastResult.Changed))
            {
                return;
            }

            PluginLog.Debug(
                () => $"{(_mode == Mode.Browse ? "nominate" : "cast")} {Humans.Label(player, steamId)} → {map}");
            PaintAll();
        }

        /// <summary>
        /// Replays the blocked sound for one viewer by alternating two root classes. Panorama
        /// fires <c>sound</c> when a selector applies, so a repeated class would stay silent.
        /// </summary>
        private static void PlayBlockedSound(CCSPlayerController player)
        {
            int slot = player.Slot;
            if (slot is < 0 or >= MaxSlots)
            {
                return;
            }

            bool second = !BlockedSoundSecond[slot];
            BlockedSoundSecond[slot] = second;
            CustomHud.SetHasClass(player, Panel, VoteIds.BlockedSoundClasses[0], !second);
            CustomHud.SetHasClass(player, Panel, VoteIds.BlockedSoundClasses[1], second);
        }

        private static void ApplySortClick(CCSPlayerController player, int sortIndex)
        {
            if (_mode != Mode.Browse || !_filters.Enabled || sortIndex < 0 || sortIndex >= _filters.Sorts.Count)
            {
                return;
            }

            int slot = player.Slot;
            SortSelection[slot] = SortSelection[slot] == sortIndex ? -1 : sortIndex;
            RebuildPlayerOrder(slot, FavoritesOf(player));
            SelfSlot[slot] = -1;
            Paint(player);
        }

        /// <summary>Rebuild one viewer's sorted order; the base grid itself never moves.</summary>
        private static void RebuildPlayerOrder(int slot, IReadOnlySet<string>? favorites = null)
        {
            if (slot is < 0 or >= MaxSlots)
            {
                return;
            }

            int index = SortSelection[slot];
            if (_mode != Mode.Browse
                || !_filters.Enabled
                || index < 0
                || index >= _filters.Sorts.Count)
            {
                GridOrder[slot] = null;
                return;
            }

            GridOrder[slot] = MapSort.Apply(
                _grid,
                _grid,
                _filters.Sorts[index],
                m => _plays?.CountOf(m) ?? 0,
                m => _archive?.RatingScore(m) ?? 0,
                _formatMapName,
                favorites);
        }

        /// <summary>Map an <c>mv-fs-*</c> chip onto the matching entry in sorts.</summary>
        private static int SortIndexForChip(int chipIndex) =>
            chipIndex >= 0 && chipIndex < _filters.Sorts.Count ? chipIndex : -1;

        private static IReadOnlyList<string> OrderFor(int slot) =>
            _mode == Mode.Browse && slot is >= 0 and < MaxSlots && GridOrder[slot] is { } order
                ? order
                : _grid;

        private static void ToggleFavorite(CCSPlayerController player, string? map)
        {
            if (_archive is null || player.AuthorizedSteamID is null || map is null)
            {
                return;
            }

            PlayerState state = StateOf(player);
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
            FavoriteCache[player.Slot] = null;
            int sortIndex = SortSelection[player.Slot];
            if (sortIndex >= 0
                && sortIndex < _filters.Sorts.Count
                && string.Equals(_filters.Sorts[sortIndex].By, "favorite", StringComparison.OrdinalIgnoreCase))
            {
                RebuildPlayerOrder(player.Slot, FavoritesOf(player));
                SelfSlot[player.Slot] = -1;
            }

            Paint(player);
        }

        private static void ToggleRating(CCSPlayerController player, PlayerRating clicked)
        {
            if (_archive is null || player.AuthorizedSteamID is null || _currentMap is null)
            {
                return;
            }

            PlayerState state = StateOf(player);
            PlayerRating next = VoteView.NextRating(PlayerArchive.RatingOf(state, _currentMap), clicked);
            if (!_archive.SetRating(state.SteamId, _currentMap, next))
            {
                return;
            }

            // Rating sorts depend on the global score; rebuild only the affected viewers.
            for (int slot = 0; slot < MaxSlots; slot++)
            {
                if (SortSelection[slot] >= 0
                    && SortSelection[slot] < _filters.Sorts.Count
                    && string.Equals(_filters.Sorts[SortSelection[slot]].By, "rating", StringComparison.OrdinalIgnoreCase))
                {
                    RebuildPlayerOrder(slot);
                    SelfSlot[slot] = -1;
                }
            }

            PaintAll();
        }

        private static PlayerState StateOf(CCSPlayerController player)
        {
            string steamId = player.AuthorizedSteamID!.SteamId64.ToString();
            PlayerState state = _archive!.GetOrCreate(steamId);
            if (state.Username.Length == 0 || state.Username == steamId)
            {
                state.Username = player.PlayerName ?? steamId;
            }

            return state;
        }

        private static HashSet<string> FavoritesOf(CCSPlayerController player)
        {
            if (player.Slot is >= 0 and < MaxSlots && FavoriteCache[player.Slot] is HashSet<string> cached)
            {
                return cached;
            }

            PlayerState? state = _archive is null || player.AuthorizedSteamID is null
                ? null
                : _archive.TryGet(player.AuthorizedSteamID.SteamId64.ToString());
            HashSet<string> built = state is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(state.Favorites, StringComparer.Ordinal);
            if (player.Slot is >= 0 and < MaxSlots)
            {
                FavoriteCache[player.Slot] = built;
            }

            return built;
        }

        // ---------------------------------------------------------------- paint

        private static void PaintPlayer(CCSPlayerController player)
        {
            int slot = player.Slot;
            bool browse = _mode == Mode.Browse;
            IReadOnlyList<string> order = OrderFor(slot);
            HashSet<string> favorites = FavoritesOf(player);
            int availableFavoriteCount = VoteView.AvailableFavoriteCount(favorites, _grid);
            if (availableFavoriteCount == 0)
            {
                FavoritesOnly[slot] = false;
            }

            CustomHud.SetText(player, Panel, VoteIds.TitleVar, L(player, browse ? "hud.maps_title" : "hud.title"));
            if (_deadlineUtc is { } deadline)
            {
                int seconds = VoteView.SecondsLeft((deadline - DateTime.UtcNow).TotalSeconds);
                CustomHud.SetText(player, Panel, VoteIds.TimeVar, VoteView.FormatTime(seconds));
            }

            SetPlayerGroup(player, Panel, VoteIds.Views, browse ? 2 : _revealing ? 1 : 0);
            CustomHud.SetHasClass(player, Panel, "is-minimized", Minimized[slot]);
            CustomHud.SetText(player, Panel, VoteIds.HideVar, HideText(player));
            CustomHud.SetHasClass(player, VoteIds.Timer, "is-urgent", IsUrgent());

            PaintCurrent(player);
            if (CustomHud.TickWrites >= PaintWriteBudget)
            {
                return;
            }

            PaintRanking(player);
            if (CustomHud.TickWrites >= PaintWriteBudget)
            {
                return;
            }

            PaintFilters(player, availableFavoriteCount);
            if (CustomHud.TickWrites >= PaintWriteBudget)
            {
                return;
            }

            if (_winner is not null)
            {
                CustomHud.SetText(player, Panel, VoteIds.WinTagVar, L(player, "hud.next"));
                CustomHud.SetText(player, Panel, VoteIds.WinNameVar, Display(_winner));
                CustomHud.SetText(player, Panel, VoteIds.WinVotesVar, WinVotesText(player));
                CustomHud.SetMapImage(player, VoteIds.WinShot, _winner, OriginOf(_winner));
            }

            PaintSelf(player, order);

            if (_currentMap is not null)
            {
                CustomHud.SetHasClass(player, VoteIds.CurrentFav, "is-active", favorites.Contains(_currentMap));
                PaintOwnRating(player);
            }

            bool anyShown = false;
            for (int i = 0; i < SlotCount; i++)
            {
                if (CustomHud.TickWrites >= PaintWriteBudget)
                {
                    return;
                }

                string slotId = VoteIds.GridSlotIds[i];
                if (i >= order.Count)
                {
                    SetPlayerClass(player, slotId, "is-off", true, xmlOn: true);
                    continue;
                }

                string map = order[i];
                bool shown = IsShown(slot, map, favorites);
                anyShown |= shown;
                int votes = _session?.CountOf(map) ?? 0;
                CustomHud.SetMapImage(player, VoteIds.GridShotIds[i], map, OriginOf(map));
                CustomHud.SetText(player, Panel, VoteIds.GridNameVars[i], Display(map));
                // The badge only shows with votes-some, so zero stays the empty XML default.
                CustomHud.SetText(player, Panel, VoteIds.GridVoteVars[i], votes > 0 ? votes.ToString() : string.Empty);
                SetPlayerGroup(player, VoteIds.GridTileIds[i], VoteIds.VotesClasses, votes > 0 ? 1 : 0);
                SetPlayerClass(player, slotId, "is-off", !shown, xmlOn: true);
                CustomHud.SetHasClass(player, slotId, "is-self", i == SelfSlot[slot]);
                CustomHud.SetHasClass(player, VoteIds.GridFavIds[i], "is-active", favorites.Contains(map));
            }

            SetPlayerClass(player, VoteIds.Empty, "is-off", anyShown, xmlOn: true);
            CustomHud.SetText(player, Panel, VoteIds.EmptyVar, L(player, "hud.empty"));
        }

        private static void PaintCurrent(CCSPlayerController player)
        {
            SetPlayerClass(player, VoteIds.Current, "is-off", _currentMap is null, xmlOn: false);
            if (_currentMap is null)
            {
                return;
            }

            CustomHud.SetText(player, Panel, VoteIds.CurrentTagVar, L(player, "hud.current"));
            CustomHud.SetText(player, Panel, VoteIds.CurrentNameVar, Display(_currentMap));
            CustomHud.SetMapImage(player, VoteIds.CurrentShot, _currentMap, _currentOrigin);
            CustomHud.SetText(player, Panel, VoteIds.RateQuestionVar, L(player, "hud.rate_q"));
            CustomHud.SetText(player, Panel, VoteIds.LikeVar, L(player, "hud.like"));
            CustomHud.SetText(player, Panel, VoteIds.DislikeVar, L(player, "hud.dislike"));
            PaintRatingCounts(player);
        }

        private static void PaintRatingCounts(CCSPlayerController player)
        {
            if (_currentMap is null)
            {
                return;
            }

            CustomHud.SetText(player, Panel, VoteIds.LikeCountVar, (_archive?.LikeCount(_currentMap) ?? 0).ToString());
            CustomHud.SetText(player, Panel, VoteIds.DislikeCountVar, (_archive?.DislikeCount(_currentMap) ?? 0).ToString());
        }

        private static void PaintRanking(CCSPlayerController player)
        {
            CustomHud.SetText(player, Panel, VoteIds.RankTitleVar, L(player, "hud.rank_title"));
            CustomHud.SetText(player, Panel, VoteIds.RankEmptyVar, L(player, "hud.rank_empty"));
            CustomHud.SetText(player, Panel, VoteIds.SelfVar, L(player, "hud.self"));

            if (_session is null)
            {
                _ranked.Clear();
                SetPlayerClass(player, VoteIds.RankEmpty, "is-off", false, xmlOn: false);
                for (int i = 0; i < RankSlots; i++)
                {
                    SetPlayerClass(player, VoteIds.RankRowIds[i], "is-off", true, xmlOn: true);
                }

                return;
            }

            IReadOnlyList<string> voted = _session.VotedOrder();
            int total = 0;
            foreach (string map in voted)
            {
                total += _session.CountOf(map);
            }

            _ranked = voted.Take(RankSlots).ToList();
            SetPlayerClass(player, VoteIds.RankEmpty, "is-off", voted.Count > 0, xmlOn: false);
            for (int i = 0; i < RankSlots; i++)
            {
                bool on = i < _ranked.Count;
                SetPlayerClass(player, VoteIds.RankRowIds[i], "is-off", !on, xmlOn: true);
                if (!on)
                {
                    continue;
                }

                string map = _ranked[i];
                int votes = _session.CountOf(map);
                CustomHud.SetMapImage(player, VoteIds.RankShotIds[i], map, OriginOf(map));
                CustomHud.SetText(player, Panel, VoteIds.RankNameVars[i], Display(map));
                CustomHud.SetText(player, Panel, VoteIds.RankVoteVars[i], votes.ToString());
                SetPlayerGroup(player, VoteIds.RankFillIds[i], VoteIds.ShareClasses, VoteView.ShareStep(votes, total));
            }
        }

        private static void PaintFilters(CCSPlayerController player, int availableFavoriteCount)
        {
            int slot = player.Slot;
            bool show = _filters.Enabled;
            SetPlayerClass(player, VoteIds.Filters, "is-off", !show, xmlOn: false);
            if (!show)
            {
                return;
            }

            CustomHud.SetText(player, Panel, VoteIds.FavOnlyVar, L(player, "hud.fav_only", availableFavoriteCount));
            for (int chip = 0; chip < FilterTypeSlots; chip++)
            {
                bool on = _typeChips > 0 && chip <= _typeChips;
                SetPlayerClass(player, VoteIds.TypeChipIds[chip], "is-off", !on, xmlOn: true);
                if (!on)
                {
                    continue;
                }

                CustomHud.SetText(player, Panel, VoteIds.TypeVars[chip], ChipText(player, chip));
                SetPlayerClass(player, VoteIds.TypeChipIds[chip], "is-disabled", chip > 0 && TypeCounts[chip] == 0, xmlOn: false);
                CustomHud.SetHasClass(player, VoteIds.TypeChipIds[chip], "is-active", TypeSelection[slot] == chip);
            }

            CustomHud.SetHasClass(player, VoteIds.FavOnly, "is-active", FavoritesOnly[slot]);
            CustomHud.SetHasClass(player, VoteIds.FavOnly, "is-disabled", availableFavoriteCount == 0);

            // Sort chips reorder one viewer's grid, so they only exist in browse.
            int sortChip = 0;
            if (_mode == Mode.Browse)
            {
                for (int i = 0; i < _filters.Sorts.Count && sortChip < FilterSortSlots; i++)
                {
                    string chipId = VoteIds.SortChipIds[sortChip];
                    SetPlayerClass(player, chipId, "is-off", false, xmlOn: true);
                    CustomHud.SetHasClass(player, chipId, "is-active", SortSelection[slot] == i);
                    CustomHud.SetText(player, Panel, VoteIds.SortVars[sortChip], L(player, $"sort.{_filters.Sorts[i].Id}"));
                    sortChip++;
                }
            }

            for (; sortChip < FilterSortSlots; sortChip++)
            {
                SetPlayerClass(player, VoteIds.SortChipIds[sortChip], "is-off", true, xmlOn: true);
            }
        }

        private static string ChipText(CCSPlayerController? player, int chip) =>
            L(player, chip == 0 ? "filter.all" : $"filter.{_filters.Types[chip - 1].Id}", TypeCounts[chip]);

        private static string WinVotesText(CCSPlayerController? player)
        {
            int votes = _winner is null ? 0 : _session?.CountOf(_winner) ?? 0;
            return votes switch
            {
                0 => L(player, "hud.win_random"),
                1 => L(player, "hud.win_votes_one"),
                _ => MapNameDisplay.ForHud(string.Format(T(player, "hud.win_votes"), votes)),
            };
        }

        /// <summary>Status line, ranking marker and grid is-self: what one vote changes for a viewer.</summary>
        private static void PaintSelf(CCSPlayerController player, IReadOnlyList<string> order)
        {
            int slot = player.Slot;
            string? mine = _session is not null && player.AuthorizedSteamID is { } steam
                ? _session.VoteOf(steam.SteamId64)
                : null;

            string status = _mode == Mode.Browse
                ? _session is null ? L(player, "hud.status_browse")
                : mine is null ? L(player, "hud.status_nominate_hint")
                : MapNameDisplay.ForHud(string.Format(T(player, "hud.status_nominated"), _formatMapName(mine)))
                : _revealing ? L(player, "hud.status_closed")
                : mine is null ? L(player, "hud.status_hint")
                : MapNameDisplay.ForHud(string.Format(T(player, "hud.status_voted"), _formatMapName(mine)));
            CustomHud.SetHasClass(player, VoteIds.Status, "is-self", mine is not null);
            CustomHud.SetText(player, Panel, VoteIds.StatusVar, status);

            for (int i = 0; i < RankSlots; i++)
            {
                CustomHud.SetHasClass(player, VoteIds.RankSelfIds[i], "is-self", mine is not null && i < _ranked.Count && _ranked[i] == mine);
            }

            int next = mine is null ? -1 : IndexOf(order, mine);
            if (SelfSlot[slot] >= 0 && SelfSlot[slot] != next && SelfSlot[slot] < SlotCount)
            {
                CustomHud.SetHasClass(player, VoteIds.GridSlotIds[SelfSlot[slot]], "is-self", false);
            }

            if (next >= 0)
            {
                CustomHud.SetHasClass(player, VoteIds.GridSlotIds[next], "is-self", true);
            }

            SelfSlot[slot] = next;
        }

        private static int IndexOf(IReadOnlyList<string> order, string map)
        {
            for (int i = 0; i < order.Count; i++)
            {
                if (string.Equals(order[i], map, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void PaintOwnRating(CCSPlayerController player)
        {
            string? steamId = player.AuthorizedSteamID?.SteamId64.ToString();
            PlayerRating rating = _currentMap is null ? PlayerRating.None : _archive?.RatingOf(steamId, _currentMap) ?? PlayerRating.None;
            CustomHud.SetHasClass(player, VoteIds.Rate, "is-disabled", steamId is null);
            CustomHud.SetHasClass(player, VoteIds.Like, "is-active", rating == PlayerRating.Like);
            CustomHud.SetHasClass(player, VoteIds.Dislike, "is-active", rating == PlayerRating.Dislike);
        }

        private static bool IsShown(int slot, string map, HashSet<string> favorites) =>
            VoteView.IsVisible(
                Masks.GetValueOrDefault(map, 1),
                TypeSelection[slot],
                FavoritesOnly[slot],
                favorites.Contains(map));

        private static string HideText(CCSPlayerController player) =>
            L(player, _mode == Mode.Browse ? "hud.close" : Minimized[player.Slot] ? "hud.show" : "hud.hide");

        // ---------------------------------------------------------------- countdown

        private static bool IsUrgent() =>
            _deadlineUtc is { } deadline
            && VoteView.SecondsLeft((deadline - DateTime.UtcNow).TotalSeconds) <= UrgentSeconds;

        /// <summary>Second boundary: one text write per open viewer; budget overflow defers to PaintDirty.</summary>
        private static void TickCountdown()
        {
            if (_deadlineUtc is not { } deadline)
            {
                return;
            }

            int seconds = VoteView.SecondsLeft((deadline - DateTime.UtcNow).TotalSeconds);
            if (seconds == _lastSecond)
            {
                return;
            }

            _lastSecond = seconds;
            string text = VoteView.FormatTime(seconds);
            bool urgent = IsUrgent();
            foreach (CCSPlayerController player in Humans.GetHumans())
            {
                if (!HudMenu.IsOpen(player) || player.Slot is < 0 or >= MaxSlots)
                {
                    continue;
                }

                if (CustomHud.TickWrites >= PaintWriteBudget)
                {
                    SetDirty(player.Slot, true);
                    continue;
                }

                CustomHud.SetText(player, Panel, VoteIds.TimeVar, text);
                CustomHud.SetHasClass(player, VoteIds.Timer, "is-urgent", urgent);
            }
        }

        // The reveal drains its own fresh panel; resetting the vote bar mid-transition would refill slowly.
        private static string TimerFillId => _revealing ? VoteIds.TimerFillReveal : VoteIds.TimerFill;

        /// <summary>Reset the bar (remove both classes), then span-N and is-running in later ticks.</summary>
        private static void StartTimer(CCSPlayerController player)
        {
            if (_deadlineUtc is null || player.Slot is < 0 or >= MaxSlots)
            {
                return;
            }

            int before = CustomHud.TickWrites;
            CustomHud.SetHasClass(player, TimerFillId, "is-running", false);
            for (int s = VoteIds.SpanMin; s <= VoteIds.SpanMax; s++)
            {
                CustomHud.SetHasClass(player, TimerFillId, VoteIds.SpanClasses[s], false);
            }

            // A reset that went out needs its own tick, or span-N would animate a refill.
            TimerTick[player.Slot] = CustomHud.TickWrites != before ? Server.TickCount : -1;
            TimerStage[player.Slot] = 1;
            StepTimer(player);
        }

        private static void StepTimers()
        {
            for (int slot = 0; slot < MaxSlots; slot++)
            {
                if (TimerStage[slot] != 0
                    && Utilities.GetPlayerFromSlot(slot) is { } player
                    && HudMenu.IsOpen(player))
                {
                    StepTimer(player);
                }
            }
        }

        private static void StepTimer(CCSPlayerController player)
        {
            int slot = player.Slot;
            int now = Server.TickCount;
            if (_deadlineUtc is not { } deadline)
            {
                TimerStage[slot] = 0;
                return;
            }

            if (now <= TimerTick[slot] || CustomHud.TickWrites >= PaintWriteBudget)
            {
                return;
            }

            if (TimerStage[slot] == 1)
            {
                double remaining = (deadline - DateTime.UtcNow).TotalSeconds;
                if (remaining <= 0)
                {
                    TimerStage[slot] = 0;
                    return;
                }

                CustomHud.SetHasClass(player, TimerFillId, VoteIds.SpanClasses[VoteView.SpanFor(remaining)], true);
                TimerStage[slot] = 2;
                TimerTick[slot] = now;
            }
            else if (TimerStage[slot] == 2)
            {
                CustomHud.SetHasClass(player, TimerFillId, "is-running", true);
                TimerStage[slot] = 0;
            }
        }

        // ---------------------------------------------------------------- helpers

        private static void SetPlayerClass(CCSPlayerController player, string panelId, string className, bool on, bool xmlOn)
        {
            if (on == xmlOn && !CustomHud.TryGetPlayerClass(player, panelId, className, out _))
            {
                return;
            }

            CustomHud.SetHasClass(player, panelId, className, on);
        }

        private static void SetPlayerGroup(CCSPlayerController player, string panelId, string[] classes, int active)
        {
            for (int k = 0; k < classes.Length; k++)
            {
                SetPlayerClass(player, panelId, classes[k], k == active, xmlOn: k == 0);
            }
        }

        private static string Display(string map) => MapNameDisplay.ForHud(_formatMapName(map));

        private static MapOrigin OriginOf(string map) => _origins.GetValueOrDefault(map, MapOrigin.Local);

        private static string T(CCSPlayerController? player, string key) =>
            _text is null ? key : _text(player, key);

        /// <summary>Localized text for a dialog variable, padded so it can never equal a client token.</summary>
        private static string L(CCSPlayerController? player, string key) => MapNameDisplay.ForHud(T(player, key));

        /// <summary>Localized text with a count in parentheses, e.g. <c>All (95)</c>.</summary>
        private static string L(CCSPlayerController? player, string key, int count) =>
            MapNameDisplay.ForHud($"{T(player, key)} ({count})");

        private static bool TryParseIndex(string buttonId, string prefix, int count, out int index)
        {
            index = -1;
            return buttonId.Length > prefix.Length
                && buttonId.StartsWith(prefix, StringComparison.Ordinal)
                && char.IsAsciiDigit(buttonId[prefix.Length])
                && int.TryParse(buttonId.AsSpan(prefix.Length), out index)
                && index >= 0 && index < count;
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
            if (slot is < 0 or >= MaxSlots)
            {
                return;
            }

            Minimized[slot] = false;
            SetDirty(slot, false);
            FavoriteCache[slot] = null;
            RevealPending[slot] = false;
            TypeSelection[slot] = 0;
            FavoritesOnly[slot] = false;
            SortSelection[slot] = -1;
            GridOrder[slot] = null;
            SelfSlot[slot] = -1;
            TimerStage[slot] = 0;
            TimerTick[slot] = -1;
            // BlockedSoundSecond is intentionally not reset: its class may still be on the
            // entity, and resetting it could toggle the same class on again (no replay).
        }

        private static void ResetAllSlots()
        {
            for (int slot = 0; slot < MaxSlots; slot++)
            {
                ResetSlotState(slot);
            }

            Array.Fill(Pending, false);
            PendingOpens.Clear();
            _dirtyCount = 0;
            Array.Fill(Dirty, false);
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
    }
}
