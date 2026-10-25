using CounterStrikeSharp.API.Core;
using MapVote.Utils;
using MapVote.Voting;

namespace MapVote.Huds
{
    public static class VoteHud
    {
        public const string Panel = "Vote";
        public const int SlotCount = 50;

        private static VoteSession? _session;
        private static bool _revealing;
        private static string? _winner;

        public static void Bind(VoteSession session)
        {
            _session = session;
            _revealing = false;
            _winner = null;
        }

        public static void Clear()
        {
            _session = null;
            _revealing = false;
            _winner = null;
        }

        public static void WriteDefaults(CCSPlayerController player)
        {
            CustomHud.SetHasClass(player, "mv-voted-row", "is-empty", true);
            for (int i = 0; i < SlotCount; i++)
            {
                PaintEmpty(player, Pool.Voted, i);
                PaintEmpty(player, Pool.Grid, i);
            }
        }

        public static void OpenFor(CCSPlayerController player)
        {
            if (_session is null || !Players.IsHumanViewer(player))
            {
                return;
            }

            WriteDefaults(player);
            Paint(player);
            HudMenu.Open(player, new HudMenuOpenOptions { OnButton = OnButton });
        }

        public static void PaintAll()
        {
            if (_session is null)
            {
                return;
            }

            ForEachOpen(Paint);
        }

        public static void Paint(CCSPlayerController player)
        {
            if (_session is null || !Players.IsHumanViewer(player))
            {
                return;
            }

            string? mine = player.AuthorizedSteamID is { } steam
                ? _session.VoteOf(steam.SteamId64)
                : null;

            IReadOnlyList<string> voted = _session.VotedOrder();
            CustomHud.SetHasClass(player, "mv-voted-row", "is-empty", voted.Count == 0);
            PaintPool(player, Pool.Voted, voted, mine);
            PaintPool(player, Pool.Grid, _session.GridOrder(), mine);
        }

        public static void BeginReveal(string winner, int votes)
        {
            _ = votes;
            _revealing = true;
            _winner = winner;

            ForEachOpen(player =>
            {
                HudMenu.DropCapture(player);
                Paint(player);
            });
        }

        public static void CloseAll()
        {
            foreach (CCSPlayerController player in Players.GetHumans().ToList())
            {
                HudMenu.Close(player);
            }

            Clear();
        }

        public static void OnButton(CCSPlayerController player, string buttonId)
        {
            if (_session is null || !_session.IsOpen || _revealing
                || !TryParseButton(buttonId, out Pool pool, out int index)
                || player.AuthorizedSteamID is null)
            {
                return;
            }

            IReadOnlyList<string> order = pool == Pool.Voted
                ? _session.VotedOrder()
                : _session.GridOrder();
            if (index >= order.Count)
            {
                return;
            }

            if (_session.Cast(player.AuthorizedSteamID.SteamId64, order[index]))
            {
                PaintAll();
            }
        }

        public static void OnTick()
        {
        }

        private static void PaintPool(
            CCSPlayerController player,
            Pool pool,
            IReadOnlyList<string> order,
            string? mine)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (i >= order.Count)
                {
                    PaintEmpty(player, pool, i);
                    continue;
                }

                string map = order[i];
                PaintTile(player, pool, i, map, _session!.CountOf(map), mine);
            }
        }

        private static void PaintTile(
            CCSPlayerController player,
            Pool pool,
            int index,
            string map,
            int votes,
            string? mine)
        {
            string tileId = TileId(pool, index);
            CustomHud.SetHasClass(player, tileId, "is-empty", false);
            CustomHud.SetHasClass(player, tileId, "is-mine", mine == map);
            CustomHud.SetHasClass(player, tileId, "is-winner", _winner == map);
            CustomHud.SetText(player, Panel, VoteVar(pool, index), votes.ToString());
            CustomHud.SetText(player, Panel, NameVar(pool, index), map);
            CustomHud.SetMapImage(player, ShotId(pool, index), map);
        }

        private static void PaintEmpty(CCSPlayerController player, Pool pool, int index)
        {
            string tileId = TileId(pool, index);
            CustomHud.SetHasClass(player, tileId, "is-empty", true);
            CustomHud.SetHasClass(player, tileId, "is-mine", false);
            CustomHud.SetHasClass(player, tileId, "is-winner", false);
            CustomHud.SetText(player, Panel, VoteVar(pool, index), string.Empty);
            CustomHud.SetText(player, Panel, NameVar(pool, index), string.Empty);
            CustomHud.SetMapImage(player, ShotId(pool, index), null);
        }

        private static string TileId(Pool pool, int index) =>
            pool == Pool.Voted ? $"mv-voted-{index}" : $"mv-{index}";

        private static string ShotId(Pool pool, int index) =>
            pool == Pool.Voted ? $"mv-voted-shot-{index}" : $"mv-shot-{index}";

        private static string VoteVar(Pool pool, int index) =>
            pool == Pool.Voted ? $"vv{index}" : $"v{index}";

        private static string NameVar(Pool pool, int index) =>
            pool == Pool.Voted ? $"vn{index}" : $"n{index}";

        private static bool TryParseButton(string buttonId, out Pool pool, out int index)
        {
            pool = Pool.Grid;
            index = -1;

            const string votedPrefix = "mv-voted-";
            if (buttonId.StartsWith(votedPrefix, StringComparison.Ordinal))
            {
                pool = Pool.Voted;
                return int.TryParse(buttonId.AsSpan(votedPrefix.Length), out index)
                    && index is >= 0 and < SlotCount;
            }

            if (!buttonId.StartsWith("mv-", StringComparison.Ordinal)
                || buttonId.Length <= 3
                || !char.IsAsciiDigit(buttonId[3]))
            {
                return false;
            }

            return int.TryParse(buttonId.AsSpan(3), out index)
                && index is >= 0 and < SlotCount;
        }

        private static void ForEachOpen(Action<CCSPlayerController> action)
        {
            foreach (CCSPlayerController player in Players.GetHumans())
            {
                if (HudMenu.IsOpen(player))
                {
                    action(player);
                }
            }
        }

        private enum Pool
        {
            Voted,
            Grid,
        }
    }
}
