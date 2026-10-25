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
        private PlayerSaveQueue _saves = null!;
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
            _archive.EnsureLoaded(
                Config,
                warn: msg => PluginLog.Info($"[players] {msg}"),
                debug: msg => PluginLog.Debug($"[players] {msg}"));
            _plays.Load(Config, warn: msg => PluginLog.Info($"[plays] {msg}"));
            _saves = new PlayerSaveQueue(
                _archive,
                Config,
                warn: msg => PluginLog.Info($"[players] {msg}"),
                debug: msg => PluginLog.Debug($"[players] {msg}"));

            RegisterHooks();

            if (hotReload)
            {
                _catalog.RequestRefresh(force: true);
            }
        }

        public override void Unload(bool hotReload)
        {
            PluginLog.Debug($"unload (hotReload={hotReload})");
            EndVoteUi();
            _saves.FlushSync();
            CustomHud.Shutdown();
            UnregisterHooks();
        }

        private void RegisterHooks()
        {
            RegisterListener<Listeners.OnMapStart>(OnMapStart);
            RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
            RegisterListener<Listeners.CheckTransmit>(CustomHud.OnCheckTransmit);
            RegisterListener<Listeners.OnTick>(OnTick);
            RegisterListener<Listeners.OnCustomHudClicked>(OnCustomHudClicked);
            RegisterEventHandler<EventCsWinPanelMatch>(OnWinPanelMatch);
            RegisterEventHandler<EventRoundAnnounceMatchPoint>(OnMatchPoint);
            RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
            RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
            RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        }

        private void UnregisterHooks()
        {
            RemoveListener<Listeners.OnMapStart>(OnMapStart);
            RemoveListener<Listeners.OnMapEnd>(OnMapEnd);
            RemoveListener<Listeners.CheckTransmit>(CustomHud.OnCheckTransmit);
            RemoveListener<Listeners.OnTick>(OnTick);
            RemoveListener<Listeners.OnCustomHudClicked>(OnCustomHudClicked);
            DeregisterEventHandler<EventCsWinPanelMatch>(OnWinPanelMatch);
            DeregisterEventHandler<EventRoundAnnounceMatchPoint>(OnMatchPoint);
            DeregisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
            DeregisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
            DeregisterEventHandler<EventRoundEnd>(OnRoundEnd);
        }

        private void OnTick()
        {
            _saves.Pump();
            CustomHud.OnTick();
        }
    }
}
