using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Utils;
using MapVote.Huds;
using MapVote.Maps;

namespace MapVote.Utils
{
    /// <summary>Single-layout <see cref="CCSCustomHudLayout"/> driver for the map vote overlay.</summary>
    public static class CustomHud
    {
        public const double FadeSeconds = 0.5;

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
            Humans.IsHumanViewer(player) && EnsureLayout();

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

            if (TryGetCached(TextBySlot, player.Slot, panelId, name, out string? prev) && prev == value)
            {
                return;
            }

            if (!TryWrite(player, out CCSCustomHudLayout hud))
            {
                return;
            }

            hud.SetDialogVariableStringForPlayer(player, panelId, name, value);
            SetCached(TextBySlot, player.Slot, panelId, name, value);
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

        /// <summary>
        /// Sets the overlay map-shot class. Empty names clear it so the id-less
        /// <c>mv-map-default</c> panel behind the shot shows through.
        /// </summary>
        public static void SetMapImage(
            CCSPlayerController player,
            string imagePanelId,
            string? mapName,
            MapOrigin origin = MapOrigin.Local)
        {
            if (player is not { IsValid: true })
            {
                return;
            }

            string? next = string.IsNullOrWhiteSpace(mapName)
                ? null
                : MapImageClasses.ClassFor(mapName, origin);
            if (!ImageClassBySlot.TryGetValue(player.Slot, out Dictionary<string, string>? map))
            {
                ImageClassBySlot[player.Slot] = map = new(StringComparer.Ordinal);
            }

            map.TryGetValue(imagePanelId, out string? prev);
            if (prev == next)
            {
                return;
            }

            if (prev is not null && !SetHasClass(player, imagePanelId, prev, false))
            {
                return;
            }

            if (next is null)
            {
                map.Remove(imagePanelId);
                return;
            }

            if (SetHasClass(player, imagePanelId, next, true))
            {
                map[imagePanelId] = next;
            }
        }

        public static void ResetRound()
        {
            HashSet<int> liveSlots = [];
            foreach (CCSPlayerController player in Humans.GetHumans())
            {
                liveSlots.Add(player.Slot);
                HudMenu.ReleasePlayer(player);
                RestorePlayerDefaults(player);
            }

            PurgeOrphans(liveSlots);
        }

        public static void ReleasePlayer(CCSPlayerController? player)
        {
            if (player is null || !player.IsValid)
            {
                return;
            }

            HudMenu.ReleasePlayer(player);
            RestorePlayerDefaults(player);
            if (TryGetLayout(player, VoteHud.Panel, out CCSCustomHudLayout hud))
            {
                hud.SetInputCaptureEnabled(player, false);
            }
        }

        public static void Shutdown()
        {
            HudMenu.Shutdown();
            ClearAllSlotState();
            if (_layout is { IsValid: true } layout)
            {
                layout.AcceptInput("Kill");
            }

            _layout = null;
        }

