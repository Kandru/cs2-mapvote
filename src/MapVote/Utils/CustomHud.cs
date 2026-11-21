using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Utils;
using MapVote.Huds;
using MapVote.Maps;
using MapVote.Runtime;

namespace MapVote.Utils
{
    /// <summary>
    /// One private <see cref="CCSCustomHudLayout"/> per human slot. The entity is only
    /// transmitted to its owner, so every write on it is effectively global for that player:
    /// no per-player class/text vectors are multiplied by the viewer count.
    /// </summary>
    public static class CustomHud
    {
        private static readonly int MaxSlots = PlayerSlots.Capacity;
        private const int SpawnsPerTick = GameConstants.SpawnsPerTick;

        private static readonly TimeSpan CloseTransmitGrace =
            TimeSpan.FromSeconds(GameConstants.CloseTransmitGraceSeconds);
        private static readonly string LayoutResource =
            $"panorama/layout/custom_game/mapvote/vote_{MapVote.PanoramaVersion}.vxml_c";

        private sealed class PlayerHud
        {
            public CCSCustomHudLayout? Layout;
            public ulong OwnerSteamId;
            public bool Visible;
            public DateTime ClosingUntil;
            public readonly Dictionary<string, Dictionary<string, string>> Text = new(StringComparer.Ordinal);
            public readonly Dictionary<string, Dictionary<string, bool>> Class = new(StringComparer.Ordinal);
            public readonly Dictionary<string, string> ImageClass = new(StringComparer.Ordinal);
        }

        private static readonly PlayerHud?[] Slots = new PlayerHud?[MaxSlots];
        private static readonly Queue<int> SpawnQueue = new();
        private static readonly bool[] Queued = new bool[MaxSlots];

        /// <summary>Classes the XML authors as present; an uncached false write must still go out.</summary>
        private static readonly HashSet<string> AuthoredOn = new(StringComparer.Ordinal) { "is-off", "p0", "votes-none", "view-vote" };

        private static int _writesTick = -1;
        private static int _writes;

        /// <summary>Native layout writes in the current server tick, from every caller and every player.</summary>
        public static int TickWrites => WritesInTick(_writesTick, _writes, Server.TickCount);

        /// <summary>A count belongs to the tick it was made in; any other tick starts at zero.</summary>
        internal static int WritesInTick(int countedTick, int writes, int nowTick) =>
            countedTick == nowTick ? writes : 0;

        /// <summary>Counts one native write against the per-tick budget.</summary>
        internal static void NoteWrite()
        {
            int now = Server.TickCount;
            _writes = WritesInTick(_writesTick, _writes, now) + 1;
            _writesTick = now;
        }

        private static bool IsVisible(CCSPlayerController player) =>
            player is { IsValid: true }
            && PlayerSlots.IsValid(player.Slot)
            && Slots[player.Slot] is { Visible: true };

        public static bool EnsureSpawned(CCSPlayerController player)
        {
            if (!Humans.TryGetSteamId(player, out ulong steamId) || !TryEntry(player, out PlayerHud entry))
            {
                return false;
            }
            if (entry.Layout is { IsValid: true })
            {
                if (entry.OwnerSteamId == steamId)
                {
                    return true;
                }

                // Slot was reused before the disconnect cleanup ran: never hand over stale state.
                // Reset in place so callers holding this entry keep a valid object.
                entry.Layout.AcceptInput("Kill");
                entry.Layout = null;
            }

            return CreateLayout(player.Slot, entry, steamId);
        }

        /// <summary>
        /// Plugin start / hot reload: forget in-memory HUD state. Never touch the entity
        /// system here: Load can run before it is initialized, and the layout entities only
        /// exist per player. They are created on connect full (or by the next map's
        /// <see cref="ResetRound"/>) and removed on disconnect, map end and unload.
        /// </summary>
        public static void Initialize()
        {
            HudMenu.ResetSessions();
            ClearAll();
        }

