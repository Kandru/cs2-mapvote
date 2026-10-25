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
        private bool _voteActive;
        private bool _revealActive;
        private string? _pendingWinner;
        private MapOrigin _pendingOrigin;
        private readonly HashSet<ulong> _rtvVoters = [];

        public void OnConfigParsed(PluginConfig config) => Config = config;

        public override void Load(bool hotReload)
        {
            _catalog = new MapCatalog(PluginLog.CatalogSink(() => Config.Debug));
            _archive = new PlayerArchive();
            _plays = new MapPlayCounts();
            _archive.EnsureLoaded(
                Config,
                warn: msg => PluginLog.Info($"[players] {msg}"),
                debug: Config.Debug ? msg => PluginLog.Info($"[players] {msg}") : null);
            _plays.Load(Config, warn: msg => PluginLog.Info($"[plays] {msg}"));
            _saves = new PlayerSaveQueue(
                _archive,
                Config,
                warn: msg => PluginLog.Info($"[players] {msg}"),
                debug: msg => PluginLog.Info($"[players] {msg}"));

            RegisterListener<Listeners.OnMapStart>(OnMapStart);
            RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
            RegisterListener<Listeners.CheckTransmit>(CustomHud.OnCheckTransmit);
            RegisterListener<Listeners.OnTick>(OnTick);
            RegisterListener<Listeners.OnCustomHudClicked>(OnCustomHudClicked);

            RegisterEventHandler<EventCsIntermission>(OnIntermission);
            RegisterEventHandler<EventRoundAnnounceMatchPoint>(OnMatchPoint);
            RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
            RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
            RegisterEventHandler<EventRoundEnd>(OnRoundEnd);

            if (hotReload)
            {
                _catalog.RequestRefresh(force: true);
            }
        }

        public override void Unload(bool hotReload)
        {
            EndVoteUi();
            _saves.FlushSync();
            CustomHud.Shutdown();
            RemoveListener<Listeners.OnMapStart>(OnMapStart);
            RemoveListener<Listeners.OnMapEnd>(OnMapEnd);
            RemoveListener<Listeners.CheckTransmit>(CustomHud.OnCheckTransmit);
            RemoveListener<Listeners.OnTick>(OnTick);
            RemoveListener<Listeners.OnCustomHudClicked>(OnCustomHudClicked);
        }

        private void OnTick()
        {
            _saves.Pump();
            CustomHud.OnTick();
        }
    }
}
