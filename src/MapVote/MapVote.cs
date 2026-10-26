using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;
using MapVote.Config;
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
        private CssTimer? _changelevelTimer;
        private DateTime _catalogWaitDeadlineUtc;
        private bool _voteActive;
        private bool _voteHudOpen;
        private bool _revealActive;
        private int _changelevelGeneration;
        private string? _pendingWinner;
        private MapOrigin _pendingOrigin;
        private Dictionary<string, MapOrigin> _origins = new(StringComparer.Ordinal);
        private readonly HashSet<ulong> _rtvVoters = [];
        private bool _rtvPassed;
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

            RegisterEventHandler<EventCsIntermission>(OnIntermission);
            RegisterEventHandler<EventCsWinPanelMatch>(OnWinPanelMatch);
            RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
            RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);

            _catalog.RequestRefresh();
            if (hotReload)
            {
                ArmEngineVote();
            }
        }

        public override void Unload(bool hotReload)
        {
            PluginLog.Debug($"unload (hotReload={hotReload})");
            _unloaded = true;
            EndVoteUi();
            SavePersistence();
            CustomHud.Shutdown();
            RemoveListener<Listeners.OnMapStart>(OnMapStart);
            RemoveListener<Listeners.OnMapEnd>(OnMapEnd);
            RemoveListener<Listeners.CheckTransmit>(CustomHud.OnCheckTransmit);
            RemoveListener<Listeners.OnTick>(OnTick);
            RemoveListener<Listeners.OnCustomHudClicked>(OnCustomHudClicked);
        }

        private void OnCatalogLoaded(IReadOnlyList<MapEntry> maps) =>
            // RCON worker thread → main thread for config I/O and Server APIs.
            Server.NextFrame(() => SyncMapsVotable(maps));

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

            CustomHud.OnTick();
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
    }
}