        /// <summary>Queue an entity for this slot; <see cref="OnTick"/> creates it outside the connect tick.</summary>
        public static void QueueSpawn(CCSPlayerController player)
        {
            if (!Humans.TryGetSteamId(player, out ulong steamId) || !PlayerSlots.IsValid(player.Slot))
            {
                return;
            }

            int slot = player.Slot;
            if (Queued[slot]
                || (Slots[slot] is { Layout: { IsValid: true }, OwnerSteamId: var owner } && owner == steamId))
            {
                return;
            }

            Queued[slot] = true;
            SpawnQueue.Enqueue(slot);
        }

        public static void OnTick()
        {
            for (int spawned = 0; spawned < SpawnsPerTick && SpawnQueue.Count > 0; spawned++)
            {
                int slot = SpawnQueue.Dequeue();
                Queued[slot] = false;
                if (Utilities.GetPlayerFromSlot(slot) is { } player && Humans.IsHumanViewer(player))
                {
                    EnsureSpawned(player);
                }
            }
        }

        public static bool TryGetLayout(CCSPlayerController player, string rootPanelId, out CCSCustomHudLayout hud)
        {
            hud = null!;
            if (rootPanelId != VoteHud.Panel || !EnsureSpawned(player) || !PlayerSlots.IsValid(player.Slot))
            {
                return false;
            }

            if (Slots[player.Slot]?.Layout is { IsValid: true } layout)
            {
                // Input capture is stored in the per-player vector; keep the old guard.
                try
                {
                    if (player.Slot >= layout.PlayerLayoutStates.Count)
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }

                hud = layout;
                return true;
            }

            return false;
        }

        /// <summary>True when <paramref name="layout"/> is the private entity of <paramref name="player"/>.</summary>
        public static bool IsLayout(CCSPlayerController player, CCSCustomHudLayout layout) =>
            Humans.TryGetSteamId(player, out ulong steamId)
            && PlayerSlots.IsValid(player.Slot)
            && Slots[player.Slot] is { Layout: { IsValid: true } owned, OwnerSteamId: var owner }
            && owner == steamId
            && owned.Handle == layout.Handle;

        /// <summary>SteamID recorded for a slot's HUD entry, even if its layout entity was lost.</summary>
        public static bool TryGetOwner(int slot, out ulong steamId)
        {
            steamId = 0;
            if (!PlayerSlots.IsValid(slot) || Slots[slot] is not { } entry || entry.OwnerSteamId == 0)
            {
                return false;
            }

            steamId = entry.OwnerSteamId;
            return true;
        }

        /// <summary>Finds the HUD entry owned by <paramref name="steamId"/>; one SteamID never has two.</summary>
        public static int FindSlotBySteamId(ulong steamId)
        {
            if (steamId == 0)
            {
                return -1;
            }

            for (int slot = 0; slot < MaxSlots; slot++)
            {
                if (Slots[slot] is { OwnerSteamId: var owner } && owner == steamId)
                {
                    return slot;
                }
            }

            return -1;
        }

        public static void ShowPanel(CCSPlayerController player)
        {
            if (!TryEntry(player, out PlayerHud entry) || !EnsureSpawned(player))
            {
                return;
            }

            bool wasVisible = entry.Visible;
            entry.Visible = true;
            ClearClosingRoot(entry);
            SetHasClass(player, VoteHud.Panel, "is-off", false);
            if (wasVisible)
            {
                SetHasClass(player, VoteHud.Panel, "is-fading", false);
            }
            else
            {
                ScheduleFadeIn(player);
            }
        }

        public static void HidePanel(CCSPlayerController player)
        {
            if (!TryEntry(player, out PlayerHud entry))
            {
                return;
            }

            SetHasClass(player, VoteHud.Panel, "is-fading", true);
            MarkClosing(entry);
            entry.Visible = false;
        }

