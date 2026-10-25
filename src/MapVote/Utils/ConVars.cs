using CounterStrikeSharp.API.Modules.Cvars;

namespace MapVote.Utils
{
    public static class ConVars
    {
        public static int? ReadInt(string name)
        {
            ConVar? cvar = ConVar.Find(name);
            if (cvar is null)
            {
                return null;
            }

            try
            {
                return cvar.GetPrimitiveValue<int>();
            }
            catch
            {
                try
                {
                    return (int)cvar.GetPrimitiveValue<float>();
                }
                catch
                {
                    return null;
                }
            }
        }

        public static string? ReadString(string name) => ConVar.Find(name)?.StringValue;
    }
}
