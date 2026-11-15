using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using MapVote.Huds;
using MapVote.Maps;
using MapVote.Runtime;
using MapVote.Utils;
using MapVote.Voting;

namespace MapVote
{
    public partial class MapVote
    {
        /// <summary>RCON retry budget when !maps is the first catalog consumer after a load.</summary>
        private const int BrowseCatalogBudgetSeconds = GameConstants.BrowseCatalogBudgetSeconds;

        private bool _mapsClosed;
        private readonly HashSet<ulong> _mapsPendingOpen = [];
        private VoteSession? _nominationSession;

        [ConsoleCommand("css_maps", "Browse the server map list.")]
        [ConsoleCommand("maps", "Browse the server map list.")]
        [ConsoleCommand("css_nom", "Nominate a map for the end-match vote.")]
        [ConsoleCommand("nom", "Nominate a map for the end-match vote.")]
        [ConsoleCommand("css_nominate", "Nominate a map for the end-match vote.")]
        [ConsoleCommand("nominate", "Nominate a map for the end-match vote.")]
        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
        public void OnMapsCommand(CCSPlayerController? player, CommandInfo info)
        {
            if (!Humans.TryGetSteamId(player, out ulong id))
            {
                PluginLog.Debug($"maps ignored: {Humans.Label(player)} has no usable SteamID64");
                return;
            }

            string label = Humans.Label(player, id);

            if (_mapsClosed)
            {
                PluginLog.Info($"maps: {label} requested but the browser is closed until next match");
                info.ReplyToCommand(Chat(Localizer["maps.closed"]));
                return;
            }

            if (VoteBusy)
            {
                PluginLog.Info($"maps: {label} requested but a vote is already running");
                info.ReplyToCommand(Chat(Localizer["maps.busy"]));
                return;
            }

            if (_catalog.Maps.Count == 0)
            {
                if (_mapsPendingOpen.Add(id))
                {
                    PluginLog.Info($"maps: {label} is waiting for the catalog to load");
                }
                else
                {
                    PluginLog.Debug($"maps: {label} is already waiting for the catalog");
                }

                _catalog.RequestRefresh(
                    BrowseCatalogBudgetSeconds,
                    Config.Catalog.QueryLocalMaps,
                    Config.Catalog.QueryMapGroup);
                return;
            }

            PluginLog.Debug($"maps: {label} opened immediately, catalog has {_catalog.Maps.Count} map(s)");
            if (PlayerSlots.IsValid(player.Slot))
            {
                ScheduleMapBrowserOpen(player.Slot, id);
            }
        }

        /// <summary>
        /// Opens the browser 0.2s after the chat command, never in the command tick (ruleset §1.3).
        /// A newer !maps from the same slot supersedes a pending open.
        /// </summary>
        private void ScheduleMapBrowserOpen(int slot, ulong steamId)
        {
            if (!PlayerSlots.IsValid(slot))
            {
                return;
            }

            int token = _hudOpenTokens.Next(slot);
            AddTimer(HudOpenDelaySeconds, () =>
            {
                if (_unloaded
                    || !_hudOpenTokens.IsCurrent(slot, token)
                    || _mapsClosed
                    || VoteBusy
                    || _catalog.Maps.Count == 0
                    || Utilities.GetPlayerFromSlot(slot) is not { } player
                    || !Humans.TryGetSteamId(player, out ulong liveSteamId)
                    || liveSteamId != steamId)
                {
                    PluginLog.Debug(
                        $"maps: delayed open skipped slot={slot} (stale token, closed, busy, empty catalog, or player changed)");
                    return;
                }

                OpenMapBrowserFor(player);
                PluginLog.Info($"maps: opened for {Humans.Label(player, steamId)}");
            });
        }

