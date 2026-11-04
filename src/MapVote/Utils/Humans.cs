using System.Diagnostics.CodeAnalysis;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace MapVote.Utils
{
    public static class Humans
    {
        public static bool IsHumanViewer([NotNullWhen(true)] CCSPlayerController? player) =>
            player is { IsValid: true, IsBot: false, IsHLTV: false };

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
