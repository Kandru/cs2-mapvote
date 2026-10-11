namespace MapVote.Utils
{
    public static class PluginLog
    {
        public static void Info(string message) => Console.WriteLine($"[MapVote] {message}");

        public static void Debug(bool enabled, string message)
        {
            if (enabled)
            {
                Info(message);
            }
        }

        /// <summary>Always logs failures; optionally logs the rest when <paramref name="debug"/> returns true.</summary>
        public static Action<string> CatalogSink(Func<bool> debug) => message =>
        {
            if (debug()
                || message.Contains("fail", StringComparison.OrdinalIgnoreCase)
                || message.Contains("empty", StringComparison.OrdinalIgnoreCase)
                || message.Contains("truncat", StringComparison.OrdinalIgnoreCase))
            {
                Info(message);
            }
        };
    }
}
