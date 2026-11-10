namespace MapVote.Voting
{
    public sealed class VoteSession
    {
        private readonly string[] _maps;
        private readonly Dictionary<ulong, string> _votesByPlayer = [];
        private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
        private readonly List<string> _firstVoteOrder = [];
        private readonly Random _random;

        public VoteSession(IReadOnlyList<string> maps, Random? random = null)
        {
            ArgumentNullException.ThrowIfNull(maps);
            if (maps.Count == 0)
            {
                throw new ArgumentException("Vote session requires at least one map.", nameof(maps));
            }

            List<string> unique = new(maps.Count);
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (string map in maps)
            {
                if (!string.IsNullOrEmpty(map) && seen.Add(map))
                {
                    unique.Add(map);
                }
            }

            if (unique.Count == 0)
            {
                throw new ArgumentException("Vote session requires at least one map.", nameof(maps));
            }

            _maps = [.. unique];
            _random = random ?? Random.Shared;
            foreach (string map in _maps)
            {
                _counts[map] = 0;
            }
        }

        public IReadOnlyList<string> Maps => _maps;
        public bool IsOpen { get; private set; } = true;
        public string? Winner { get; private set; }

        public bool Cast(ulong playerId, string mapName)
        {
            if (!IsOpen || string.IsNullOrEmpty(mapName) || !_counts.ContainsKey(mapName))
            {
                return false;
            }

            if (_votesByPlayer.TryGetValue(playerId, out string? previous))
            {
                if (previous == mapName)
                {
                    return false;
                }

                Decrement(previous);
            }

            _votesByPlayer[playerId] = mapName;
            Increment(mapName);
            return true;
        }

        public bool RemovePlayer(ulong playerId)
        {
            if (!_votesByPlayer.Remove(playerId, out string? map))
            {
                return false;
            }

            Decrement(map);
            return true;
        }

        public string? VoteOf(ulong playerId) =>
            _votesByPlayer.TryGetValue(playerId, out string? map) ? map : null;

        public int CountOf(string mapName) =>
            _counts.TryGetValue(mapName, out int count) ? count : 0;

        /// <summary>Maps with at least one vote, highest count first (first-vote order breaks ties).</summary>
        public IReadOnlyList<string> VotedOrder()
        {
            int n = _firstVoteOrder.Count;
            if (n == 0)
            {
                return Array.Empty<string>();
            }

            if (n == 1)
            {
                return [_firstVoteOrder[0]];
            }

            List<(string Map, int Count, int Index)> ranked = new(n);
            for (int i = 0; i < n; i++)
            {
                string map = _firstVoteOrder[i];
                ranked.Add((map, _counts[map], i));
            }

            ranked.Sort(static (a, b) =>
            {
                int byCount = b.Count.CompareTo(a.Count);
                return byCount != 0 ? byCount : a.Index.CompareTo(b.Index);
            });

            return ranked.ConvertAll(static x => x.Map);
        }

        /// <summary>Full catalog order for the map grid (voted maps stay visible with overlays).</summary>
        public IReadOnlyList<string> GridOrder() => _maps;

        public void Lock()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            Winner = ResolveWinner();
        }

        private void Increment(string map)
        {
            int next = ++_counts[map];
            if (next == 1)
            {
                _firstVoteOrder.Add(map);
            }
        }

        private void Decrement(string map)
        {
            int next = Math.Max(0, _counts[map] - 1);
            _counts[map] = next;
            if (next == 0)
            {
                _firstVoteOrder.Remove(map);
            }
        }

        private string ResolveWinner()
        {
            int best = 0;
            string? winner = null;
            foreach (string map in _firstVoteOrder)
            {
                int count = _counts[map];
                if (count > best)
                {
                    best = count;
                    winner = map;
                }
            }

            if (best <= 0 || winner is null)
            {
                return _maps[_random.Next(_maps.Length)];
            }

            // First-vote order: the earliest leader stays the winner when counts tie.
            return winner;
        }
    }
}
