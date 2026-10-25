using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;
using MapVote.Huds;
using MapVote.Maps;

namespace MapVote.Utils
{
    /// <summary>Single-layout <see cref="CCSCustomHudLayout"/> driver for the map vote overlay.</summary>
    public static partial class CustomHud
    {
        public const string DefaultMapClass = "mv-map-default";
        public const double FadeSeconds = 1.0;

        private static readonly TimeSpan CloseTransmitGrace = TimeSpan.FromSeconds(FadeSeconds);
        private static readonly string LayoutResource =
            $"panorama/layout/custom_game/mapvote/vote_{MapVote.PanoramaVersion}.vxml_c";

        private static CCSCustomHudLayout? _layout;
        private static readonly Dictionary<int, HashSet<string>> VisibleBySlot = [];
        private static readonly Dictionary<int, DateTime> ClosingRootsBySlot = [];
        private static readonly Dictionary<int, Dictionary<string, Dictionary<string, bool>>> ClassBySlot = [];
        private static readonly Dictionary<int, Dictionary<string, Dictionary<string, string>>> TextBySlot = [];
        private static readonly Dictionary<int, Dictionary<string, string>> ImageClassBySlot = [];

        private static bool _budgetActive;
        private static int _writes;

        /// <summary>Native Set* calls since the last <see cref="BeginWriteBudget"/>.</summary>
        public static int Writes => _writes;

        public static void BeginWriteBudget()
        {
            _budgetActive = true;
            _writes = 0;
        }

        public static void EndWriteBudget() => _budgetActive = false;

        public static bool IsPanelVisible(CCSPlayerController player, string panelId) =>
            player is { IsValid: true }
            && VisibleBySlot.TryGetValue(player.Slot, out HashSet<string>? set)
            && set.Contains(panelId);

        public static bool EnsureSpawned(CCSPlayerController player) =>
            Players.IsHumanViewer(player) && EnsureLayout();

        public static bool TryGetLayout(CCSPlayerController player, string rootPanelId, out CCSCustomHudLayout hud)
        {
            hud = null!;
            if (rootPanelId != VoteHud.Panel || !EnsureSpawned(player) || _layout is not { IsValid: true } layout)
            {
                return false;
            }

            hud = layout;
            return true;
        }

        public static bool IsLayout(CCSCustomHudLayout layout, string rootPanelId) =>
            rootPanelId == VoteHud.Panel
            && _layout is { IsValid: true } live
            && live.Handle == layout.Handle;

        public static void ShowPanel(CCSPlayerController player, string panelId)
        {
            if (player is not { IsValid: true })
            {
                return;
            }

            if (!VisibleBySlot.TryGetValue(player.Slot, out HashSet<string>? set))
            {
                VisibleBySlot[player.Slot] = set = [];
            }

            bool wasVisible = set.Contains(panelId);
            set.Add(panelId);
            ClearClosingRoot(player.Slot);
            SetHasClass(player, panelId, "is-off", false);
            if (wasVisible)
            {
                SetHasClass(player, panelId, "is-fading", false);
            }
            else
            {
                ScheduleFadeIn(player, panelId);
            }
        }

        public static void HidePanel(CCSPlayerController player, string panelId)
        {
            if (player is not { IsValid: true })
            {
                return;
            }

            SetHasClass(player, panelId, "is-fading", true);
            MarkClosingRoot(player.Slot);
            ForgetVisible(player.Slot, panelId);
        }

        public static void SetText(CCSPlayerController player, string panelId, string name, string value)
        {
            if (player is not { IsValid: true })
            {
                return;
            }

            string text = value ?? string.Empty;
            if (TryGetCached(TextBySlot, player.Slot, panelId, name, out string? prev) && prev == text)
            {
                return;
            }

            if (!TryWrite(player, out CCSCustomHudLayout hud))
            {
                return;
            }

            hud.SetDialogVariableStringForPlayer(player, panelId, name, text);
            SetCached(TextBySlot, player.Slot, panelId, name, text);
            NoteWrite();
        }

