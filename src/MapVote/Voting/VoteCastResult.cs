namespace MapVote.Voting
{
    /// <summary>Outcome of <see cref="VoteSession.Cast"/>.</summary>
    public enum VoteCastResult
    {
        /// <summary>The vote is closed, the map is unknown, or the map name is empty.</summary>
        Rejected,

        /// <summary>The player already voted for that map.</summary>
        Unchanged,

        /// <summary>The player changed their vote less than <see cref="VoteSession.ChangeCooldown"/> ago.</summary>
        RateLimited,

        /// <summary>The player's first vote; counted.</summary>
        Accepted,

        /// <summary>The player switched from another map; counted.</summary>
        Changed,
    }
}