        public static void OnCheckTransmit(CCheckTransmitInfoList infoList)
        {
            ExpireClosingRoots();

            if (_layout is not { IsValid: true } layout)
            {
                return;
            }

            if (VisibleBySlot.Count == 0 && ClosingRootsBySlot.Count == 0)
            {
                return;
            }

            int index = (int)layout.Index;
            foreach ((CCheckTransmitInfo info, CCSPlayerController? viewer) in infoList)
            {
                if (!Humans.IsHumanViewer(viewer))
                {
                    continue;
                }

                if (ShouldTransmit(viewer.Slot))
                {
                    info.TransmitEntities.Add(index);
                }
                else
                {
                    info.TransmitEntities.Remove(index);
                }
            }
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

        private static bool EnsureLayout()
        {
            if (_layout is { IsValid: true } existing && LayoutPath(existing) == LayoutResource)
            {
                return true;
            }

            if (_layout is { IsValid: true } stale)
            {
                stale.AcceptInput("Kill");
                _layout = null;
            }

            foreach (CCSCustomHudLayout found in
                     Utilities.FindAllEntitiesByDesignerName<CCSCustomHudLayout>("custom_hud_layout"))
            {
                if (found is { IsValid: true } && LayoutPath(found) == LayoutResource)
                {
                    _layout = found;
                    return true;
                }
            }

            return CreateLayout();
        }

        private static bool CreateLayout()
        {
            try
            {
                CCSCustomHudLayout? hud = Utilities.CreateEntityByName<CCSCustomHudLayout>("custom_hud_layout");
                if (hud is null || hud.Handle == IntPtr.Zero)
                {
                    PluginLog.Info($"[CustomHud] CreateEntityByName failed for {LayoutResource}");
                    return false;
                }

                using (CEntityKeyValues kv = new())
                {
                    kv.SetVector("origin", 0f, 0f, 0f);
                    kv.SetString("layout", LayoutResource);
                    kv.SetBool("observable", false);
                    hud.DispatchSpawn(kv);
                }

                if (hud is not { IsValid: true })
                {
                    PluginLog.Info($"[CustomHud] DispatchSpawn left entity invalid for {LayoutResource}");
                    return false;
                }

                _layout = hud;
                PluginLog.Debug($"[CustomHud] Spawned #{hud.Index} → {LayoutResource}");
                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Info($"[CustomHud] Spawn failed for {LayoutResource}: {ex.Message}");
                return false;
            }
        }

        private static bool TryWrite(CCSPlayerController player, out CCSCustomHudLayout hud)
        {
            hud = null!;
            if (!EnsureSpawned(player) || _layout is not { IsValid: true } layout)
            {
                return false;
            }

            hud = layout;
            try
            {
                return player.Slot >= 0 && player.Slot < hud.PlayerLayoutStates.Count;
            }
            catch
            {
                return false;
            }
        }

        private static void RestorePlayerDefaults(CCSPlayerController player)
        {
            if (player is not { IsValid: true })
            {
                return;
            }

            int slot = player.Slot;
            bool dirty = ClassBySlot.ContainsKey(slot)
                || TextBySlot.ContainsKey(slot)
                || VisibleBySlot.ContainsKey(slot)
                || ImageClassBySlot.ContainsKey(slot);
            if (!dirty)
            {
                return;
            }

            ClassBySlot.TryGetValue(slot, out Dictionary<string, Dictionary<string, bool>>? classes);
            TextBySlot.TryGetValue(slot, out Dictionary<string, Dictionary<string, string>>? texts);
            ClearSlotCaches(slot);

            if (classes is not null)
            {
                foreach ((string panelId, Dictionary<string, bool> map) in classes)
                {
                    foreach ((string className, bool enabled) in map.Where(static kv => kv.Value))
                    {
                        SetHasClass(player, panelId, className, false);
                    }
                }
            }

            if (texts is not null)
            {
                foreach ((string panelId, Dictionary<string, string> vars) in texts)
                {
                    foreach ((string name, string value) in vars.Where(static kv => kv.Value.Length > 0))
                    {
                        SetText(player, panelId, name, string.Empty);
                    }
                }
            }

            SetHasClass(player, VoteHud.Panel, "is-fading", false);
            SetHasClass(player, VoteHud.Panel, "is-off", true);
            MarkClosingRoot(slot);
            VoteHud.WriteDefaults(player);
            ClearSlotCaches(slot);
        }

        private static void ClearSlotCaches(int slot)
        {
            VisibleBySlot.Remove(slot);
            ClassBySlot.Remove(slot);
            TextBySlot.Remove(slot);
            ImageClassBySlot.Remove(slot);
        }

        private static void PurgeOrphans(HashSet<int> liveSlots)
        {
            static void Purge<T>(Dictionary<int, T> map, HashSet<int> live)
            {
                foreach (int slot in map.Keys.Where(s => !live.Contains(s)).ToList())
                {
                    map.Remove(slot);
                }
            }

            Purge(VisibleBySlot, liveSlots);
            Purge(ClassBySlot, liveSlots);
            Purge(TextBySlot, liveSlots);
            Purge(ImageClassBySlot, liveSlots);
            Purge(ClosingRootsBySlot, liveSlots);
        }

        private static void ClearAllSlotState()
        {
            VisibleBySlot.Clear();
            ClosingRootsBySlot.Clear();
            ClassBySlot.Clear();
            TextBySlot.Clear();
            ImageClassBySlot.Clear();
        }

        private static string? LayoutPath(CCSCustomHudLayout layout)
        {
            try
            {
                return layout.StrLayout;
            }
            catch
            {
                return null;
            }
        }

        private static bool ShouldTransmit(int slot)
        {
            if (VisibleBySlot.TryGetValue(slot, out HashSet<string>? set) && set.Contains(VoteHud.Panel))
            {
                return true;
            }

            return ClosingRootsBySlot.TryGetValue(slot, out DateTime until) && DateTime.UtcNow < until;
        }

        private static void MarkClosingRoot(int slot) =>
            ClosingRootsBySlot[slot] = DateTime.UtcNow + CloseTransmitGrace;

        private static void ClearClosingRoot(int slot) => ClosingRootsBySlot.Remove(slot);

        private static void ExpireClosingRoots()
        {
            if (ClosingRootsBySlot.Count == 0)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            foreach (int slot in ClosingRootsBySlot.Keys.Where(s => now >= ClosingRootsBySlot[s]).ToList())
            {
                ClosingRootsBySlot.Remove(slot);
                if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } player)
                {
                    SetHasClass(player, VoteHud.Panel, "is-off", true);
                    SetHasClass(player, VoteHud.Panel, "is-fading", false);
                }
            }
        }
    }
}
