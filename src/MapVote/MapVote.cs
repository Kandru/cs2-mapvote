using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Extensions;
using MapVote.Config;
using MapVote.Huds;
using MapVote.Maps;
using MapVote.Players;
using MapVote.Runtime;
using MapVote.Utils;
using MapVote.Voting;

namespace MapVote
{
    [MinimumApiVersion(379)]
    public partial class MapVote : BasePlugin, IPluginConfig<PluginConfig>
    {
        private const float HudOpenDelaySeconds = GameConstants.HudOpenDelaySeconds;
        private const float JoinerOpenDelaySeconds = GameConstants.JoinerOpenDelaySeconds;

        public override string ModuleName => "MapVote";
        public override string ModuleAuthor => "Kandru";
        public override string ModuleDescription => "Fullscreen Panorama end-match map vote";

        public PluginConfig Config { get; set; } = new();

        private MapCatalog _catalog = null!;
        private PlayerArchive _archive = null!;
        private MapPlayCounts _plays = null!;
        private VoteSession? _session;
        private VoteTiming _timing;
        // Flow timers are cancelled by bumping this token, never by Kill() (ruleset §1.3).
        private int _flowEpoch;
        // Deferred HUD opens are superseded per slot; never touch a dictionary indexer directly.
        private readonly SlotActionTokens _hudOpenTokens = new();
        private DateTime _scheduleEpochUtc;
        private DateTime _intermissionEpochUtc;
        private int _voteSeconds = VoteClock.DefaultVoteSeconds;
        private int? _baseVoteSeconds;
        private int _catalogGeneration;
        private List<MapEntry>? _pendingCandidates;
        private string? _pendingWinner;
        private MapOrigin _pendingOrigin;
        private Dictionary<string, MapOrigin> _origins = new(StringComparer.Ordinal);
        private readonly HashSet<ulong> _rtvVoters = [];
        private bool _rtvPassed;
        private bool _rtvClosed;
        private bool _leaveMap;
        private bool _unloaded;

        public void OnConfigParsed(PluginConfig config)
        {
            Config = config;
            Config.Normalize();
            if (ConfigMigration.EnsureSortDefaults(Config.Filters))
            {
                PluginLog.Info("config: migrated default sorts to alphabetical / most_positive / most_negative");
            }

            MapOrder.Configure(Config.Display.Order);
            EnsureMapsVotable();
            PluginLog.Bind(() => Config.Debug);
            PluginLog.Debug(
                $"config: order={Config.Display.Order}, max_maps={Config.Endmatch.MaxMaps}, "
                + $"vote_seconds={Config.Endmatch.VoteSeconds?.ToString() ?? "server"}, "
                + $"reveal={Config.Endmatch.WinnerRevealSeconds}s, "
                + $"catalog={Config.Catalog.QueryLocalMaps}/{Config.Catalog.QueryMapGroup}, "
                + $"rtv={Config.Rtv.Enabled} ({Config.Rtv.Ratio:0.##}, {Config.Rtv.DurationSeconds}s), "
                + $"nominations={Config.Nominations.Enabled}, votable_keys={Config.MapsVotable.Count}");
        }

        public override void Load(bool hotReload)
        {
            PluginLog.Bind(() => Config.Debug);
            PluginLog.Info($"plugin loading{(hotReload ? " (hot reload)" : string.Empty)}");
            PluginLog.Debug($"debug logging enabled; hotReload={hotReload}");
            _unloaded = false;
            InitializeRuntimeState("load");

            _catalog = new MapCatalog(PluginLog.Info, PluginLog.Debug, OnCatalogLoaded);
            _archive = new PlayerArchive();
            _plays = new MapPlayCounts();
            LoadPersistence();

            RegisterListener<Listeners.OnMapStart>(OnMapStart);
            RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
            RegisterListener<Listeners.CheckTransmit>(CustomHud.OnCheckTransmit);
            RegisterListener<Listeners.OnTick>(OnTick);
            RegisterListener<Listeners.OnCustomHudClicked>(OnCustomHudClicked);

            RegisterEventHandler<EventCsWinPanelMatch>(OnWinPanelMatch);
            RegisterEventHandler<EventCsIntermission>(OnCsIntermission);
            RegisterEventHandler<EventRoundAnnounceWarmup>(OnRoundAnnounceWarmup);
            RegisterEventHandler<EventRoundAnnounceMatchStart>(OnRoundAnnounceMatchStart);
            RegisterEventHandler<EventBeginNewMatch>(OnBeginNewMatch);
            RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
            RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);

