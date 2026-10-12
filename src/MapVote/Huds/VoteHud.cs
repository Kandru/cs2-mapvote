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
        private static bool _tickSide;
        private static int _tickCounter;
        private static bool _bumpSide;
        private static readonly string?[] LastStripMaps = new string?[SlotCount];
        private static readonly int[] LastStripCounts = new int[SlotCount];
        private static readonly bool[] StripBump = new bool[SlotCount];

        public static void Bind(VoteSession session)
        {
            _session = session;
            _revealing = false;
            _tickSide = false;
            _tickCounter = 0;
            ClearStripSnapshot();
        }

        public static void Clear()
        {
            _session = null;
            _revealing = false;
            ClearStripSnapshot();
        }

        public static void WriteDefaults(CCSPlayerController player)
        {
            CustomHud.SetText(player, Panel, "winner_name", string.Empty);
            CustomHud.SetText(player, Panel, "winner_votes", string.Empty);
            CustomHud.SetHasClass(player, Panel, "is-reveal", false);
            CustomHud.SetHasClass(player, "mv-voted-row", "is-empty", true);
            SetWinnerVisible(player, show: false);
            CustomHud.SetMapImage(player, "mv-winner-shot", null);

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

            IReadOnlyList<string> strip = _session.VotedOrder();
            PrepareStripBump(strip);
            ForEachOpen(player => PaintPrepared(player, strip));
            CommitStripSnapshot(strip);
        }

        public static void Paint(CCSPlayerController player)
        {
            if (_session is null || !Players.IsHumanViewer(player))
            {
                return;
            }

            IReadOnlyList<string> strip = _session.VotedOrder();
            PrepareStripBump(strip);
            PaintPrepared(player, strip);
            CommitStripSnapshot(strip);
        }

        public static void BeginReveal(string winner, int votes)
        {
            _revealing = true;
            _tickSide = false;
            _tickCounter = 0;

            ForEachOpen(player =>
            {
                HudMenu.DropCapture(player);
                CustomHud.SetHasClass(player, Panel, "is-reveal", true);
                SetWinnerVisible(player, show: true);
                CustomHud.SetMapImage(player, "mv-winner-shot", winner);
                CustomHud.SetText(player, Panel, "winner_name", winner);
                CustomHud.SetText(player, Panel, "winner_votes", votes.ToString());
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
            if (!_revealing || ++_tickCounter < 32)
            {
                return;
            }

            _tickCounter = 0;
            _tickSide = !_tickSide;
            ForEachOpen(player =>
            {
                CustomHud.SetHasClass(player, "mv-winner", "tick-a", _tickSide);
                CustomHud.SetHasClass(player, "mv-winner", "tick-b", !_tickSide);
            });
        }

        private static void PaintPrepared(CCSPlayerController player, IReadOnlyList<string> strip)
        {
            if (_session is null || !Players.IsHumanViewer(player))
            {
                return;
            }

            string? mine = player.AuthorizedSteamID is { } steam
                ? _session.VoteOf(steam.SteamId64)
                : null;

            CustomHud.SetHasClass(player, "mv-voted-row", "is-empty", strip.Count == 0);
            PaintPool(player, Pool.Voted, strip, mine, useBump: true);
            PaintPool(player, Pool.Grid, _session.GridOrder(), mine, useBump: false);
        }

        private static void PaintPool(
            CCSPlayerController player,
            Pool pool,
            IReadOnlyList<string> order,
            string? mine,
            bool useBump)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (i >= order.Count)
                {
                    PaintEmpty(player, pool, i);
                    continue;
                }

                string map = order[i];
                PaintTile(player, pool, i, map, _session!.CountOf(map), mine, bump: useBump && StripBump[i]);
            }
        }

        private static void PaintTile(
            CCSPlayerController player,
            Pool pool,
            int index,
            string map,
            int votes,
            string? mine,
            bool bump)
        {
            string tileId = TileId(pool, index);
            CustomHud.SetHasClass(player, tileId, "is-empty", false);
            CustomHud.SetHasClass(player, tileId, "is-voted", votes > 0);
            CustomHud.SetHasClass(player, tileId, "is-mine", mine == map);
            CustomHud.SetHasClass(player, tileId, "bump-a", bump && _bumpSide);
            CustomHud.SetHasClass(player, tileId, "bump-b", bump && !_bumpSide);
            CustomHud.SetText(player, Panel, VoteVar(pool, index), votes > 0 ? votes.ToString() : string.Empty);
            CustomHud.SetText(player, Panel, NameVar(pool, index), map);
            CustomHud.SetMapImage(player, ShotId(pool, index), map);
        }

        private static void PaintEmpty(CCSPlayerController player, Pool pool, int index)
        {
            string tileId = TileId(pool, index);
            CustomHud.SetHasClass(player, tileId, "is-empty", true);
            CustomHud.SetHasClass(player, tileId, "is-voted", false);
            CustomHud.SetHasClass(player, tileId, "is-mine", false);
            CustomHud.SetHasClass(player, tileId, "bump-a", false);
            CustomHud.SetHasClass(player, tileId, "bump-b", false);
            CustomHud.SetText(player, Panel, VoteVar(pool, index), string.Empty);
            CustomHud.SetText(player, Panel, NameVar(pool, index), string.Empty);
            CustomHud.SetMapImage(player, ShotId(pool, index), null);
        }

        private static void SetWinnerVisible(CCSPlayerController player, bool show)
        {
            CustomHud.SetHasClass(player, "mv-winner", "is-empty", !show);
            CustomHud.SetHasClass(player, "mv-winner", "tick-a", false);
            CustomHud.SetHasClass(player, "mv-winner", "tick-b", false);
        }

        private static void PrepareStripBump(IReadOnlyList<string> strip)
        {
            bool any = false;
            for (int i = 0; i < SlotCount; i++)
            {
                string? map = i < strip.Count ? strip[i] : null;
                int votes = map is null ? 0 : _session!.CountOf(map);
                bool changed = map != LastStripMaps[i] || votes != LastStripCounts[i];
                StripBump[i] = changed && map is not null;
                any |= StripBump[i];
            }

            if (any)
            {
                _bumpSide = !_bumpSide;
            }
        }

        private static void CommitStripSnapshot(IReadOnlyList<string> strip)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (i < strip.Count)
                {
                    LastStripMaps[i] = strip[i];
                    LastStripCounts[i] = _session!.CountOf(strip[i]);
                }
                else
                {
                    LastStripMaps[i] = null;
                    LastStripCounts[i] = 0;
                }
            }
        }

        private static void ClearStripSnapshot()
        {
            Array.Clear(LastStripMaps);
            Array.Clear(LastStripCounts);
            Array.Clear(StripBump);
            _bumpSide = false;
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
