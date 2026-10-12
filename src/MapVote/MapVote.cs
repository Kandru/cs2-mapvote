using CounterStrikeSharp.API.Core;
using MapVote.Config;
using MapVote.Maps;
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

            RegisterListener<Listeners.OnMapStart>(OnMapStart);
            RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
            RegisterListener<Listeners.CheckTransmit>(CustomHud.OnCheckTransmit);
            RegisterListener<Listeners.OnTick>(CustomHud.OnTick);
            RegisterListener<Listeners.OnCustomHudClicked>(OnCustomHudClicked);

            RegisterEventHandler<EventCsIntermission>(OnIntermission);
            RegisterEventHandler<EventRoundAnnounceMatchPoint>(OnMatchPoint);
            RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);

            if (hotReload)
            {
                _catalog.RequestRefresh(force: true);
            }
        }

        public override void Unload(bool hotReload)
        {
            EndVoteUi();
            CustomHud.Shutdown();
            RemoveListener<Listeners.OnMapStart>(OnMapStart);
            RemoveListener<Listeners.OnMapEnd>(OnMapEnd);
            RemoveListener<Listeners.CheckTransmit>(CustomHud.OnCheckTransmit);
            RemoveListener<Listeners.OnTick>(CustomHud.OnTick);
            RemoveListener<Listeners.OnCustomHudClicked>(OnCustomHudClicked);
        }
    }
}
