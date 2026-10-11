namespace MapVote.Maps
{
    public enum MapOrigin
    {
        Local = 0,
        Workshop = 1,
    }

    public sealed record MapEntry(string Name, MapOrigin Origin);
}
