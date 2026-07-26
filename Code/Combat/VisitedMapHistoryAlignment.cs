namespace NinjaSlayer.Code.Combat;

internal static class VisitedMapHistoryAlignment
{
    public static int ResolveHistoryIndex(
        IReadOnlyList<int> visitedPointTypes,
        IReadOnlyList<int> historyPointTypes,
        int visitedIndex)
    {
        if (visitedIndex < 0 || visitedIndex >= visitedPointTypes.Count)
        {
            return -1;
        }

        int expectedPrefix = Math.Max(0, historyPointTypes.Count - visitedPointTypes.Count);
        int bestStart = -1;
        int bestCompared = -1;
        int bestDistance = int.MaxValue;
        for (int start = 0; start < historyPointTypes.Count; start++)
        {
            if (start + visitedIndex >= historyPointTypes.Count)
            {
                break;
            }

            int compared = Math.Min(visitedPointTypes.Count, historyPointTypes.Count - start);
            bool matches = true;
            for (int i = 0; i < compared; i++)
            {
                if (visitedPointTypes[i] != historyPointTypes[start + i])
                {
                    matches = false;
                    break;
                }
            }

            if (!matches)
            {
                continue;
            }

            int distance = Math.Abs(start - expectedPrefix);
            if (compared > bestCompared || compared == bestCompared && distance < bestDistance)
            {
                bestStart = start;
                bestCompared = compared;
                bestDistance = distance;
            }
        }

        return bestStart < 0 ? -1 : bestStart + visitedIndex;
    }
}