        public static bool SetHasClass(CCSPlayerController player, string panelId, string className, bool enabled)
        {
            if (player is not { IsValid: true } || string.IsNullOrEmpty(className))
            {
                return false;
            }

            if (TryGetCached(ClassBySlot, player.Slot, panelId, className, out bool prev) && prev == enabled)
            {
                return true;
            }

            if (!TryWrite(player, out CCSCustomHudLayout hud))
            {
                return false;
            }

            hud.SetHasClassForPlayer(player, panelId, className, enabled);
            SetCached(ClassBySlot, player.Slot, panelId, className, enabled);
            NoteWrite();
            return true;
        }

        private static void NoteWrite()
        {
            if (_budgetActive)
            {
                _writes++;
            }
        }

        public static void SetMapImage(CCSPlayerController player, string imagePanelId, string? mapName)
        {
            string next = string.IsNullOrWhiteSpace(mapName)
                ? DefaultMapClass
                : $"mv-map-{MapNameFilter.SanitizeCssStem(mapName)}";
            SwapImageClass(player, imagePanelId, next);
        }

        public static void OnTick() => VoteHud.OnTick();

        private static void SwapImageClass(CCSPlayerController player, string imagePanelId, string next)
        {
            if (player is not { IsValid: true })
            {
                return;
            }

            if (!ImageClassBySlot.TryGetValue(player.Slot, out Dictionary<string, string>? map))
            {
                ImageClassBySlot[player.Slot] = map = new(StringComparer.Ordinal);
            }

            map.TryGetValue(imagePanelId, out string? prev);
            if (prev == next)
            {
                return;
            }

            if (prev is not null)
            {
                if (!SetHasClass(player, imagePanelId, prev, false))
                {
                    return;
                }
            }
            else if (next != DefaultMapClass && !SetHasClass(player, imagePanelId, DefaultMapClass, false))
            {
                return;
            }

            if (!SetHasClass(player, imagePanelId, next, true))
            {
                return;
            }

            map[imagePanelId] = next;
        }

        private static void ScheduleFadeIn(CCSPlayerController player, string panelId)
        {
            SetHasClass(player, panelId, "is-fading", true);
            int slot = player.Slot;
            string id = panelId;
            Server.NextFrame(() =>
            {
                if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } live && IsPanelVisible(live, id))
                {
                    SetHasClass(live, id, "is-fading", false);
                }
            });
        }

        private static void ForgetVisible(int slot, string panelId)
        {
            if (!VisibleBySlot.TryGetValue(slot, out HashSet<string>? set))
            {
                return;
            }

            set.Remove(panelId);
            if (set.Count == 0)
            {
                VisibleBySlot.Remove(slot);
            }
        }

        private static bool TryGetCached<T>(
            Dictionary<int, Dictionary<string, Dictionary<string, T>>> store,
            int slot,
            string panelId,
            string key,
            out T value)
        {
            value = default!;
            return store.TryGetValue(slot, out Dictionary<string, Dictionary<string, T>>? byPanel)
                && byPanel.TryGetValue(panelId, out Dictionary<string, T>? map)
                && map.TryGetValue(key, out value!);
        }

        private static void SetCached<T>(
            Dictionary<int, Dictionary<string, Dictionary<string, T>>> store,
            int slot,
            string panelId,
            string key,
            T value)
        {
            if (!store.TryGetValue(slot, out Dictionary<string, Dictionary<string, T>>? byPanel))
            {
                store[slot] = byPanel = new(StringComparer.Ordinal);
            }

            if (!byPanel.TryGetValue(panelId, out Dictionary<string, T>? map))
            {
                byPanel[panelId] = map = new(StringComparer.Ordinal);
            }

            map[key] = value;
        }
    }
}
