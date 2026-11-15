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

        /// <summary>
        /// At most <paramref name="limit"/> entries, always keeping the named priority entries
        /// (in priority order) before randomly filling the remaining slots. The result keeps the
        /// original relative order of <paramref name="maps"/>.
        /// </summary>
        public static List<MapEntry> TakeWithPriority(
            List<MapEntry> maps,
            IReadOnlyList<string> priorityNames,
            int limit,
            Random random)
        {
            limit = Math.Max(1, limit);
            if (maps.Count <= limit)
            {
                return maps;
            }

            if (priorityNames.Count == 0)
            {
                return Take(maps, limit, random);
            }

            Dictionary<string, int> indexOf = new(maps.Count, StringComparer.Ordinal);
            for (int i = 0; i < maps.Count; i++)
            {
                indexOf.TryAdd(maps[i].Name, i);
            }

            // Every priority map that exists in the source takes a slot first.
            bool[] selected = new bool[maps.Count];
            int selectedCount = 0;
            foreach (string name in priorityNames)
            {
                if (selectedCount >= limit)
                {
                    break;
                }

                if (indexOf.TryGetValue(name, out int index) && !selected[index])
                {
                    selected[index] = true;
                    selectedCount++;
                }
            }

            // Fill the remaining slots from the maps that are not already selected.
            List<int> remaining = new(maps.Count - selectedCount);
            for (int i = 0; i < maps.Count; i++)
            {
                if (!selected[i])
                {
                    remaining.Add(i);
                }
            }

            for (int i = 0; i < remaining.Count && selectedCount < limit; i++)
            {
                int j = random.Next(i, remaining.Count);
                (remaining[i], remaining[j]) = (remaining[j], remaining[i]);
                selected[remaining[i]] = true;
                selectedCount++;
            }

            List<int> ordered = new(selectedCount);
            for (int i = 0; i < selected.Length; i++)
            {
                if (selected[i])
                {
                    ordered.Add(i);
                }
            }

            return ordered.ConvertAll(index => maps[index]);
        }
    }
}
