using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;
using MapVote.Huds;
using CssTimer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace MapVote.Utils
{
    public sealed class HudMenuOpenOptions
    {
        public required Action<CCSPlayerController, string> OnButton { get; init; }
        public Action<CCSPlayerController>? OnClosed { get; init; }
        public bool CaptureMouse { get; init; } = true;
    }

    public static class HudMenu
    {
        public const string PanelId = VoteHud.Panel;
        private const float CaptureDelaySeconds = 0.2f;

        private sealed class Session
        {
            public required HudMenuOpenOptions Options;
            public int CaptureToken;
            public CssTimer? CaptureTimer;
        }

        private static readonly Dictionary<int, Session> Sessions = [];

        public static bool IsOpen(CCSPlayerController player) =>
            player is { IsValid: true } && Sessions.ContainsKey(player.Slot);

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
            CustomHud.ShowPanel(player, PanelId);
            if (options.CaptureMouse)
            {
                ScheduleCapture(player, session);
            }

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

        public static void DropCapture(CCSPlayerController player)
        {
            if (!Sessions.TryGetValue(player.Slot, out Session? session))
            {
                return;
            }

            InvalidateCapture(session);
            SetCapture(player, enabled: false);
        }

        public static void Shutdown()
        {
            foreach (int slot in Sessions.Keys.ToList())
            {
                if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } player
                    && Sessions.TryGetValue(slot, out Session? session))
                {
                    CloseInternal(player, session, invokeClosed: false);
                }
                else
                {
                    Sessions.Remove(slot);
                }
            }
        }

        public static bool OnCustomHudClicked(CCSPlayerController player, CCSCustomHudLayout layout, string buttonId)
        {
            if (!Sessions.TryGetValue(player.Slot, out Session? session)
                || string.IsNullOrEmpty(buttonId)
                || !CustomHud.IsLayout(layout, PanelId))
            {
                return false;
            }

            try
            {
                session.Options.OnButton(player, buttonId);
            }
            catch (Exception ex)
            {
                PluginLog.Info($"[HudMenu] OnButton failed: {ex.Message}");
            }

            return true;
        }

        private static void ScheduleCapture(CCSPlayerController player, Session session)
        {
            int token = ++session.CaptureToken;
            session.CaptureTimer = new CssTimer(CaptureDelaySeconds, () =>
            {
                if (!Sessions.TryGetValue(player.Slot, out Session? live)
                    || live.CaptureToken != token
                    || !player.IsValid)
                {
                    return;
                }

                live.CaptureTimer = null;
                SetCapture(player, enabled: true);
            });
        }

        private static void CloseInternal(CCSPlayerController player, Session session, bool invokeClosed)
        {
            InvalidateCapture(session);
            Sessions.Remove(player.Slot);
            SetCapture(player, enabled: false);
            CustomHud.HidePanel(player, PanelId);

            if (invokeClosed && player.IsValid && session.Options.OnClosed is { } onClosed)
            {
                try
                {
                    onClosed(player);
                }
                catch (Exception ex)
                {
                    PluginLog.Info($"[HudMenu] OnClosed failed: {ex.Message}");
                }
            }
        }

        private static void InvalidateCapture(Session session)
        {
            session.CaptureToken++;
            session.CaptureTimer?.Kill();
            session.CaptureTimer = null;
        }

        private static void SetCapture(CCSPlayerController player, bool enabled)
        {
            if (player.IsValid && CustomHud.TryGetLayout(player, PanelId, out CCSCustomHudLayout hud))
            {
                hud.SetInputCaptureEnabled(player, enabled);
            }
        }
    }
}
