using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;
using MapVote.Runtime;
using MapVote.Utils;
using CssTimer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace MapVote.Huds
{
    public sealed class HudMenuOpenOptions
    {
        public required Action<CCSPlayerController, string> OnButton { get; init; }
        public Action<CCSPlayerController>? OnClosed { get; init; }
    }

    public static class HudMenu
    {
        public const string PanelId = VoteHud.Panel;
        private const float CaptureDelaySeconds = GameConstants.CaptureDelaySeconds;

        private sealed class Session
        {
            public required HudMenuOpenOptions Options;
            public int CaptureToken;
        }

        private static readonly Dictionary<int, Session> Sessions = [];

        public static bool IsOpen(CCSPlayerController player) =>
            player is { IsValid: true } && Sessions.ContainsKey(player.Slot);

        public static bool HasOpenSessions => Sessions.Count > 0;

        public static bool Open(CCSPlayerController player, HudMenuOpenOptions options)
        {
            if (!CustomHud.EnsureSpawned(player))
            {
                return false;
            }

            if (Sessions.TryGetValue(player.Slot, out Session? existing))
            {
                CloseInternal(player, existing, invokeClosed: false);
            }

            Session session = new() { Options = options };
            Sessions[player.Slot] = session;
            CustomHud.ShowPanel(player);
            ScheduleCapture(player.Slot, session);
            PluginLog.Debug($"hud menu opened: {Humans.Label(player)} slot={player.Slot}");
            return true;
        }

        public static void Close(CCSPlayerController player)
        {
            if (Sessions.TryGetValue(player.Slot, out Session? session))
            {
                CloseInternal(player, session, invokeClosed: true);
            }
        }

        public static void ReleasePlayer(CCSPlayerController? player)
        {
            if (player is not null && Sessions.TryGetValue(player.Slot, out Session? session))
            {
                CloseInternal(player, session, invokeClosed: false);
            }
        }

        /// <summary>Disconnect path when the controller is already invalid: drop the session without writing.</summary>
        public static void ReleaseSlot(int slot)
        {
            if (Sessions.Remove(slot, out Session? session))
            {
                InvalidateCapture(session);
            }
        }

        /// <summary>Drop sessions without touching layout entities (used before a full layout reset).</summary>
        public static void ResetSessions()
        {
            foreach (Session session in Sessions.Values)
            {
                InvalidateCapture(session);
            }

            Sessions.Clear();
        }

        public static bool OnCustomHudClicked(CCSPlayerController player, CCSCustomHudLayout layout, string buttonId)
        {
            if (!Sessions.TryGetValue(player.Slot, out Session? session)
                || string.IsNullOrEmpty(buttonId)
                || !CustomHud.IsLayout(player, layout))
            {
                return false;
            }

            try
            {
                session.Options.OnButton(player, buttonId);
            }
            catch (Exception ex)
            {
                PluginLog.Info(
                    $"hud button handler failed: {Humans.Label(player)} slot={player.Slot} id={buttonId}: {ex.Message}");
            }

            return true;
        }

        private static void ScheduleCapture(int slot, Session session)
        {
            int token = ++session.CaptureToken;
            _ = new CssTimer(CaptureDelaySeconds, () =>
            {
                // The session check stands in for Kill(): a reopened menu has a new session object.
                if (Utilities.GetPlayerFromSlot(slot) is not { IsValid: true } player
                    || !Sessions.TryGetValue(slot, out Session? live)
                    || live != session
                    || live.CaptureToken != token)
                {
                    return;
                }

                SetCapture(player, enabled: true);
            });
        }

        private static void CloseInternal(CCSPlayerController player, Session session, bool invokeClosed)
        {
            InvalidateCapture(session);
            Sessions.Remove(player.Slot);
            SetCapture(player, enabled: false);
            CustomHud.HidePanel(player);

            if (invokeClosed && player.IsValid && session.Options.OnClosed is { } onClosed)
            {
                try
                {
                    onClosed(player);
                }
                catch (Exception ex)
                {
                    PluginLog.Info(
                        $"hud close handler failed: {Humans.Label(player)} slot={player.Slot}: {ex.Message}");
                }
            }

            PluginLog.Debug(
                $"hud menu closed: {Humans.Label(player)} slot={player.Slot} notify={invokeClosed}");
        }

        private static void InvalidateCapture(Session session)
        {
            session.CaptureToken++;
        }

        private static void SetCapture(CCSPlayerController player, bool enabled)
        {
            if (player.IsValid && CustomHud.TryGetLayout(player, PanelId, out CCSCustomHudLayout hud))
            {
                hud.SetInputCaptureEnabled(player, enabled);
                CustomHud.NoteWrite();
            }
        }
    }
}
