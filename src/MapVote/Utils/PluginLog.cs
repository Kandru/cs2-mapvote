namespace MapVote.Utils
{
    public static class PluginLog
    {
        private static Func<bool>? _debugEnabled;

        public static void Bind(Func<bool> debugEnabled) => _debugEnabled = debugEnabled;

        /// <summary>Lifecycle and user-visible outcomes: maps found, votes cast, RTV state, map changes.</summary>
        public static void Info(string message) => Console.WriteLine($"[MapVote] {message}");

        public static bool IsDebug => _debugEnabled?.Invoke() == true;

        /// <summary>Exact internal flow: attempts, slots, timers, ignored actions and per-step details.</summary>
        public static void Debug(string message)
        {
            if (IsDebug)
            {
                Console.WriteLine($"[MapVote] debug: {message}");
            }
        }

        /// <summary>Builds the message only when debug logging is on.</summary>
        public static void Debug(Func<string> message)
        {
            if (IsDebug)
            {
                Debug(message());
            }
        }
    }
}
