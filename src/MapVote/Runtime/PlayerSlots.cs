using CounterStrikeSharp.API;

namespace MapVote.Runtime
{
    /// <summary>
    /// The single source of truth for player-slot bounds. CS2 slots are zero-based and the
    /// usable range comes from <see cref="Server.MaxPlayers"/>, never a hard-coded 65.
    /// </summary>
    public static class PlayerSlots
    {
        /// <summary>Used when the engine is not available (unit tests, early type load).</summary>
        public const int FallbackCapacity = 64;

        private static int? _capacity;

        /// <summary>Number of usable slots for this server. Stable for the process lifetime.</summary>
        public static int Capacity => _capacity ??= ReadCapacity();

        public static bool IsValid(int slot) => IsValid(slot, Capacity);

        /// <summary>Pure bounds check, exposed for tests and callers with an explicit capacity.</summary>
        internal static bool IsValid(int slot, int capacity) => slot >= 0 && slot < capacity;

        private static int ReadCapacity()
        {
            try
            {
                int maxPlayers = Server.MaxPlayers;
                return maxPlayers > 0 ? maxPlayers : FallbackCapacity;
            }
            catch
            {
                return FallbackCapacity;
            }
        }
    }
}