        public static void SetText(CCSPlayerController player, string panelId, string name, string value)
        {
            if (!TryEntry(player, out PlayerHud entry))
            {
                return;
            }

            if (entry.Layout is not { IsValid: true } && !EnsureSpawned(player))
            {
                return;
            }

            bool cached = TryGetCached(entry.Text, panelId, name, out string? prev);
            if (cached && prev == value)
            {
                return;
            }

            if (!cached && value.Length == 0)
            {
                // Never written: the client still shows the XML default for this var.
                SetCached(entry.Text, panelId, name, value);
                return;
            }

            if (entry.Layout is not { IsValid: true } layout)
            {
                return;
            }

            layout.SetDialogVariableString(panelId, name, value);
            SetCached(entry.Text, panelId, name, value);
            NoteWrite();
        }

        public static bool SetHasClass(CCSPlayerController player, string panelId, string className, bool enabled)
        {
            if (string.IsNullOrEmpty(className) || !TryEntry(player, out PlayerHud entry))
            {
                return false;
            }

            if (entry.Layout is not { IsValid: true } && !EnsureSpawned(player))
            {
                return false;
            }

            bool cached = TryGetCached(entry.Class, panelId, className, out bool prev);
            if (cached && prev == enabled)
            {
                return true;
            }

            // Only the AuthoredOn classes can be true in the XML; every other
            // state class starts absent, so an unwritten false is already the default.
            if (!cached && !enabled && !AuthoredOn.Contains(className))
            {
                SetCached(entry.Class, panelId, className, false);
                return true;
            }

            if (entry.Layout is not { IsValid: true } layout)
            {
                return false;
            }

            layout.SetHasClass(panelId, className, enabled);
            SetCached(entry.Class, panelId, className, enabled);
            NoteWrite();
            return true;
        }

        /// <summary>The per-entity class value last written for this player, if any.</summary>
        public static bool TryGetPlayerClass(CCSPlayerController player, string panelId, string className, out bool enabled)
        {
            enabled = false;
            return PlayerSlots.IsValid(player.Slot)
                && Slots[player.Slot] is { Layout: { IsValid: true } } entry
                && TryGetCached(entry.Class, panelId, className, out enabled);
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
            if (!TryEntry(player, out PlayerHud entry)
                || (entry.Layout is not { IsValid: true } && !EnsureSpawned(player)))
            {
                return;
            }

            string? next = string.IsNullOrWhiteSpace(mapName)
                ? null
                : MapImageClasses.ClassFor(mapName, origin);
            entry.ImageClass.TryGetValue(imagePanelId, out string? prev);
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
                entry.ImageClass.Remove(imagePanelId);
                return;
            }

            if (SetHasClass(player, imagePanelId, next, true))
            {
                entry.ImageClass[imagePanelId] = next;
            }
        }

        public static void ResetRound()
        {
            HashSet<int> liveSlots = [];
            foreach (CCSPlayerController player in Humans.GetHumans())
            {
                liveSlots.Add(player.Slot);
                // The client rebuilt the layout from XML on round restart.
                HudMenu.ReleasePlayer(player);
                if (PlayerSlots.IsValid(player.Slot) && Slots[player.Slot] is not null)
                {
                    RestorePlayerDefaults(player);
                }
                else
                {
                    VoteHud.WriteDefaults(player);
                }
            }

            for (int slot = 0; slot < MaxSlots; slot++)
            {
                if (!liveSlots.Contains(slot) && Slots[slot] is not null)
                {
                    Kill(slot);
                }
            }

            QueueHumans();
        }

        /// <summary>
        /// Disconnect / map-boundary cleanup by slot. Never writes to the controller:
        /// the layout entity is killed and the input-capture session is dropped instead.
        /// </summary>
        public static void ReleaseSlot(int slot)
        {
            if (!PlayerSlots.IsValid(slot))
            {
                return;
            }

            HudMenu.ReleaseSlot(slot);
            Kill(slot);
        }

