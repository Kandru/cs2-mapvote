using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;
using MapVote.Config;
using MapVote.Huds;
using MapVote.Maps;
using MapVote.Players;
using MapVote.Utils;
using MapVote.Voting;
using CssTimer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace MapVote
{
    public partial class MapVote : BasePlugin, IPluginConfig<PluginConfig>
    {
        public override string ModuleName => "MapVote";
        public override string ModuleAuthor => "Kandru";
        public override string ModuleDescription => "Fullscreen Panorama end-match map vote";

        public PluginConfig Config { get; set; } = new();

        private MapCatalog _catalog = null!;
        private PlayerArchive _archive = null!;
        private MapPlayCounts _plays = null!;
        private VoteSession? _session;
        private VoteTiming _timing;
        private CssTimer? _openTimer;
        private CssTimer? _revealTimer;
        private CssTimer? _catalogWaitTimer;
        private CssTimer? _rtvExpireTimer;
        private CssTimer? _rtvAnnounceTimer;
        private DateTime _scheduleEpochUtc;
        private DateTime? _rtvDeadlineUtc;
        private int _panelSeconds = VoteClock.DefaultPanelSeconds;
        private int _catalogGeneration;
        private bool _catalogPreparing;
        private bool _voteActive;
        private bool _voteHudOpen;
        private bool _revealActive;
        private string? _pendingWinner;
        private MapOrigin _pendingOrigin;
        private Dictionary<string, MapOrigin> _origins = new(StringComparer.Ordinal);
        private readonly HashSet<ulong> _rtvVoters = [];
        private bool _rtvPassed;
        private bool _rtvClosed;
        private bool _nativeVoteArmed;
        private bool _unloaded;
        private readonly Dictionary<int, ulong> _steamBySlot = [];

        public void OnConfigParsed(PluginConfig config)
        {
            Config = config;
            EnsureMapsVotable();
            PluginLog.Bind(() => Config.Debug);
        }

        public override void Load(bool hotReload)
        {
            PluginLog.Bind(() => Config.Debug);
            PluginLog.Debug($"load hotReload={hotReload}");
            _unloaded = false;

            _catalog = new MapCatalog(PluginLog.CatalogSink(), OnCatalogLoaded);
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
            RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
            RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);

            if (hotReload)
            {
                ArmEngineVote();
            }
        }

        public override void Unload(bool hotReload)
        {
            PluginLog.Debug($"unload (hotReload={hotReload})");
            _unloaded = true;
            StopRtvWindow();
            _catalog?.Invalidate();
            EndVoteUi();
            SavePersistence();
            CustomHud.Shutdown();
            RemoveListener<Listeners.OnMapStart>(OnMapStart);
            RemoveListener<Listeners.OnMapEnd>(OnMapEnd);
            RemoveListener<Listeners.CheckTransmit>(CustomHud.OnCheckTransmit);
            RemoveListener<Listeners.OnTick>(OnTick);
            RemoveListener<Listeners.OnCustomHudClicked>(OnCustomHudClicked);
        }

        private void OnCatalogLoaded(IReadOnlyList<MapEntry> maps, int generation) =>
            // RCON worker thread → main thread for config I/O and Server APIs.
            Server.NextFrame(() =>
            {
                if (!_unloaded && _catalogPreparing && generation == _catalogGeneration)
                {
                    OnCatalogSettled(maps, generation);
                }
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
                PluginLog.Info($"config reload before maps_votable sync failed: {ex.Message}");
            }

            EnsureMapsVotable();
            if (!MapVisibility.TrySync(Config.MapsVotable, maps))
            {
                return;
            }

            try
            {
                Config.Update();
                PluginLog.Debug($"maps_votable synced ({Config.MapsVotable.Count} keys)");
            }
            catch (Exception ex)
            {
                PluginLog.Info($"maps_votable config update failed: {ex.Message}");
            }
        }

        private void EnsureMapsVotable() =>
            Config.MapsVotable ??= new Dictionary<string, bool>(StringComparer.Ordinal);

        private void OnTick()
        {
            if (!_voteActive && !_revealActive)
            {
                return;
            }

            VoteHud.OnTick();
        }

        private void LoadPersistence()
        {
            _archive.Load(
                Config,
                warn: msg => PluginLog.Info($"[players] {msg}"),
                debug: msg => PluginLog.Debug($"[players] {msg}"));
            _plays.Load(Config, warn: msg => PluginLog.Info($"[plays] {msg}"));
        }

        private void SavePersistence()
        {
            int written = _archive.SaveDirty(
                Config,
                warn: msg => PluginLog.Info($"[players] {msg}"),
                debug: msg => PluginLog.Debug($"[players] {msg}"));
            if (written > 0)
            {
                PluginLog.Debug($"[players] saved {written} dirty player(s)");
            }

            _plays.Save(Config, warn: msg => PluginLog.Info($"[plays] {msg}"));
        }

        private void OnMapStart(string mapName)
        {
            _catalog.Invalidate();
            StopRtvWindow();
            _rtvVoters.Clear();
            _rtvPassed = false;
            _rtvClosed = false;
            _nativeVoteArmed = false;
            _steamBySlot.Clear();
            EndVoteUi();
            CustomHud.ResetRound();
            LoadPersistence();
            ArmEngineVote();
            PluginLog.Debug($"map start {mapName}");
        }

        private void OnMapEnd()
        {
            _catalog.Invalidate();
            EndVoteUi();
            SavePersistence();
            CustomHud.Shutdown();
        }

        private HookResult OnWinPanelMatch(EventCsWinPanelMatch @event, GameEventInfo info)
        {
            PluginLog.Debug("WinPanel: preparing end-match vote");
            BeginEndMatchFromWinPanel();
            return HookResult.Continue;
        }

        private HookResult OnCsIntermission(EventCsIntermission @event, GameEventInfo info)
        {
            CloseRtvForMatchEnd();
            return HookResult.Continue;
        }

        private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
        {
            CCSPlayerController? player = @event.Userid;
            if (player is not { IsValid: true })
            {
                return HookResult.Continue;
            }

            if (player.AuthorizedSteamID is { SteamId64: > 0 } steam)
            {
                if (player.Slot is >= 0)
                {
                    _steamBySlot[player.Slot] = steam.SteamId64;
                }

                PlayerState state = _archive.GetOrCreate(steam.SteamId64.ToString());
                if (string.IsNullOrEmpty(state.Username) || state.Username == state.SteamId)
                {
                    state.Username = player.PlayerName ?? state.SteamId;
                }

                PluginLog.Debug($"connect: {Humans.Label(player, steam.SteamId64)} slot={player.Slot}");
            }

            if (_voteHudOpen || _revealActive)
            {
                OpenVoteHudForJoiner(player);
            }

            return HookResult.Continue;
        }

        private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
        {
            CCSPlayerController? player = @event.Userid;
            if (player is null)
            {
                return HookResult.Continue;
            }

            ulong? steamId = player.AuthorizedSteamID?.SteamId64;
            if (steamId is null or 0
                && player.Slot is >= 0
                && _steamBySlot.TryGetValue(player.Slot, out ulong cached))
            {
                steamId = cached;
            }

            if (player.Slot is >= 0)
            {
                _steamBySlot.Remove(player.Slot);
                VoteHud.ReleaseSlot(player.Slot);
            }

            if (steamId is > 0)
            {
                _rtvVoters.Remove(steamId.Value);
                if (_rtvVoters.Count == 0)
                {
                    StopRtvWindow();
                }

                if (_session is { IsOpen: true } && _session.RemovePlayer(steamId.Value))
                {
                    VoteHud.PaintAll();
                }

                string label = Humans.Label(player, steamId);
                PluginLog.Debug($"disconnect: {label} slot={player.Slot}");
                TrySucceedRtv(
                    CountRtvHumans(steamId.Value),
                    $"after disconnect of {label}",
                    deferCommand: true);
            }

            CustomHud.ReleasePlayer(player);
            return HookResult.Continue;
        }

        private void OnCustomHudClicked(CCSPlayerController player, CCSCustomHudLayout layout, string buttonId) =>
            HudMenu.OnCustomHudClicked(player, layout, buttonId);
    }
}
