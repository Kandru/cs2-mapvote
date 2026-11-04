using CounterStrikeSharp.API.Modules.Cvars;

namespace MapVote.Utils
{
    public static class ConVars
    {
        public static int? ReadInt(string name)
        {
            ConVar<int>? asInt = ConVar<int>.Find(name);
            if (asInt is not null)
            {
                return asInt.Value;
            }

            ConVar<float>? asFloat = ConVar<float>.Find(name);
            return asFloat is null ? null : (int)asFloat.Value;
        }

        public static float? ReadFloat(string name)
        {
            ConVar<float>? asFloat = ConVar<float>.Find(name);
            if (asFloat is not null)
            {
                return asFloat.Value;
            }

            ConVar<int>? asInt = ConVar<int>.Find(name);
            return asInt?.Value;
        }

        public static string? ReadString(string name) => ConVar<string>.Find(name)?.Value;
    }
}
