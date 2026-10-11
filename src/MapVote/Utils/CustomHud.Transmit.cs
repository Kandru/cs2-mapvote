using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using MapVote.Huds;

namespace MapVote.Utils
{
    public static partial class CustomHud
    {
        public static void OnCheckTransmit(CCheckTransmitInfoList infoList)
        {
            ExpireClosingRoots();

            if (_layout is not { IsValid: true } layout)
            {
                return;
            }

            int index = (int)layout.Index;
            foreach ((CCheckTransmitInfo info, CCSPlayerController? viewer) in infoList)
            {
                if (!Players.IsHumanViewer(viewer))
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
                    SetHasClass(player, VoteHud.Panel, "ph-off", true);
                    SetHasClass(player, VoteHud.Panel, "ph-fading", false);
                }
            }
        }
    }
}
