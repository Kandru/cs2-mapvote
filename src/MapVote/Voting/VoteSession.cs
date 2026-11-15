using MapVote.Runtime;

namespace MapVote.Voting
{
    public sealed class VoteSession
    {
        /// <summary>Minimum time between two successful casts by the same player.</summary>
        public static readonly TimeSpan ChangeCooldown = GameConstants.VoteChangeCooldown;

        private readonly string[] _maps;
        private readonly Dictionary<ulong, string> _votesByPlayer = [];
        private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
        private readonly Dictionary<ulong, DateTimeOffset> _lastCastUtc = [];
        private readonly List<string> _firstVoteOrder = [];
        private readonly Random _random;
        private readonly TimeProvider _time;

        public VoteSession(IReadOnlyList<string> maps, Random? random = null, TimeProvider? timeProvider = null)
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
            _time = timeProvider ?? TimeProvider.System;
            foreach (string map in _maps)
            {
                _counts[map] = 0;
            }
        }

        public IReadOnlyList<string> Maps => _maps;
        public bool IsOpen { get; private set; } = true;
        public string? Winner { get; private set; }

        public VoteCastResult Cast(ulong playerId, string mapName)
        {
            if (!CanAccept(mapName))
            {
                return VoteCastResult.Rejected;
            }

            if (!_votesByPlayer.TryGetValue(playerId, out string? previous))
            {
                SetVote(playerId, mapName, null);
                _lastCastUtc[playerId] = _time.GetUtcNow();
                return VoteCastResult.Accepted;
            }

            if (previous == mapName)
            {
                return VoteCastResult.Unchanged;
            }

            DateTimeOffset now = _time.GetUtcNow();
            // A carried vote has no timestamp and stays changeable immediately.
            if (now - _lastCastUtc.GetValueOrDefault(playerId) < ChangeCooldown)
            {
                return VoteCastResult.RateLimited;
            }

            SetVote(playerId, mapName, previous);
            _lastCastUtc[playerId] = now;
            return VoteCastResult.Changed;
        }

        public bool RemovePlayer(ulong playerId)
        {
            if (!_votesByPlayer.Remove(playerId, out string? map))
            {
                return false;
            }

            _lastCastUtc.Remove(playerId);
            Decrement(map);
            return true;
        }

        public string? VoteOf(ulong playerId) =>
            _votesByPlayer.TryGetValue(playerId, out string? map) ? map : null;

        public int CountOf(string mapName) =>
            _counts.TryGetValue(mapName, out int count) ? count : 0;

        /// <summary>
        /// Copies every vote this session holds into <paramref name="target"/>.
        /// Votes for maps the target does not offer are skipped, and the target's per-player
        /// change cooldown does not apply: carried votes are not player clicks.
        /// Returns the number of copied votes.
        /// </summary>
        public int CopyVotesTo(VoteSession target)
        {
            ArgumentNullException.ThrowIfNull(target);
            int copied = 0;
            foreach ((ulong playerId, string map) in _votesByPlayer)
            {
                if (!target.CanAccept(map))
                {
                    continue;
                }

                string? previous = target.VoteOf(playerId);
                if (previous == map)
                {
                    continue;
                }

                target.SetVote(playerId, map, previous);
                copied++;
            }

            return copied;
        }

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

        private bool CanAccept(string mapName) =>
            IsOpen && !string.IsNullOrEmpty(mapName) && _counts.ContainsKey(mapName);

        private void SetVote(ulong playerId, string mapName, string? previous)
        {
            if (previous is not null)
            {
                Decrement(previous);
            }

            _votesByPlayer[playerId] = mapName;
            Increment(mapName);
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
