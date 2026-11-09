namespace MapVote.Players
{
    /// <summary>Per-player opinion of a map. Like and dislike are mutually exclusive.</summary>
    public enum PlayerRating
    {
        None = 0,
        Like = 1,
        Dislike = 2,
    }
}
