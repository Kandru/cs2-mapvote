using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using MapVote.Huds;
using MapVote.Maps;
using MapVote.Utils;

namespace MapVote
{
    public partial class MapVote
    {
        /// <summary>RCON retry budget when !maps is the first catalog consumer after a load.</summary>
        private const int BrowseCatalogBudgetSeconds = 3;

        private bool _mapsClosed;
        private readonly HashSet<ulong> _mapsPendingOpen = [];

        [ConsoleCommand("css_maps", "Browse the server map list.")]
        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
        public void OnMapsCommand(CCSPlayerController? player, CommandInfo info)
        {
            if (player is null || !Humans.IsHumanViewer(player)
                || player.AuthorizedSteamID is not { SteamId64: var id })
            {
                PluginLog.Debug("maps ignored: invalid player");
                return;
            }

            string label = Humans.Label(player, id);

            if (_mapsClosed)
            {
                PluginLog.Info($"maps failed: {label} - closed until next match");
                info.ReplyToCommand(Chat(Localizer["maps.closed"]));
                return;
            }

            if (VoteBusy)
            {
                PluginLog.Info($"maps failed: {label} - vote already running");
                info.ReplyToCommand(Chat(Localizer["maps.busy"]));
                return;
            }

            if (_catalog.Maps.Count == 0)
            {
                if (_mapsPendingOpen.Add(id))
                {
                    PluginLog.Info($"maps pending: {label} - loading catalog");
                    info.ReplyToCommand(Chat(Localizer["maps.loading"]));
                }

                _catalog.RequestRefresh(BrowseCatalogBudgetSeconds);
                return;
            }

            OpenMapBrowserFor(player);
            PluginLog.Info($"maps opened: {label}");
        }

        /// <summary>
        /// Opens the shared overlay in browse mode for one human. The map list is bound
        /// once per browse session so one player's filters do not reset another's.
        /// </summary>
        private void OpenMapBrowserFor(CCSPlayerController player)
        {
            if (_unloaded || _mapsClosed || !Humans.IsHumanViewer(player))
            {
                return;
            }

            if (!VoteHud.IsBrowsing)
            {
                List<MapEntry> maps = SelectMaps(excludeCurrent: false);
                Dictionary<string, MapOrigin> origins = new(maps.Count, StringComparer.Ordinal);
                List<string> names = new(maps.Count);
                foreach (MapEntry map in maps)
                {
                    origins[map.Name] = map.Origin;
                    names.Add(map.Name);
                }

                VoteHud.BindBrowse(
                    Config.Filters,
                    _archive,
                    _plays,
                    (viewer, key) => Localization.ForPlayerSafe(Localizer, viewer, key),
                    origins,
                    names);
            }

            VoteHud.OpenFor(player);
        }

        /// <summary>Very first !maps after a load: opens everyone who was waiting once RCON answers.</summary>
        private void OnMapsCatalogSettled(IReadOnlyList<MapEntry> maps, int generation)
        {
            if (_mapsPendingOpen.Count == 0)
            {
                return;
            }

            if (_unloaded || _mapsClosed || generation != _catalog.Generation)
            {
                _mapsPendingOpen.Clear();
                return;
            }

            List<CCSPlayerController> waiting = Humans.GetHumans()
                .Where(player => player.AuthorizedSteamID is { SteamId64: var id }
                    && _mapsPendingOpen.Contains(id))
                .ToList();
            _mapsPendingOpen.Clear();

            if (maps.Count == 0)
            {
                foreach (CCSPlayerController player in waiting)
                {
                    player.PrintToChat(Chat(Localizer["maps.unavailable"]));
                }

                return;
            }

            foreach (CCSPlayerController player in waiting)
            {
                OpenMapBrowserFor(player);
            }
        }

        /// <summary>Map start / warmup / match start / new match: !maps is available again.</summary>
        private void OpenMapsForNewMatch(string reason, bool clearPending = false)
        {
            if (clearPending)
            {
                _mapsPendingOpen.Clear();
            }

            if (!_mapsClosed)
            {
                return;
            }

            _mapsClosed = false;
            PluginLog.Debug($"map browser open ({reason})");
        }

        /// <summary>Intermission / map end: close the browser and reject !maps until the next match.</summary>
        private void CloseMapsForMatchEnd()
        {
            _mapsPendingOpen.Clear();
            _mapsClosed = true;
            VoteHud.CloseBrowse();
            PluginLog.Debug("map browser closed until next match");
        }
    }
}
