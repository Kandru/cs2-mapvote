namespace MapVote.Runtime
{
    /// <summary>
    /// Per-slot generation tokens for delayed work such as deferred HUD opens. Starting a
    /// newer action supersedes the previous one; clearing a slot invalidates work that is
    /// still waiting on a timer. Unlike a dictionary indexer increment, the first use of a
    /// slot never throws.
    /// </summary>
    public sealed class SlotActionTokens
    {
        private readonly Dictionary<int, int> _tokens = [];

        /// <summary>Starts a new action for <paramref name="slot"/> and returns its token.</summary>
        public int Next(int slot)
        {
            int token = _tokens.TryGetValue(slot, out int previous) ? previous + 1 : 1;
            _tokens[slot] = token;
            return token;
        }

        /// <summary>True while <paramref name="token"/> is still the newest action for the slot.</summary>
        public bool IsCurrent(int slot, int token) =>
            _tokens.TryGetValue(slot, out int current) && current == token;

        /// <summary>Invalidates any waiting action for one slot.</summary>
        public void Clear(int slot) => _tokens.Remove(slot);

        /// <summary>Invalidates every waiting action, for example on map end or unload.</summary>
        public void ClearAll() => _tokens.Clear();
    }
}
