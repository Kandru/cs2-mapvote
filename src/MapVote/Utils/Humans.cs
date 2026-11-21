using System.Diagnostics.CodeAnalysis;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace MapVote.Utils
{
    public static class Humans
    {
        /// <summary>A controller that can see and interact with the HUD.</summary>
        public static bool IsHumanViewer([NotNullWhen(true)] CCSPlayerController? player) =>
            player is { IsValid: true, IsBot: false, IsHLTV: false };

        /// <summary>
        /// A human with a usable SteamID64. Bots, HLTV, connecting players and players
        /// without a positive authorized id never own votes, ratings or favorites.
        /// </summary>
        public static bool TryGetSteamId(
            [NotNullWhen(true)] CCSPlayerController? player,
            out ulong steamId)
        {
            steamId = 0;
            if (!IsHumanViewer(player))
            {
                return false;
            }

            ulong? authorized = player.AuthorizedSteamID?.SteamId64;
            if (authorized is not > 0)
            {
                return false;
            }

            steamId = authorized.Value;
            return true;
        }

        /// <summary>Console label: "Name (steamid)". Missing name → "?", missing id → "0".</summary>
        public static string Label(CCSPlayerController? player, ulong? steamId = null)
        {
            string? raw = player?.PlayerName;
            string name = string.IsNullOrWhiteSpace(raw) ? "?" : raw;
            ulong id = steamId
                ?? player?.AuthorizedSteamID?.SteamId64
                ?? 0;
            return $"{name} ({id})";
        }

        public static IEnumerable<CCSPlayerController> GetHumans() =>
            Utilities.GetPlayers().Where(IsHumanViewer);
    }
}
