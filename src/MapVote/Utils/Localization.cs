using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using Microsoft.Extensions.Localization;

namespace MapVote.Utils
{
    /// <summary>
    /// Safe per-player localization. CounterStrikeSharp's <c>ForPlayer</c> throws for bots and SteamID 0.
    /// </summary>
    public static class Localization
    {
        public static string ForPlayerSafe(IStringLocalizer localizer, CCSPlayerController? player, string key)
        {
            try
            {
                if (player is { IsValid: true }
                    && player.AuthorizedSteamID is { SteamId64: > 0 })
                {
                    return localizer.ForPlayer(player, key);
                }
            }
            catch
            {
            }

            return localizer[key];
        }
    }
}
