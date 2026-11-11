using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using MapVote.Config;
using MapVote.Maps;
using MapVote.Players;
using MapVote.Utils;
using MapVote.Voting;

namespace MapVote.Huds
{
    /// <summary>
    /// View of layout v2. Everything the contract marks G is written once for all viewers
    /// (<see cref="PaintGlobal"/>); per-player state (view, filters, favourites, own vote,
    /// own rating, countdown classes) is written by <see cref="Paint"/>.
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
        private static List<string> _source = [];
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

        // Global grid: one order for everyone, computed once per change. Clicks route through it.
        private static List<string> _grid = [];
        private static readonly Dictionary<string, int> SlotOf = new(StringComparer.Ordinal);
        private static int[] _typeMask = [];
        private static readonly int[] TypeCounts = new int[FilterTypeSlots];
        private static int _typeChips;
        private static List<string> _ranked = [];
        private static int _sortSelection = -1;
        /// <summary>Some global value is not written yet; nothing opens or repaints until it is.</summary>
        private static bool _globalDirty;

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
        /// <summary>Reveal reached this open player while the tick budget was spent.</summary>
        private static readonly bool[] RevealPending = new bool[MaxSlots];
        /// <summary>Opens waiting for tick budget, first in first out. Queued players are not open.</summary>
        private static readonly List<int> PendingOpens = [];
        private static readonly bool[] Pending = new bool[MaxSlots];
        private static readonly bool[] PendingCapture = new bool[MaxSlots];
        private static int _dirtyCount;
        private static readonly HashSet<string> NoFavorites = new(StringComparer.Ordinal);

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
            BindCore(Mode.Vote, session, Shuffle(session.GridOrder()), filters, archive, plays, text, origins,
                currentMap, currentOrigin, cleanMapNames);
        }

        /// <summary>Binds the same overlay to a plain map list (!maps): no vote, no ranking, sort chips on.</summary>
        public static void BindBrowse(
            FiltersConfig filters,
            PlayerArchive archive,
            MapPlayCounts plays,
            Func<CCSPlayerController?, string, string> text,
            IReadOnlyDictionary<string, MapOrigin> origins,
            IReadOnlyList<string> maps,
            string? currentMap,
            MapOrigin currentOrigin,
            bool cleanMapNames)
        {
            BindCore(Mode.Browse, null, Shuffle(maps), filters, archive, plays, text, origins,
                currentMap, currentOrigin, cleanMapNames);
        }

        private static void BindCore(
            Mode mode,
            VoteSession? session,
            List<string> source,
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
            _source = source;
            _filters = filters;
            _archive = archive;
            _plays = plays;
            _text = text;
            _origins = new Dictionary<string, MapOrigin>(origins, StringComparer.Ordinal);
            _formatMapName = cleanMapNames ? MapNameDisplay.Format : static name => name;
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
            _source = [];
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
            _globalDirty = false;
        }

        /// <summary>Global grid order, slot table and type masks. Once per bind or sort change.</summary>
        private static void RebuildGrid()
        {
            List<string> grid = _source.Count > SlotCount ? _source.GetRange(0, SlotCount) : [.. _source];
            if (_mode == Mode.Browse
                && _filters.Enabled
                && _sortSelection >= 0
                && _sortSelection < _filters.Sorts.Count
                && !_filters.Sorts[_sortSelection].Only)
            {
                grid = MapSort.Apply(
                    grid,
                    _source,
                    _filters.Sorts[_sortSelection],
                    m => _plays?.CountOf(m) ?? 0,
                    m => _archive?.RatingScore(m) ?? 0,
                    NoFavorites,
                    _formatMapName);
            }

            _grid = grid;
            SlotOf.Clear();
            _typeChips = _filters.Enabled ? Math.Min(_filters.Types.Count, FilterTypeSlots - 1) : 0;
            _typeMask = new int[grid.Count];
            Array.Fill(TypeCounts, 0);
            TypeCounts[0] = grid.Count;
            for (int i = 0; i < grid.Count; i++)
            {
                SlotOf[grid[i]] = i;
                int mask = 1;
                for (int t = 0; t < _typeChips; t++)
                {
                    if (MapTypeFilter.MatchesType(grid[i], _filters.Types[t], _filters.Types))
                    {
                        mask |= 1 << (t + 1);
                        TypeCounts[t + 1]++;
                    }
                }

                _typeMask[i] = mask;
            }

            _globalDirty = true;
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
                // Every global text must be on the client before a panel is shown.
                if (!EnsureGlobal())
                {
                    return;
                }

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
            ResetSlotState(player.Slot);
            Paint(player);
            HudMenu.Open(player, new HudMenuOpenOptions { OnButton = OnButton, CaptureMouse = captureMouse });
            StartTimer(player);
            PluginLog.Debug($"hud open: {Humans.Label(player)} slot={player.Slot}");
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
                _globalDirty = true;
            }

            if (player.Slot is >= 0 and < MaxSlots && Pending[player.Slot])
            {
                // Not open yet; DrainPendingOpens opens it in the reveal state.
                return;
            }

            ApplyRevealPaint(player);
        }

        /// <summary>Full repaint (global and every open viewer), diffed by the CustomHud caches.</summary>
        public static void PaintAll()
        {
            if (!IsBound)
            {
                return;
            }

            _globalDirty = true;
            ForEachOpen(player => SetDirty(player.Slot, true));
            PaintDirty();
        }

        public static void Paint(CCSPlayerController player)
        {
            if (!IsBound || !Humans.IsHumanViewer(player) || player.Slot is < 0 or >= MaxSlots || Pending[player.Slot])
            {
                return;
            }

            if (CustomHud.TickWrites >= PaintWriteBudget || !EnsureGlobal())
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

            if (_globalDirty)
            {
                EnsureGlobal();
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
            _globalDirty = true;
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

            if (CustomHud.TickWrites >= PaintWriteBudget || !EnsureGlobal())
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

        public static void OnButton(CCSPlayerController player, string buttonId)
        {
            bool browsing = _mode == Mode.Browse;
            bool interactive = browsing || _session is { IsOpen: true };            if (!interactive || player.Slot is < 0 or >= MaxSlots)
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
                if (_filters.Enabled && FavoritesOf(player).Count > 0)
                {
                    FavoritesOnly[slot] = !FavoritesOnly[slot];
                    Paint(player);
                }

                return;
            }

            if (TryParseIndex(buttonId, "mv-fs-", FilterSortSlots, out int sortChip))
            {
                ApplySortClick(SortIndexForChip(sortChip));
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

            if (TryParseIndex(buttonId, "mv-fav-", SlotCount, out int favIndex))
            {
                if (favIndex < _grid.Count)
                {
                    ToggleFavorite(player, _grid[favIndex]);
                }

                return;
            }

            if (browsing)
            {
                // Browse has no vote path: map cards are not clickable.
                return;
            }

            string? map = null;
            if (TryParseIndex(buttonId, "mv-rank-", RankSlots, out int rank))
            {
                map = rank < _ranked.Count ? _ranked[rank] : null;
            }
            else if (TryParseIndex(buttonId, "mv-", SlotCount, out int index))
            {
                map = index < _grid.Count ? _grid[index] : null;
            }

            if (map is not null)
            {
                CastVote(player, map);
            }
        }

        private static void CastVote(CCSPlayerController player, string map)
        {
            ulong steamId = player.AuthorizedSteamID!.SteamId64;
            VoteSession session = _session!;
            string? previous = session.VoteOf(steamId);
            if (!session.Cast(steamId, map))
            {
                return;
            }

            string label = Humans.Label(player, steamId);
            PluginLog.Debug(previous is null ? $"cast {label} → {map}" : $"cast {label} {previous} → {map}");

            // Only the two changed cards and the ranking are global news; the rest is cached.
            if (_globalDirty || CustomHud.TickWrites >= PaintWriteBudget)
            {
                _globalDirty = true;
            }
            else
            {
                PaintRanking();
                PaintCard(previous);
                PaintCard(map);
            }

            ForEachOpen(viewer =>
            {
                if (_globalDirty || CustomHud.TickWrites >= PaintWriteBudget)
                {
                    SetDirty(viewer.Slot, true);
                }
                else
                {
                    PaintSelf(viewer);
                }
            });
        }

        private static void ApplySortClick(int sortIndex)
        {
            if (_mode != Mode.Browse || !_filters.Enabled || sortIndex < 0 || sortIndex >= _filters.Sorts.Count)
            {
                return;
            }

            // The order is global, so every open viewer repaints against the new slot table.
            _sortSelection = _sortSelection == sortIndex ? -1 : sortIndex;
            RebuildGrid();
            PaintAll();
        }

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

            // Counters are global, the selection is the clicker's only: feedback in this tick.
            if (CustomHud.TickWrites >= PaintWriteBudget)
            {
                _globalDirty = true;
                SetDirty(player.Slot, true);
                return;
            }

            PaintRatingCounts();
            PaintOwnRating(player);
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

        // ---------------------------------------------------------------- global paint

        private static bool EnsureGlobal()
        {
            if (!_globalDirty)
            {
                return true;
            }

            if (CustomHud.TickWrites >= PaintWriteBudget)
            {
                return false;
            }

            _globalDirty = !PaintGlobal();
            return !_globalDirty;
        }

        /// <summary>Writes every G value; false when the budget ran out (re-run: cached values cost nothing).</summary>
        private static bool PaintGlobal()
        {
            if (!IsBound)
            {
                return true;
            }

            bool browse = _mode == Mode.Browse;
            CustomHud.SetTextGlobal(Panel, VoteIds.TitleVar, L(null, browse ? "hud.maps_title" : "hud.title"));
            if (_deadlineUtc is { } deadline)
            {
                _lastSecond = VoteView.SecondsLeft((deadline - DateTime.UtcNow).TotalSeconds);
                CustomHud.SetTextGlobal(Panel, VoteIds.TimeVar, VoteView.FormatTime(_lastSecond));
            }

            PaintCurrentGlobal();
            if (CustomHud.TickWrites >= PaintWriteBudget)
            {
                return false;
            }

            if (!browse)
            {
                CustomHud.SetTextGlobal(Panel, VoteIds.RankTitleVar, L(null, "hud.rank_title"));
                CustomHud.SetTextGlobal(Panel, VoteIds.RankEmptyVar, L(null, "hud.rank_empty"));
                CustomHud.SetTextGlobal(Panel, VoteIds.SelfVar, L(null, "hud.self"));
                PaintRanking();
                if (CustomHud.TickWrites >= PaintWriteBudget)
                {
                    return false;
                }
            }

            PaintFiltersGlobal();
            if (CustomHud.TickWrites >= PaintWriteBudget)
            {
                return false;
            }

            if (_winner is not null)
            {
                CustomHud.SetTextGlobal(Panel, VoteIds.WinTagVar, L(null, "hud.next"));
                CustomHud.SetTextGlobal(Panel, VoteIds.WinNameVar, Display(_winner));
                CustomHud.SetTextGlobal(Panel, VoteIds.WinVotesVar, WinVotesText(null));
                CustomHud.SetMapImageGlobal(VoteIds.WinShot, _winner, OriginOf(_winner));
            }

            for (int i = 0; i < _grid.Count; i++)
            {
                if (CustomHud.TickWrites >= PaintWriteBudget)
                {
                    return false;
                }

                PaintCard(i);
            }

            return true;
        }

        private static void PaintCurrentGlobal()
        {
            SetGlobalClass(VoteIds.Current, "is-off", _currentMap is null, xmlOn: false);
            if (_currentMap is null)
            {
                return;
            }

            CustomHud.SetTextGlobal(Panel, VoteIds.CurrentTagVar, L(null, "hud.current"));
            CustomHud.SetTextGlobal(Panel, VoteIds.CurrentNameVar, Display(_currentMap));
            CustomHud.SetMapImageGlobal(VoteIds.CurrentShot, _currentMap, _currentOrigin);
            CustomHud.SetTextGlobal(Panel, VoteIds.RateQuestionVar, L(null, "hud.rate_q"));
            CustomHud.SetTextGlobal(Panel, VoteIds.LikeVar, L(null, "hud.like"));
            CustomHud.SetTextGlobal(Panel, VoteIds.DislikeVar, L(null, "hud.dislike"));
            PaintRatingCounts();
        }

        private static void PaintRatingCounts()
        {
            if (_currentMap is null)
            {
                return;
            }

            CustomHud.SetTextGlobal(Panel, VoteIds.LikeCountVar, (_archive?.LikeCount(_currentMap) ?? 0).ToString());
            CustomHud.SetTextGlobal(Panel, VoteIds.DislikeCountVar, (_archive?.DislikeCount(_currentMap) ?? 0).ToString());
        }

        private static void PaintRanking()
        {
            if (_session is null)
            {
                return;
            }

            IReadOnlyList<string> voted = _session.VotedOrder();
            int total = 0;
            foreach (string map in voted)
            {
                total += _session.CountOf(map);
            }

            _ranked = voted.Take(RankSlots).ToList();
            SetGlobalClass(VoteIds.RankEmpty, "is-off", voted.Count > 0, xmlOn: false);
            for (int i = 0; i < RankSlots; i++)
            {
                bool on = i < _ranked.Count;
                SetGlobalClass(VoteIds.RankRowIds[i], "is-off", !on, xmlOn: true);
                if (!on)
                {
                    continue;
                }

                string map = _ranked[i];
                int votes = _session.CountOf(map);
                CustomHud.SetMapImageGlobal(VoteIds.RankShotIds[i], map, OriginOf(map));
                CustomHud.SetTextGlobal(Panel, VoteIds.RankNameVars[i], Display(map));
                CustomHud.SetTextGlobal(Panel, VoteIds.RankVoteVars[i], votes.ToString());
                SetGlobalGroup(VoteIds.RankFillIds[i], VoteIds.ShareClasses, VoteView.ShareStep(votes, total));
            }
        }

        private static void PaintFiltersGlobal()
        {
            bool show = _filters.Enabled;
            SetGlobalClass(VoteIds.Filters, "is-off", !show, xmlOn: false);
            CustomHud.SetTextGlobal(Panel, VoteIds.FavOnlyVar, L(null, "hud.fav_only"));
            for (int chip = 0; chip < FilterTypeSlots; chip++)
            {
                bool on = show && _typeChips > 0 && chip <= _typeChips;
                SetGlobalClass(VoteIds.TypeChipIds[chip], "is-off", !on, xmlOn: true);
                if (!on)
                {
                    continue;
                }

                CustomHud.SetTextGlobal(Panel, VoteIds.TypeVars[chip], ChipText(null, chip));
                SetGlobalClass(VoteIds.TypeChipIds[chip], "is-disabled", chip > 0 && TypeCounts[chip] == 0, xmlOn: false);
            }

            // Sort chips reorder the global grid, so they only exist in browse.
            int sortChip = 0;
            if (show && _mode == Mode.Browse)
            {
                for (int i = 0; i < _filters.Sorts.Count && sortChip < FilterSortSlots; i++)
                {
                    SortConfig sort = _filters.Sorts[i];
                    if (sort.Only)
                    {
                        continue;
                    }

                    string chipId = VoteIds.SortChipIds[sortChip];
                    SetGlobalClass(chipId, "is-off", false, xmlOn: true);
                    SetGlobalClass(chipId, "is-active", _sortSelection == i, xmlOn: false);
                    CustomHud.SetTextGlobal(Panel, VoteIds.SortVars[sortChip], L(null, $"sort.{sort.Id}"));
                    sortChip++;
                }
            }

            for (; sortChip < FilterSortSlots; sortChip++)
            {
                SetGlobalClass(VoteIds.SortChipIds[sortChip], "is-off", true, xmlOn: true);
            }
        }

        private static void PaintCard(string? map)
        {
            if (map is not null && SlotOf.TryGetValue(map, out int index))
            {
                PaintCard(index);
            }
        }

        private static void PaintCard(int index)
        {
            string map = _grid[index];
            int votes = _session?.CountOf(map) ?? 0;
            CustomHud.SetMapImageGlobal(VoteIds.GridShotIds[index], map, OriginOf(map));
            CustomHud.SetTextGlobal(Panel, VoteIds.GridNameVars[index], Display(map));
            // The badge only shows with votes-some, so zero stays the empty XML default.
            CustomHud.SetTextGlobal(Panel, VoteIds.GridVoteVars[index], votes > 0 ? votes.ToString() : string.Empty);
            SetGlobalGroup(VoteIds.GridTileIds[index], VoteIds.VotesClasses, votes > 0 ? 1 : 0);
        }

        // ---------------------------------------------------------------- per-player paint

        private static void PaintPlayer(CCSPlayerController player)
        {
            int slot = player.Slot;
            bool browse = _mode == Mode.Browse;
            HashSet<string> favorites = FavoritesOf(player);
            if (favorites.Count == 0)
            {
                FavoritesOnly[slot] = false;
            }

            SetPlayerGroup(player, Panel, VoteIds.Views, browse ? 2 : _revealing ? 1 : 0);
            CustomHud.SetHasClass(player, Panel, "is-minimized", Minimized[slot]);
            CustomHud.SetText(player, Panel, VoteIds.HideVar, HideText(player));
            CustomHud.SetHasClass(player, VoteIds.Timer, "is-urgent", IsUrgent());
            PaintSelf(player);
            PaintOwnLanguage(player, browse);

            if (_currentMap is not null)
            {
                CustomHud.SetHasClass(player, VoteIds.CurrentFav, "is-active", favorites.Contains(_currentMap));
                PaintOwnRating(player);
            }

            for (int chip = 0; chip <= _typeChips && chip < FilterTypeSlots; chip++)
            {
                CustomHud.SetHasClass(player, VoteIds.TypeChipIds[chip], "is-active", TypeSelection[slot] == chip);
            }

            CustomHud.SetHasClass(player, VoteIds.FavOnly, "is-active", FavoritesOnly[slot]);
            CustomHud.SetHasClass(player, VoteIds.FavOnly, "is-disabled", favorites.Count == 0);

            int shown = 0;
            for (int i = 0; i < _grid.Count; i++)
            {
                if (IsShown(slot, i, favorites))
                {
                    shown++;
                }
            }

            SetPlayerClass(player, VoteIds.Empty, "is-off", shown > 0, xmlOn: true);
            CustomHud.SetText(player, Panel, VoteIds.EmptyVar, L(player, "hud.empty"));

            for (int i = 0; i < SlotCount; i++)
            {
                if (CustomHud.TickWrites >= PaintWriteBudget)
                {
                    return;
                }

                string slotId = VoteIds.GridSlotIds[i];
                if (i >= _grid.Count)
                {
                    SetPlayerClass(player, slotId, "is-off", true, xmlOn: true);
                    continue;
                }

                SetPlayerClass(player, slotId, "is-off", !IsShown(slot, i, favorites), xmlOn: true);
                CustomHud.SetHasClass(player, slotId, "is-self", i == SelfSlot[slot]);
                CustomHud.SetHasClass(player, VoteIds.GridFavIds[i], "is-active", favorites.Contains(_grid[i]));
            }
        }

        /// <summary>
        /// G texts are written in the server language; a viewer whose language differs gets a
        /// per-player override, so same-language viewers cost no extra writes.
        /// </summary>
        private static void PaintOwnLanguage(CCSPlayerController player, bool browse)
        {
            void Own(string variable, string own, string server)
            {
                if (own != server)
                {
                    CustomHud.SetText(player, Panel, variable, own);
                }
            }

            void Key(string variable, string key) => Own(variable, L(player, key), L(null, key));

            Key(VoteIds.TitleVar, browse ? "hud.maps_title" : "hud.title");
            Key(VoteIds.FavOnlyVar, "hud.fav_only");
            for (int chip = 0; chip <= _typeChips && chip < FilterTypeSlots; chip++)
            {
                Own(VoteIds.TypeVars[chip], ChipText(player, chip), ChipText(null, chip));
            }

            if (_currentMap is not null)
            {
                Key(VoteIds.CurrentTagVar, "hud.current");
                Key(VoteIds.RateQuestionVar, "hud.rate_q");
                Key(VoteIds.LikeVar, "hud.like");
                Key(VoteIds.DislikeVar, "hud.dislike");
            }

            if (browse)
            {
                for (int i = 0, chip = 0; i < _filters.Sorts.Count && chip < FilterSortSlots; i++)
                {
                    if (!_filters.Sorts[i].Only)
                    {
                        Key(VoteIds.SortVars[chip++], $"sort.{_filters.Sorts[i].Id}");
                    }
                }

                return;
            }

            Key(VoteIds.RankTitleVar, "hud.rank_title");
            Key(VoteIds.RankEmptyVar, "hud.rank_empty");
            Key(VoteIds.SelfVar, "hud.self");
            if (_winner is not null)
            {
                Key(VoteIds.WinTagVar, "hud.next");
                Own(VoteIds.WinVotesVar, WinVotesText(player), WinVotesText(null));
            }
        }

        private static string ChipText(CCSPlayerController? player, int chip)
        {
            string label = T(player, chip == 0 ? "filter.all" : $"filter.{_filters.Types[chip - 1].Id}");
            return MapNameDisplay.ForHud($"{label} {TypeCounts[chip]}");
        }

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
        private static void PaintSelf(CCSPlayerController player)
        {
            int slot = player.Slot;
            string? mine = _session is not null && player.AuthorizedSteamID is { } steam
                ? _session.VoteOf(steam.SteamId64)
                : null;

            string status = _mode == Mode.Browse ? L(player, "hud.status_browse")
                : _revealing ? L(player, "hud.status_closed")
                : mine is null ? L(player, "hud.status_hint")
                : MapNameDisplay.ForHud(string.Format(T(player, "hud.status_voted"), _formatMapName(mine)));
            CustomHud.SetHasClass(player, VoteIds.Status, "is-self", mine is not null);
            CustomHud.SetText(player, Panel, VoteIds.StatusVar, status);

            for (int i = 0; i < RankSlots; i++)
            {
                CustomHud.SetHasClass(player, VoteIds.RankSelfIds[i], "is-self", mine is not null && i < _ranked.Count && _ranked[i] == mine);
            }

            int next = mine is not null && SlotOf.TryGetValue(mine, out int index) ? index : -1;
            if (SelfSlot[slot] >= 0 && SelfSlot[slot] != next)
            {
                CustomHud.SetHasClass(player, VoteIds.GridSlotIds[SelfSlot[slot]], "is-self", false);
            }

            if (next >= 0)
            {
                CustomHud.SetHasClass(player, VoteIds.GridSlotIds[next], "is-self", true);
            }

            SelfSlot[slot] = next;
        }

        private static void PaintOwnRating(CCSPlayerController player)
        {
            string? steamId = player.AuthorizedSteamID?.SteamId64.ToString();
            PlayerRating rating = _currentMap is null ? PlayerRating.None : _archive?.RatingOf(steamId, _currentMap) ?? PlayerRating.None;
            CustomHud.SetHasClass(player, VoteIds.Rate, "is-disabled", steamId is null);
            CustomHud.SetHasClass(player, VoteIds.Like, "is-active", rating == PlayerRating.Like);
            CustomHud.SetHasClass(player, VoteIds.Dislike, "is-active", rating == PlayerRating.Dislike);
        }

        private static bool IsShown(int slot, int index, HashSet<string> favorites) =>
            VoteView.IsVisible(_typeMask[index], TypeSelection[slot], FavoritesOnly[slot], favorites.Contains(_grid[index]));

        private static string HideText(CCSPlayerController player) =>
            L(player, _mode == Mode.Browse ? "hud.close" : Minimized[player.Slot] ? "hud.show" : "hud.hide");

        // ---------------------------------------------------------------- countdown

        private static bool IsUrgent() => _deadlineUtc is not null && _lastSecond is >= 0 and <= UrgentSeconds;

        /// <summary>Text globally once per second; is-urgent per viewer when it flips.</summary>
        private static void TickCountdown()
        {
            if (_deadlineUtc is not { } deadline || _globalDirty || CustomHud.TickWrites >= PaintWriteBudget)
            {
                return;
            }

            int seconds = VoteView.SecondsLeft((deadline - DateTime.UtcNow).TotalSeconds);
            if (seconds == _lastSecond)
            {
                return;
            }

            _lastSecond = seconds;
            CustomHud.SetTextGlobal(Panel, VoteIds.TimeVar, VoteView.FormatTime(seconds));
            bool urgent = IsUrgent();
            ForEachOpen(player => CustomHud.SetHasClass(player, VoteIds.Timer, "is-urgent", urgent));
        }

        /// <summary>Reset the bar (remove both classes), then span-N and is-running in later ticks.</summary>
        private static void StartTimer(CCSPlayerController player)
        {
            if (_deadlineUtc is null || player.Slot is < 0 or >= MaxSlots)
            {
                return;
            }

            int before = CustomHud.TickWrites;
            CustomHud.SetHasClass(player, VoteIds.TimerFill, "is-running", false);
            for (int s = VoteIds.SpanMin; s <= VoteIds.SpanMax; s++)
            {
                CustomHud.SetHasClass(player, VoteIds.TimerFill, VoteIds.SpanClasses[s], false);
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

                CustomHud.SetHasClass(player, VoteIds.TimerFill, VoteIds.SpanClasses[VoteView.SpanFor(remaining)], true);
                TimerStage[slot] = 2;
                TimerTick[slot] = now;
            }
            else if (TimerStage[slot] == 2)
            {
                CustomHud.SetHasClass(player, VoteIds.TimerFill, "is-running", true);
                TimerStage[slot] = 0;
            }
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Class write that knows the XML default: an unwritten class already shows it.</summary>
        private static void SetGlobalClass(string panelId, string className, bool on, bool xmlOn)
        {
            if (on == xmlOn && !CustomHud.TryGetGlobalClass(panelId, className, out _))
            {
                return;
            }

            CustomHud.SetHasClassGlobal(panelId, className, on);
        }

        private static void SetPlayerClass(CCSPlayerController player, string panelId, string className, bool on, bool xmlOn)
        {
            if (on == xmlOn && !CustomHud.TryGetPlayerClass(player, panelId, className, out _))
            {
                return;
            }

            CustomHud.SetHasClass(player, panelId, className, on);
        }

        /// <summary>Exclusive class group; index 0 is the one the XML authors.</summary>
        private static void SetGlobalGroup(string panelId, string[] classes, int active)
        {
            for (int k = 0; k < classes.Length; k++)
            {
                SetGlobalClass(panelId, classes[k], k == active, xmlOn: k == 0);
            }
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
            SelfSlot[slot] = -1;
            TimerStage[slot] = 0;
            TimerTick[slot] = -1;
        }

        private static void ResetAllSlots()
        {
            for (int slot = 0; slot < MaxSlots; slot++)
            {
                ResetSlotState(slot);
            }

            Array.Fill(Pending, false);
            PendingOpens.Clear();
            _sortSelection = -1;
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
