using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Utils;
using MapVote.Huds;

namespace MapVote.Utils
{
    public static partial class CustomHud
    {
        public static void ResetRound()
        {
            HashSet<int> liveSlots = [];
            foreach (CCSPlayerController player in Players.GetHumans())
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
                PluginLog.Info($"[CustomHud] Spawned #{hud.Index} → {LayoutResource}");
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

            SetHasClass(player, VoteHud.Panel, "ph-fading", false);
            SetHasClass(player, VoteHud.Panel, "ph-off", true);
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
    }
}