        public static void Shutdown()
        {
            HudMenu.ResetSessions();
            for (int slot = 0; slot < MaxSlots; slot++)
            {
                Kill(slot);
            }

            ClearAll();
        }

        public static void OnCheckTransmit(CCheckTransmitInfoList infoList)
        {
            ExpireClosingRoots();

            foreach ((CCheckTransmitInfo info, CCSPlayerController? viewer) in infoList)
            {
                bool human = Humans.TryGetSteamId(viewer, out ulong viewerSteamId);
                for (int slot = 0; slot < MaxSlots; slot++)
                {
                    if (Slots[slot] is not { Layout: { IsValid: true } layout } entry)
                    {
                        continue;
                    }

                    int index = (int)layout.Index;
                    if (human && viewer!.Slot == slot && entry.OwnerSteamId == viewerSteamId
                        && ShouldTransmit(entry))
                    {
                        info.TransmitEntities.Add(index);
                    }
                    else
                    {
                        info.TransmitEntities.Remove(index);
                    }
                }
            }
        }

        private static void QueueHumans()
        {
            foreach (CCSPlayerController player in Humans.GetHumans())
            {
                QueueSpawn(player);
            }
        }

        private static bool TryEntry(CCSPlayerController player, out PlayerHud entry)
        {
            entry = null!;
            if (!Humans.IsHumanViewer(player) || !PlayerSlots.IsValid(player.Slot))
            {
                return false;
            }

            entry = Slots[player.Slot] ??= new PlayerHud();
            return true;
        }

        private static bool CreateLayout(int slot, PlayerHud entry, ulong ownerSteamId)
        {
            if (entry.Layout is { IsValid: true } stale)
            {
                stale.AcceptInput("Kill");
            }

            entry.Layout = null;

            try
            {
                CCSCustomHudLayout? hud = Utilities.CreateEntityByName<CCSCustomHudLayout>("custom_hud_layout");
                if (hud is null || hud.Handle == IntPtr.Zero)
                {
                    PluginLog.Info($"hud: layout entity creation failed for slot {slot}");
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
                    PluginLog.Info($"hud: layout entity is invalid after spawn for slot {slot}");
                    return false;
                }

                entry.Layout = hud;
                entry.OwnerSteamId = ownerSteamId;
                entry.ClosingUntil = default;
                entry.Visible = false;
                entry.Text.Clear();
                entry.Class.Clear();
                entry.ImageClass.Clear();
                PluginLog.Debug(
                    $"hud: layout spawned slot={slot} entity=#{hud.Index} owner={ownerSteamId} resource={LayoutResource}");
                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Info($"hud: layout spawn failed for slot {slot}: {ex.Message}");
                return false;
            }
        }

        private static void Kill(int slot)
        {
            if (!PlayerSlots.IsValid(slot))
            {
                return;
            }

            PlayerHud? entry = Slots[slot];
            if (entry?.Layout is { IsValid: true } layout)
            {
                layout.AcceptInput("Kill");
            }

            Slots[slot] = null;
            ForgetQueuedSlot(slot);
        }

        /// <summary>Drops a stale spawn request when its slot is released before the queue drains.</summary>
        private static void ForgetQueuedSlot(int slot)
        {
            Queued[slot] = false;
            if (SpawnQueue.Count == 0)
            {
                return;
            }

            int count = SpawnQueue.Count;
            for (int i = 0; i < count; i++)
            {
                int queued = SpawnQueue.Dequeue();
                if (queued != slot)
                {
                    SpawnQueue.Enqueue(queued);
                }
            }
        }

        private static void ClearAll()
        {
            for (int slot = 0; slot < MaxSlots; slot++)
            {
                Slots[slot] = null;
                Queued[slot] = false;
            }

            SpawnQueue.Clear();
            _writesTick = -1;
            _writes = 0;
        }

