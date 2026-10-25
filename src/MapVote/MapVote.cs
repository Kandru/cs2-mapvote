using CounterStrikeSharp.API.Core;
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
        private bool _revealActive;
        private int _changelevelGeneration;
        private string? _pendingWinner;
        private MapOrigin _pendingOrigin;
        private readonly HashSet<ulong> _rtvVoters = [];
        private bool _rtvPassed;
        private bool _nativeVoteArmed;
        private readonly Dictionary<int, ulong> _steamBySlot = [];

        public void OnConfigParsed(PluginConfig config)
        {
            Config = config;
            PluginLog.Bind(() => Config.Debug);
        }

        public override void Load(bool hotReload)
        {
            PluginLog.Bind(() => Config.Debug);
            PluginLog.Debug($"load hotReload={hotReload}");

            _catalog = new MapCatalog(PluginLog.CatalogSink());
            _archive = new PlayerArchive();
            _plays = new MapPlayCounts();
            LoadPersistence();

            RegisterListener<Listeners.OnMapStart>(OnMapStart);
            RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
            RegisterListener<Listeners.CheckTransmit>(CustomHud.OnCheckTransmit);
            RegisterListener<Listeners.OnTick>(OnTick);
            RegisterListener<Listeners.OnCustomHudClicked>(OnCustomHudClicked);

            RegisterEventHandler<EventCsIntermission>(OnIntermission);
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
            EndVoteUi();
            SavePersistence();
            CustomHud.Shutdown();
            RemoveListener<Listeners.OnMapStart>(OnMapStart);
            RemoveListener<Listeners.OnMapEnd>(OnMapEnd);
            RemoveListener<Listeners.CheckTransmit>(CustomHud.OnCheckTransmit);
            RemoveListener<Listeners.OnTick>(OnTick);
            RemoveListener<Listeners.OnCustomHudClicked>(OnCustomHudClicked);
        }

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