        /// <summary>
        /// Opens the browser for one human. The shared context is bound once per browse
        /// session; each viewer has a private layout entity and its own sort/filter state.
        /// </summary>
        private void OpenMapBrowserFor(CCSPlayerController player)
        {
            if (_unloaded || _mapsClosed || VoteBusy || !Humans.IsHumanViewer(player))
            {
                PluginLog.Debug(
                    () => $"maps: browser open ignored for {Humans.Label(player)} (unloaded={_unloaded}, closed={_mapsClosed}, busy={VoteBusy}, human={Humans.IsHumanViewer(player)})");
                return;
            }

            if (!VoteHud.IsBrowsing)
            {
                List<MapEntry> maps = SelectMaps(excludeCurrent: false);
                var (currentMap, currentOrigin) = CurrentMap();
                Dictionary<string, MapOrigin> origins = new(maps.Count, StringComparer.Ordinal);
                List<string> names = new(maps.Count);
                foreach (MapEntry map in maps)
                {
                    origins[map.Name] = map.Origin;
                    names.Add(map.Name);
                }

                VoteSession? nominations = NominationSessionFor(names);
                VoteHud.BindBrowse(
                    Config.Filters,
                    _archive,
                    _plays,
                    (viewer, key) => Localization.ForPlayerSafe(Localizer, viewer, key),
                    origins,
                    names,
                    currentMap,
                    currentOrigin,
                    Config.CleanMapNames,
                    nominations);
                PluginLog.Debug(
                    () => $"maps: bound browse list with {names.Count} map(s), current={currentMap ?? "unknown"}, "
                        + $"nominations={(nominations is null ? "off" : $"{nominations.VotedOrder().Count} map(s)")}");
            }

            VoteHud.OpenFor(player);
        }

        /// <summary>
        /// Returns the session bound to this map list, or null for browse-only mode.
        /// It is reset on map start, so the list cannot go stale between browse sessions.
        /// </summary>
        private VoteSession? NominationSessionFor(IReadOnlyList<string> maps)
        {
            if (!Config.Nominations.Enabled)
            {
                _nominationSession = null;
                return null;
            }

            return _nominationSession ??= new VoteSession(maps);
        }

        /// <summary>Map start: nominations belong to one map only.</summary>
        private void ResetNominations()
        {
            _nominationSession = null;
            PluginLog.Debug("nominations: reset at map start");
        }

        /// <summary>Very first !maps after a load: opens everyone who was waiting once RCON answers.</summary>
        private void OnMapsCatalogSettled(IReadOnlyList<MapEntry> maps, int generation)
        {
            if (_mapsPendingOpen.Count == 0)
            {
                return;
            }

            if (_unloaded || _mapsClosed || VoteBusy || generation != _catalog.Generation)
            {
                PluginLog.Debug(
                    $"maps: pending opens dropped (unloaded={_unloaded}, closed={_mapsClosed}, busy={VoteBusy}, "
                    + $"generation={generation}/{_catalog.Generation})");
                _mapsPendingOpen.Clear();
                return;
            }

            List<CCSPlayerController> waiting = Humans.GetHumans()
                .Where(player => Humans.TryGetSteamId(player, out ulong id)
                    && _mapsPendingOpen.Contains(id))
                .ToList();
            _mapsPendingOpen.Clear();

            if (maps.Count == 0)
            {
                PluginLog.Info($"maps: catalog settled empty; {waiting.Count} waiting player(s) notified");
                foreach (CCSPlayerController player in waiting)
                {
                    player.PrintToChat(Chat(Localizer["maps.unavailable"]));
                }

                return;
            }

            PluginLog.Info($"maps: catalog settled with {maps.Count} map(s); opening browser for {waiting.Count} player(s)");
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
            PluginLog.Debug($"maps: browser opened ({reason})");
        }

        /// <summary>Intermission / map end: close the browser and reject !maps until the next match.</summary>
        private void CloseMapsForMatchEnd()
        {
            _mapsPendingOpen.Clear();
            _mapsClosed = true;
            VoteHud.CloseBrowse();
            PluginLog.Debug("maps: browser closed until next match");
        }
    }
}
