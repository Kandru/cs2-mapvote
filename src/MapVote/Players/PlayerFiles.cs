using System.Text.Json;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API.Modules.Extensions;
using MapVote.Config;

namespace MapVote.Players
{
    /// <summary>On-disk player JSON paths and (de)serialization (trimmed from cs2-challenges).</summary>
    internal static partial class PlayerFiles
    {
        public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        [GeneratedRegex("^[0-9]{17}$", RegexOptions.CultureInvariant)]
        private static partial Regex SteamId64();

        public static string DirectoryPath(PluginConfig config) =>
            Path.Combine(Path.GetDirectoryName(config.GetConfigPath()) ?? ".", "players");

        public static string FilePath(PluginConfig config, string steamId) =>
            FilePath(DirectoryPath(config), steamId);

        public static string FilePath(string directory, string steamId)
        {
            if (!TrySafeFileName(steamId, out string fileName))
            {
                throw new ArgumentException("SteamID64 required.", nameof(steamId));
            }

            return Path.Combine(directory, $"{fileName}.json");
        }

        public static bool TrySafeFileName(string steamId, out string fileName)
        {
            fileName = string.Empty;
            if (string.IsNullOrEmpty(steamId) || !SteamId64().IsMatch(steamId))
            {
                return false;
            }

            fileName = steamId;
            return true;
        }

        public static PlayerState? DeserializeBytes(ReadOnlySpan<byte> bytes)
        {
            PlayerState? state = JsonSerializer.Deserialize<PlayerState>(bytes, JsonOptions);
            if (state is null)
            {
                return null;
            }

            state.Username ??= string.Empty;
            state.SteamId ??= string.Empty;
            state.Favorites ??= [];
            return state;
        }

        public static byte[] Serialize(PlayerState state) =>
            JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);

        public static void Write(string path, PlayerState state) => WriteBytes(path, Serialize(state));

        public static void WriteBytes(string path, byte[] bytes, long version = 0) =>
            Commit(WriteTemp(path, bytes, version), path);

        public static string WriteTemp(string path, byte[] bytes, long version)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string temp = version > 0 ? $"{path}.{version}.tmp" : path + ".tmp";
            try
            {
                File.WriteAllBytes(temp, bytes);
                return temp;
            }
            catch
            {
                Discard(temp);
                throw;
            }
        }

        public static void Commit(string temp, string path)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
            }
            catch
            {
                Discard(temp);
                throw;
            }
        }

        public static void Discard(string temp)
        {
            try
            {
                File.Delete(temp);
            }
            catch
            {
            }
        }

        public static bool TryQuarantine(string path, out string? quarantinePath)
        {
            quarantinePath = path + ".bad";
            try
            {
                if (File.Exists(quarantinePath))
                {
                    quarantinePath = $"{path}.bad.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                }

                File.Move(path, quarantinePath, overwrite: false);
                return true;
            }
            catch
            {
                quarantinePath = null;
                return false;
            }
        }
    }
}