            MapOrder.Configure(Config.Display?.Order);
        }

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            // Called after a reload or a late load, when the server is already live.
            // A normal start runs before the engine globals exist; OnMapStart attaches then.
            if (!hotReload)
            {
                return;
            }

            CustomHud.ResetRound();
            ArmEngineHold();
        }

        public override void Unload(bool hotReload)
        {
            PluginLog.Info($"plugin unloading{(hotReload ? " (hot reload)" : string.Empty)}");
            _unloaded = true;
            try
            {
                SavePersistence();
            }
            finally
            {
                try
                {
                    ShutdownRuntimeState("unload");
                }
                finally
                {
                    RemoveListener<Listeners.OnMapStart>(OnMapStart);
                    RemoveListener<Listeners.OnMapEnd>(OnMapEnd);
                    RemoveListener<Listeners.CheckTransmit>(CustomHud.OnCheckTransmit);
                    RemoveListener<Listeners.OnTick>(OnTick);
                    RemoveListener<Listeners.OnCustomHudClicked>(OnCustomHudClicked);
                    DeregisterEventHandler<EventCsWinPanelMatch>(OnWinPanelMatch);
                    DeregisterEventHandler<EventCsIntermission>(OnCsIntermission);
                    DeregisterEventHandler<EventRoundAnnounceWarmup>(OnRoundAnnounceWarmup);
                    DeregisterEventHandler<EventRoundAnnounceMatchStart>(OnRoundAnnounceMatchStart);
                    DeregisterEventHandler<EventBeginNewMatch>(OnBeginNewMatch);
                    DeregisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
                    DeregisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
                }
            }
        }

        private void OnCatalogLoaded(IReadOnlyList<MapEntry> maps, int generation) =>
            // RCON worker thread → main thread for config I/O and Server APIs.
            Server.NextFrame(() =>
            {
                if (_unloaded)
                {
                    return;
                }

                if (generation == _catalogGeneration && _flow.Phase == EndMatchPhase.Preparing)
                {
                    OnCatalogSettled(maps, generation);
                }

                OnMapsCatalogSettled(maps, generation);
            });

        private void SyncMapsVotable(IReadOnlyList<MapEntry> maps)
        {
            if (_unloaded)
            {
                return;
            }

            try
            {
                Config.Reload();
            }
            catch (Exception ex)
            {
                PluginLog.Info($"config: reload before maps_votable sync failed: {ex.Message}");
            }

            EnsureMapsVotable();
            Config.Catalog ??= new CatalogConfig();
            if (!MapVisibility.TrySync(Config.MapsVotable, maps))
            {
                return;
            }

            try
            {
                Config.Update();
                PluginLog.Debug($"maps_votable: synced {Config.MapsVotable.Count} key(s)");
            }
            catch (Exception ex)
            {
                PluginLog.Info($"maps_votable: config update failed: {ex.Message}");
            }
        }

        private void EnsureMapsVotable() =>
            Config.MapsVotable ??= new Dictionary<string, bool>(StringComparer.Ordinal);

        private void OnTick()
        {
            CustomHud.OnTick();
            // Vote mode is bound only during Voting/Revealing; browse mode can be open any time.
            VoteHud.OnTick();
            TickRtv();
        }

        private void LoadPersistence()
        {
            _archive.Load(
                Config,
                warn: msg => PluginLog.Info($"players: {msg}"),
                debug: msg => PluginLog.Debug($"players: {msg}"));
            _plays.Load(Config, warn: msg => PluginLog.Info($"plays: {msg}"));
        }

        private void SavePersistence()
        {
            int written = _archive.SaveDirty(
                Config,
                warn: msg => PluginLog.Info($"players: {msg}"),
                debug: msg => PluginLog.Debug($"players: {msg}"));
            if (written > 0)
            {
                PluginLog.Debug($"players: saved {written} dirty player(s)");
            }

            _plays.Save(Config, warn: msg => PluginLog.Info($"plays: {msg}"));
        }

        private void OnMapStart(string mapName)
        {
            PluginLog.Info($"map start: {mapName}");
            InitializeRuntimeState("map start");
            CustomHud.ResetRound();
            LoadPersistence();
            ArmEngineHold();
            PluginLog.Debug("map start: runtime initialized, slots reconciled, persistence loaded");
        }

        private void OnMapEnd()
        {
            PluginLog.Info("map end: saving player data");
            try
            {
                SavePersistence();
            }
            finally
            {
                ShutdownRuntimeState("map end");
            }
        }

        /// <summary>
        /// Resets every per-map runtime value to its startup default. Used on plugin load
        /// (including hot reload) and on map start, so neither path depends on the other.
        /// The cached original server vote length is intentionally retained across maps.
        /// </summary>
        private void InitializeRuntimeState(string reason)
        {
            _catalog?.Invalidate();
            _leaveMap = false;
            _mapsClosed = false;
            _session = null;
            _pendingCandidates = null;
            _pendingWinner = null;
            _pendingOrigin = MapOrigin.Local;
            _origins = new Dictionary<string, MapOrigin>(StringComparer.Ordinal);
            _catalogGeneration = 0;
            _scheduleEpochUtc = default;
            _intermissionEpochUtc = default;
            _voteSeconds = VoteClock.DefaultVoteSeconds;
            _timing = default;
            _flowEpoch++;
            _flow.Reset();
            StopRtvWindow();
            _rtvVoters.Clear();
            _rtvPassed = false;
            _rtvClosed = false;
            _hudOpenTokens.ClearAll();
            _mapsPendingOpen.Clear();
            _nominationSession = null;
            VoteHud.ResetView();
            CustomHud.Initialize();
            MapOrder.ResetMatch();
            PluginLog.Debug($"runtime initialized ({reason})");
        }

        /// <summary>
        /// De-initializes everything that can outlive a map: timers, votes, RTV, browser
        /// state, pending HUD opens and layout entities. Used on map end and on plugin
        /// unload; both leave the plugin in a closed, inert state.
        /// </summary>
        private void ShutdownRuntimeState(string reason)
        {
            _leaveMap = true;
            _mapsClosed = true;
            _rtvClosed = true;
            _session = null;
            _pendingCandidates = null;
            _pendingWinner = null;
            _pendingOrigin = MapOrigin.Local;
            _origins = new Dictionary<string, MapOrigin>(StringComparer.Ordinal);
            _catalogGeneration = 0;
            _scheduleEpochUtc = default;
            _intermissionEpochUtc = default;
            _voteSeconds = VoteClock.DefaultVoteSeconds;
            _timing = default;
            _flowEpoch++;
            _flow.Reset();
            StopRtvWindow();
            _rtvVoters.Clear();
            _rtvPassed = false;
            _hudOpenTokens.ClearAll();
            _mapsPendingOpen.Clear();
            _nominationSession = null;
            try
            {
                _catalog?.Invalidate();
            }
            finally
            {
                VoteHud.ResetView();
                CustomHud.Shutdown();
                PluginLog.Debug($"runtime shut down ({reason})");
            }
        }

        private HookResult OnWinPanelMatch(EventCsWinPanelMatch @event, GameEventInfo info)
        {
            PluginLog.Debug("end-match: win panel event");
            BeginEndMatchFromWinPanel();
            if (_flow.IsCustomVote)
            {
                SuppressNativeVoteSlots();
            }

            return HookResult.Continue;
        }

        private HookResult OnCsIntermission(EventCsIntermission @event, GameEventInfo info)
        {
            PluginLog.Info("intermission: preparing end-match map vote");
            CloseRtvForMatchEnd();
            CloseMapsForMatchEnd();
            BeginFromIntermission();
            return HookResult.Continue;
        }

        private HookResult OnRoundAnnounceWarmup(EventRoundAnnounceWarmup @event, GameEventInfo info)
        {
            OpenRtvForNewMatch("warmup");
            OpenMapsForNewMatch("warmup");
            return HookResult.Continue;
        }

        private HookResult OnRoundAnnounceMatchStart(EventRoundAnnounceMatchStart @event, GameEventInfo info)
        {
            OpenRtvForNewMatch("match start");
            OpenMapsForNewMatch("match start");
            return HookResult.Continue;
        }

        private HookResult OnBeginNewMatch(EventBeginNewMatch @event, GameEventInfo info)
        {
            MapOrder.ResetMatch();
            OpenRtvForNewMatch("new match");
            OpenMapsForNewMatch("new match");
            return HookResult.Continue;
        }

        private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
        {
            CCSPlayerController? player = @event.Userid;
            if (player is not { IsValid: true })
            {
                return HookResult.Continue;
            }

            if (Humans.TryGetSteamId(player, out ulong steamId))
            {
                _archive.GetOrCreate(steamId, player.PlayerName);
                PluginLog.Debug($"connect: {Humans.Label(player, steamId)} slot={player.Slot}");

                if (_flow.Phase is EndMatchPhase.Voting or EndMatchPhase.Revealing
                    && PlayerSlots.IsValid(player.Slot))
                {
                    ScheduleVoteHudForJoiner(player.Slot, steamId);
                }
            }

            CustomHud.QueueSpawn(player);
            return HookResult.Continue;
        }

        private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
        {
            CCSPlayerController? player = @event.Userid;
            if (player is null)
            {
                return HookResult.Continue;
            }

            int slot = player.Slot;
            ulong steamId = Humans.TryGetSteamId(player, out ulong current)
                ? current
                : player.AuthorizedSteamID?.SteamId64 ?? 0;
            if (steamId == 0
                && PlayerSlots.IsValid(slot)
                && CustomHud.TryGetOwner(slot, out ulong layoutOwner))
            {
                // The controller is gone; the private layout still names its SteamID.
                steamId = layoutOwner;
            }

            // A slot may already belong to the next player. Find the live layout by
            // SteamID first and only release a slot whose owner still matches.
            if (!PlayerSlots.IsValid(slot) && steamId > 0)
            {
                slot = CustomHud.FindSlotBySteamId(steamId);
            }

            bool slotReused = PlayerSlots.IsValid(slot)
                && steamId > 0
                && Utilities.GetPlayerFromSlot(slot) is { IsValid: true } occupant
                && Humans.TryGetSteamId(occupant, out ulong occupantSteamId)
                && occupantSteamId != steamId;
            if (PlayerSlots.IsValid(slot) && !slotReused)
            {
                bool ownsSlot = steamId == 0
                    || (CustomHud.TryGetOwner(slot, out ulong owner) && owner == steamId)
                    || (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } currentOccupant
                        && Humans.TryGetSteamId(currentOccupant, out ulong currentId)
                        && currentId == steamId);
                if (!ownsSlot && steamId > 0)
                {
                    slot = CustomHud.FindSlotBySteamId(steamId);
                    ownsSlot = PlayerSlots.IsValid(slot);
                }

                if (ownsSlot)
                {
                    _hudOpenTokens.Clear(slot);
                    // Kill the layout/session without writing to a controller that may be gone.
                    CustomHud.ReleaseSlot(slot);
                    VoteHud.ReleaseSlot(slot);
                    if (!HudMenu.HasOpenSessions)
                    {
                        VoteHud.CloseBrowse();
                    }
                }
                else
                {
                    PluginLog.Debug(
                        $"disconnect: no owned HUD slot for {steamId}; no HUD release");
                }
            }
            else if (slotReused)
            {
                PluginLog.Debug(
                    $"disconnect: slot {slot} now belongs to another player; leaving its HUD untouched");
            }

            if (steamId > 0)
            {
                _mapsPendingOpen.Remove(steamId);
                _archive.SaveIfDirty(
                    Config,
                    steamId.ToString(),
                    warn: msg => PluginLog.Info($"players: {msg}"),
                    debug: msg => PluginLog.Debug($"players: {msg}"));

                _rtvVoters.Remove(steamId);
                if (_rtvVoters.Count == 0)
                {
                    StopRtvWindow();
                }

                bool votesChanged = _session is { IsOpen: true } && _session.RemovePlayer(steamId);
                if (_nominationSession is { } nominations && nominations.RemovePlayer(steamId))
                {
                    votesChanged = true;
                }

                if (votesChanged)
                {
                    VoteHud.PaintAll();
                }

                string label = Humans.Label(player, steamId);
                PluginLog.Debug(
                    $"disconnect: {label} slot={slot} votes_changed={votesChanged} rtv_votes={_rtvVoters.Count}");
                TrySucceedRtv(
                    CountRtvHumans(steamId),
                    $"after disconnect of {label}",
                    deferCommand: true);
            }
            else
            {
                PluginLog.Debug($"disconnect: unknown identity slot={slot}; only slot state released");
            }

            return HookResult.Continue;
        }

        private void OnCustomHudClicked(CCSPlayerController player, CCSCustomHudLayout layout, string buttonId) =>
            HudMenu.OnCustomHudClicked(player, layout, buttonId);
    }
}
