namespace MapVote.Utils
{
    public static class PluginLog
    {
        private static Func<bool>? _debugEnabled;

        public static void Bind(Func<bool> debugEnabled) => _debugEnabled = debugEnabled;

        public static void Info(string message) => Console.WriteLine($"[MapVote] {message}");

        public static void Debug(string message)
        {
            if (_debugEnabled?.Invoke() == true)
            {
                Console.WriteLine($"[MapVote] debug: {message}");
            }
        }

        /// <summary>Always logs failures, empty password, and truncation; routine catalog noise is debug-only.</summary>
        public static Action<string> CatalogSink() => message =>
        {
            if (message.Contains("fail", StringComparison.OrdinalIgnoreCase)
                || message.Contains("rcon_password", StringComparison.OrdinalIgnoreCase)
                || message.Contains("truncat", StringComparison.OrdinalIgnoreCase))
            {
                Info(message);
                return;
            }

            Debug(message);
        };
    }
}
