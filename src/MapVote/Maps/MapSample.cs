namespace MapVote.Maps
{
    public static class MapSample
    {
        /// <summary>
        /// At most <paramref name="limit"/> uniformly random entries, in their original relative order.
        /// </summary>
        public static List<MapEntry> Take(List<MapEntry> maps, int limit, Random random)
        {
            limit = Math.Max(1, limit);
            if (maps.Count <= limit)
            {
                return maps;
            }

            // Partial Fisher-Yates over the indices, then restore the original order.
            int[] indices = new int[maps.Count];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }

            for (int i = 0; i < limit; i++)
            {
                int j = random.Next(i, indices.Length);
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }

            Array.Sort(indices, 0, limit);
            List<MapEntry> sample = new(limit);
            for (int i = 0; i < limit; i++)
            {
                sample.Add(maps[indices[i]]);
            }

            return sample;
        }
    }
}