        private static bool ShouldTransmit(PlayerHud entry) =>
            entry.Visible || DateTime.UtcNow < entry.ClosingUntil;

        private static void MarkClosing(PlayerHud entry) =>
            entry.ClosingUntil = DateTime.UtcNow + CloseTransmitGrace;

        private static void ClearClosingRoot(PlayerHud entry) => entry.ClosingUntil = default;

        /// <summary>
        /// Ends the transmit grace window. Only state is touched here: <c>CheckTransmit</c>
        /// must not issue Panorama writes, so the root classes are restored next frame.
        /// </summary>
        private static void ExpireClosingRoots()
        {
            DateTime now = DateTime.UtcNow;
            for (int slot = 0; slot < MaxSlots; slot++)
            {
                PlayerHud? entry = Slots[slot];
                if (entry is null || entry.ClosingUntil == default || now < entry.ClosingUntil)
                {
                    continue;
                }

                entry.ClosingUntil = default;
                if (entry.Layout is { IsValid: true })
                {
                    int expiredSlot = slot;
                    Server.NextFrame(() => RestoreClosedRoot(expiredSlot));
                }
            }
        }

        private static void RestoreClosedRoot(int slot)
        {
            if (!PlayerSlots.IsValid(slot) || Slots[slot] is not { Visible: false } entry
                || entry.ClosingUntil != default)
            {
                return;
            }

            if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } player)
            {
                SetHasClass(player, VoteHud.Panel, "is-off", true);
                SetHasClass(player, VoteHud.Panel, "is-fading", false);
            }
        }

        private static void ScheduleFadeIn(CCSPlayerController player)
        {
            SetHasClass(player, VoteHud.Panel, "is-fading", true);
            int slot = player.Slot;
            Server.NextFrame(() =>
            {
                if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } live && IsVisible(live))
                {
                    SetHasClass(live, VoteHud.Panel, "is-fading", false);
                }
            });
        }

        private static void RestorePlayerDefaults(CCSPlayerController player)
        {
            if (!TryEntry(player, out PlayerHud entry))
            {
                return;
            }

            bool live = entry.Layout is { IsValid: true };
            if (live)
            {
                foreach ((string panelId, Dictionary<string, bool> classes) in entry.Class)
                {
                    foreach ((string className, bool enabled) in classes.ToList())
                    {
                        // Back to the XML default: off for state classes, on for the authored-on ones
                        // (a lingering per-player is-off=false would be a ghost card after the reset).
                        bool authored = AuthoredOn.Contains(className);
                        if (enabled != authored)
                        {
                            SetHasClass(player, panelId, className, authored);
                        }
                    }
                }

                foreach ((string panelId, Dictionary<string, string> vars) in entry.Text)
                {
                    foreach ((string name, string value) in vars.Where(static kv => kv.Value.Length > 0).ToList())
                    {
                        SetText(player, panelId, name, string.Empty);
                    }
                }

                SetHasClass(player, VoteHud.Panel, "is-fading", false);
                SetHasClass(player, VoteHud.Panel, "is-off", true);
                MarkClosing(entry);
            }

            VoteHud.WriteDefaults(player);

            entry.Text.Clear();
            entry.Class.Clear();
            entry.ImageClass.Clear();
            entry.Visible = false;
        }

        private static bool TryGetCached<T>(
            Dictionary<string, Dictionary<string, T>> store,
            string panelId,
            string key,
            out T value)
        {
            value = default!;
            return store.TryGetValue(panelId, out Dictionary<string, T>? map)
                && map.TryGetValue(key, out value!);
        }

        private static void SetCached<T>(
            Dictionary<string, Dictionary<string, T>> store,
            string panelId,
            string key,
            T value)
        {
            if (!store.TryGetValue(panelId, out Dictionary<string, T>? map))
            {
                store[panelId] = map = new(StringComparer.Ordinal);
            }

            map[key] = value;
        }
    }
}
