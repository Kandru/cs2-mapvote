using CounterStrikeSharp.API.Core;
using MapVote.Utils;
using MapVote.Voting;

namespace MapVote.Huds
{
    public static class VoteHud
    {
        public const string Panel = "Vote";
        public const int SlotCount = 50;
        public const int CatalogCols = 10;
        public const int CatalogRowCount = 5; // ceil(SlotCount / CatalogCols)

        private const string MinimizeButtonId = "mv-minimize";

        private static VoteSession? _session;
        private static bool _revealing;
        private static string? _winner;
        private static readonly int[] LastVotedWidth = new int[65];
        private static readonly int[] LastCatalogRows = new int[65];
        private static readonly bool[] Minimized = new bool[65];

        public static void Bind(VoteSession session)
        {
            _session = session;
            _revealing = false;
            _winner = null;
            Array.Fill(LastVotedWidth, -1);
            Array.Fill(LastCatalogRows, -1);
            Array.Fill(Minimized, false);
        }

        public static void Clear()
        {
            _session = null;
            _revealing = false;
            _winner = null;
            Array.Fill(LastVotedWidth, -1);
            Array.Fill(LastCatalogRows, -1);
            Array.Fill(Minimized, false);
        }

        public static void WriteDefaults(CCSPlayerController player)
        {
            if (player.Slot is >= 0 and < LastVotedWidth.Length)
            {
                LastVotedWidth[player.Slot] = -1;
                LastCatalogRows[player.Slot] = -1;
                Minimized[player.Slot] = false;
            }

            CustomHud.SetText(player, Panel, "mv_title", "Vote for the next map");
            CustomHud.SetText(player, Panel, "mv_hint", "Cast the first vote");
            CustomHud.SetHasClass(player, Panel, "is-minimized", false);
            CustomHud.SetHasClass(player, "mv-voted-section", "has-votes", false);
            CustomHud.SetHasClass(player, "mv-voted-hint", "is-off", false);
            SetVotedWidth(player, 0);
            SetCatalogRows(player, 0);
            for (int r = 0; r < CatalogRowCount; r++)
            {
                CustomHud.SetHasClass(player, $"mv-crow-{r}", "is-off", true);
            }

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
            CustomHud.SetText(
                player,
                Panel,
                "mv_title",
                _revealing ? "Winning map" : "Vote for the next map");
            CustomHud.SetText(player, Panel, "mv_hint", "Cast the first vote");
            CustomHud.SetHasClass(player, "mv-voted-section", "has-votes", voted.Count > 0);
            CustomHud.SetHasClass(player, "mv-voted-hint", "is-off", voted.Count > 0 || _revealing);
            SetVotedWidth(player, voted.Count);
            PaintPool(player, Pool.Voted, voted, mine);

            IReadOnlyList<string> grid = _session.GridOrder();
            int rowCount = grid.Count == 0 ? 0 : (grid.Count + CatalogCols - 1) / CatalogCols;
            SetCatalogRows(player, rowCount);
            for (int r = 0; r < CatalogRowCount; r++)
            {
                CustomHud.SetHasClass(player, $"mv-crow-{r}", "is-off", r >= rowCount);
            }

            PaintPool(player, Pool.Grid, grid, mine);
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
            if (_session is null || !_session.IsOpen)
            {
                return;
            }

            if (buttonId == MinimizeButtonId)
            {
                ToggleMinimized(player);
                return;
            }

            if (_revealing
                || !TryParseButton(buttonId, out Pool pool, out int index)
                || player.AuthorizedSteamID is null)
            {
                return;
            }

            if (player.Slot is >= 0 and < Minimized.Length && Minimized[player.Slot])
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

        private static void ToggleMinimized(CCSPlayerController player)
        {
            if (player.Slot is < 0 or >= Minimized.Length)
            {
                return;
            }

            bool next = !Minimized[player.Slot];
            Minimized[player.Slot] = next;
            CustomHud.SetHasClass(player, Panel, "is-minimized", next);
        }

        public static void OnTick()
        {
        }

        private static void SetVotedWidth(CCSPlayerController player, int count)
        {
            int n = Math.Clamp(count, 0, SlotCount);
            int slot = player.Slot;
            int prev = LastVotedWidth[slot];
            if (prev == n)
            {
                return;
            }

            if (prev >= 0)
            {
                CustomHud.SetHasClass(player, "mv-voted-strip", $"vw-{prev}", false);
            }

            CustomHud.SetHasClass(player, "mv-voted-strip", $"vw-{n}", true);
            LastVotedWidth[slot] = n;
        }

        private static void SetCatalogRows(CCSPlayerController player, int rowCount)
        {
            int n = Math.Clamp(rowCount, 0, CatalogRowCount);
            int slot = player.Slot;
            int prev = LastCatalogRows[slot];
            if (prev == n)
            {
                return;
            }

            if (prev >= 0)
            {
                CustomHud.SetHasClass(player, "mv-catalog", $"rows-{prev}", false);
            }

            CustomHud.SetHasClass(player, "mv-catalog", $"rows-{n}", true);
            LastCatalogRows[slot] = n;
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
            bool isWinner = _winner is not null && _winner == map;
            bool isLost = _revealing && _winner is not null && !isWinner;
            CustomHud.SetHasClass(player, tileId, "is-off", false);
            CustomHud.SetHasClass(player, tileId, "is-self", !_revealing && mine == map);
            CustomHud.SetHasClass(player, tileId, "is-done", isWinner);
            CustomHud.SetHasClass(player, tileId, "is-lost", isLost);
            CustomHud.SetText(player, Panel, VoteVar(pool, index), votes.ToString());
            CustomHud.SetText(player, Panel, NameVar(pool, index), map);
            CustomHud.SetMapImage(player, ShotId(pool, index), map);
        }

        private static void PaintEmpty(CCSPlayerController player, Pool pool, int index)
        {
            string tileId = TileId(pool, index);
            CustomHud.SetHasClass(player, tileId, "is-off", true);
            CustomHud.SetHasClass(player, tileId, "is-self", false);
            CustomHud.SetHasClass(player, tileId, "is-done", false);
            CustomHud.SetHasClass(player, tileId, "is-lost", false);
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
                // Ignore non-tile ids under the voted prefix (e.g. mv-voted-shot-0).
                ReadOnlySpan<char> rest = buttonId.AsSpan(votedPrefix.Length);
                if (rest.IsEmpty || !char.IsAsciiDigit(rest[0]))
                {
                    return false;
                }

                pool = Pool.Voted;
                return int.TryParse(rest, out index) && index is >= 0 and < SlotCount;
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
