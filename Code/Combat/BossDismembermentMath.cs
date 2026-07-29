namespace NinjaSlayer.Code.Combat;

internal readonly record struct BossFragmentPoint(float X, float Y);

internal readonly record struct BossFragmentRect(float X, float Y, float Width, float Height);

internal readonly record struct BossFragmentBoundsCalibration(
    float UniformScale,
    float TranslationX,
    float TranslationY,
    float RawWidthRatio,
    float RawHeightRatio,
    float CorrectedWidthRatio,
    float CorrectedHeightRatio)
{
    public bool IsIdentity => MathF.Abs(UniformScale - 1f) <= 0.0001f
        && MathF.Abs(TranslationX) <= 0.001f
        && MathF.Abs(TranslationY) <= 0.001f;
}

internal sealed record BossFragmentCell(
    BossFragmentPoint Seed,
    IReadOnlyList<BossFragmentPoint> Vertices)
{
    public float Area => BossDismembermentMath.PolygonArea(Vertices);
    public BossFragmentPoint Centroid => BossDismembermentMath.PolygonCentroid(Vertices);
}

internal readonly record struct BossFragmentLink(
    int FirstIndex,
    int SecondIndex);

internal static class BossDismembermentMath
{
    public const int MaximumPieces = 16;
    private const int CandidateCount = 128;

    public static IReadOnlyList<BossFragmentLink> BuildRagdollLinks(
        IReadOnlyList<BossFragmentPoint> points,
        int maximumClusterSize = 3)
    {
        int count = Math.Min(points.Count, MaximumPieces);
        if (count < 2)
        {
            return [];
        }

        var connected = new HashSet<int> { 0 };
        var candidates = new List<BossFragmentLink>(count - 1);
        while (connected.Count < count)
        {
            int bestFirst = -1;
            int bestSecond = -1;
            float bestDistance = float.PositiveInfinity;
            foreach (int first in connected.Order())
            {
                for (int second = 0; second < count; second++)
                {
                    if (connected.Contains(second))
                    {
                        continue;
                    }

                    float dx = points[second].X - points[first].X;
                    float dy = points[second].Y - points[first].Y;
                    float distance = MathF.Sqrt(dx * dx + dy * dy);
                    if (distance < bestDistance
                        || (MathF.Abs(distance - bestDistance) <= 0.001f
                            && (first < bestFirst
                                || (first == bestFirst && second < bestSecond))))
                    {
                        bestFirst = first;
                        bestSecond = second;
                        bestDistance = distance;
                    }
                }
            }

            candidates.Add(new BossFragmentLink(bestFirst, bestSecond));
            connected.Add(bestSecond);
        }

        maximumClusterSize = Math.Clamp(maximumClusterSize, 2, count);
        int[] parents = Enumerable.Range(0, count).ToArray();
        int[] sizes = Enumerable.Repeat(1, count).ToArray();
        var links = new List<BossFragmentLink>(count - 1);
        foreach (BossFragmentLink candidate in candidates)
        {
            int firstRoot = FindRoot(parents, candidate.FirstIndex);
            int secondRoot = FindRoot(parents, candidate.SecondIndex);
            if (firstRoot == secondRoot
                || sizes[firstRoot] + sizes[secondRoot] > maximumClusterSize)
            {
                continue;
            }

            parents[secondRoot] = firstRoot;
            sizes[firstRoot] += sizes[secondRoot];
            links.Add(candidate);
        }

        return links;
    }

    public static float ResolveCollisionPadding(float visibleArea)
    {
        if (!float.IsFinite(visibleArea) || visibleArea <= 0f)
        {
            return 18f;
        }

        return Math.Clamp(MathF.Sqrt(visibleArea) * 0.08f, 18f, 42f);
    }

    public static ulong ResolveMotionSeed(
        ulong snapshotSeed,
        ulong runtimeEntropy,
        ulong presentationInstanceId) =>
        snapshotSeed
        ^ runtimeEntropy
        ^ (presentationInstanceId * 0x9E3779B97F4A7C15UL);

    public static ulong StableHash64(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        ulong hash = 14695981039346656037UL;
        foreach (char character in value)
        {
            hash ^= character;
            hash = unchecked(hash * 1099511628211UL);
        }

        return hash;
    }

    public static bool TryResolveUniformBoundsCalibration(
        BossFragmentRect expected,
        BossFragmentRect actual,
        out BossFragmentBoundsCalibration calibration)
    {
        calibration = default;
        if (!IsValidBounds(expected) || !IsValidBounds(actual))
        {
            return false;
        }

        float rawWidthRatio = actual.Width / expected.Width;
        float rawHeightRatio = actual.Height / expected.Height;
        if (!float.IsFinite(rawWidthRatio)
            || !float.IsFinite(rawHeightRatio)
            || rawWidthRatio is < 0.95f or > 1.05f
            || rawHeightRatio is < 0.95f or > 1.05f)
        {
            return false;
        }

        float uniformScale;
        float correctedWidthRatio;
        float correctedHeightRatio;
        if (rawWidthRatio is >= 0.98f and <= 1.02f
            && rawHeightRatio is >= 0.98f and <= 1.02f)
        {
            uniformScale = 1f;
            correctedWidthRatio = rawWidthRatio;
            correctedHeightRatio = rawHeightRatio;
        }
        else
        {
            float widthScale = expected.Width / actual.Width;
            float heightScale = expected.Height / actual.Height;
            uniformScale = MathF.Sqrt(widthScale * heightScale);
            correctedWidthRatio = rawWidthRatio * uniformScale;
            correctedHeightRatio = rawHeightRatio * uniformScale;
            if (!float.IsFinite(uniformScale)
                || uniformScale <= 0f
                || correctedWidthRatio is < 0.98f or > 1.02f
                || correctedHeightRatio is < 0.98f or > 1.02f)
            {
                return false;
            }
        }

        float expectedCenterX = expected.X + expected.Width * 0.5f;
        float expectedCenterY = expected.Y + expected.Height * 0.5f;
        float actualCenterX = actual.X + actual.Width * 0.5f;
        float actualCenterY = actual.Y + actual.Height * 0.5f;
        float translationX = expectedCenterX - actualCenterX * uniformScale;
        float translationY = expectedCenterY - actualCenterY * uniformScale;
        if (!float.IsFinite(translationX) || !float.IsFinite(translationY))
        {
            return false;
        }

        calibration = new BossFragmentBoundsCalibration(
            uniformScale,
            translationX,
            translationY,
            rawWidthRatio,
            rawHeightRatio,
            correctedWidthRatio,
            correctedHeightRatio);
        return true;
    }

    public static bool ConvexPolygonsOverlap(
        IReadOnlyList<BossFragmentPoint> first,
        IReadOnlyList<BossFragmentPoint> second,
        float separationEpsilon = 0.001f)
    {
        if (first.Count < 3
            || second.Count < 3
            || first.Any(point => !IsFinite(point))
            || second.Any(point => !IsFinite(point)))
        {
            return false;
        }

        separationEpsilon = Math.Max(0f, separationEpsilon);
        return HasNoSeparatingAxis(first, second, separationEpsilon)
            && HasNoSeparatingAxis(second, first, separationEpsilon);
    }

    public static IReadOnlyList<BossFragmentPoint> BuildConvexHull(
        IReadOnlyList<BossFragmentPoint> points)
    {
        BossFragmentPoint[] sorted = points
            .Where(IsFinite)
            .Distinct()
            .OrderBy(point => point.X)
            .ThenBy(point => point.Y)
            .ToArray();
        if (sorted.Length <= 2)
        {
            return sorted;
        }

        var lower = new List<BossFragmentPoint>(sorted.Length);
        foreach (BossFragmentPoint point in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], point) <= 0f)
            {
                lower.RemoveAt(lower.Count - 1);
            }

            lower.Add(point);
        }

        var upper = new List<BossFragmentPoint>(sorted.Length);
        for (int index = sorted.Length - 1; index >= 0; index--)
        {
            BossFragmentPoint point = sorted[index];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], point) <= 0f)
            {
                upper.RemoveAt(upper.Count - 1);
            }

            upper.Add(point);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    public static IReadOnlyList<BossFragmentCell> BuildVoronoiCells(
        BossFragmentRect bounds,
        int requestedCount,
        ulong seed)
    {
        int count = Math.Clamp(requestedCount, 1, MaximumPieces);
        if (bounds.Width <= 1f || bounds.Height <= 1f)
        {
            return [];
        }

        IReadOnlyList<BossFragmentPoint> seeds = BuildDistributedSeeds(bounds, count, seed);
        return BuildVoronoiCells(bounds, seeds);
    }

    public static IReadOnlyList<BossFragmentCell> BuildVoronoiCells(
        BossFragmentRect bounds,
        IReadOnlyList<BossFragmentPoint> requestedSeeds)
    {
        if (bounds.Width <= 1f || bounds.Height <= 1f)
        {
            return [];
        }

        BossFragmentPoint[] polygon =
        [
            new(bounds.X, bounds.Y),
            new(bounds.X + bounds.Width, bounds.Y),
            new(bounds.X + bounds.Width, bounds.Y + bounds.Height),
            new(bounds.X, bounds.Y + bounds.Height)
        ];
        return BuildVoronoiCells(polygon, requestedSeeds);
    }

    public static IReadOnlyList<BossFragmentCell> BuildVoronoiCells(
        IReadOnlyList<BossFragmentPoint> convexBounds,
        int requestedCount,
        ulong seed)
    {
        int count = Math.Clamp(requestedCount, 1, MaximumPieces);
        if (convexBounds.Count < 3 || PolygonArea(convexBounds) <= 1f)
        {
            return [];
        }

        IReadOnlyList<BossFragmentPoint> seeds = BuildDistributedSeeds(
            convexBounds,
            count,
            seed);
        return BuildVoronoiCells(convexBounds, seeds);
    }

    public static IReadOnlyList<BossFragmentCell> BuildVoronoiCells(
        IReadOnlyList<BossFragmentPoint> convexBounds,
        IReadOnlyList<BossFragmentPoint> requestedSeeds)
    {
        if (convexBounds.Count < 3 || PolygonArea(convexBounds) <= 1f)
        {
            return [];
        }

        BossFragmentRect bounds = BoundsOf(convexBounds);
        BossFragmentPoint[] seeds = requestedSeeds
            .Take(MaximumPieces)
            .Where(seedPoint => IsFinite(seedPoint) && ContainsPoint(convexBounds, seedPoint))
            .ToArray();
        if (seeds.Length == 0)
        {
            return [];
        }

        var cells = new List<BossFragmentCell>(seeds.Length);
        for (int i = 0; i < seeds.Length; i++)
        {
            BossFragmentPoint seedPoint = seeds[i];
            List<BossFragmentPoint> polygon = [.. convexBounds];

            for (int j = 0; j < seeds.Length && polygon.Count > 0; j++)
            {
                if (i == j)
                {
                    continue;
                }

                BossFragmentPoint other = seeds[j];
                float normalX = other.X - seedPoint.X;
                float normalY = other.Y - seedPoint.Y;
                float limit = (other.X * other.X + other.Y * other.Y
                    - seedPoint.X * seedPoint.X - seedPoint.Y * seedPoint.Y) * 0.5f;
                polygon = ClipToHalfPlane(polygon, normalX, normalY, limit);
            }

            if (polygon.Count >= 3 && PolygonArea(polygon) > 1f)
            {
                cells.Add(new BossFragmentCell(seedPoint, polygon));
            }
        }

        return cells;
    }

    public static BossFragmentRect BoundsOf(IReadOnlyList<BossFragmentPoint> points)
    {
        if (points.Count == 0)
        {
            return default;
        }

        float minX = points[0].X;
        float minY = points[0].Y;
        float maxX = minX;
        float maxY = minY;
        for (int index = 1; index < points.Count; index++)
        {
            minX = Math.Min(minX, points[index].X);
            minY = Math.Min(minY, points[index].Y);
            maxX = Math.Max(maxX, points[index].X);
            maxY = Math.Max(maxY, points[index].Y);
        }

        return new BossFragmentRect(minX, minY, maxX - minX, maxY - minY);
    }

    internal static float PolygonArea(IReadOnlyList<BossFragmentPoint> polygon)
    {
        if (polygon.Count < 3)
        {
            return 0f;
        }

        double twiceArea = 0d;
        for (int i = 0; i < polygon.Count; i++)
        {
            BossFragmentPoint current = polygon[i];
            BossFragmentPoint next = polygon[(i + 1) % polygon.Count];
            twiceArea += current.X * next.Y - next.X * current.Y;
        }

        return (float)Math.Abs(twiceArea * 0.5d);
    }

    internal static BossFragmentPoint PolygonCentroid(IReadOnlyList<BossFragmentPoint> polygon)
    {
        if (polygon.Count == 0)
        {
            return default;
        }

        double crossSum = 0d;
        double xSum = 0d;
        double ySum = 0d;
        for (int i = 0; i < polygon.Count; i++)
        {
            BossFragmentPoint current = polygon[i];
            BossFragmentPoint next = polygon[(i + 1) % polygon.Count];
            double cross = current.X * next.Y - next.X * current.Y;
            crossSum += cross;
            xSum += (current.X + next.X) * cross;
            ySum += (current.Y + next.Y) * cross;
        }

        if (Math.Abs(crossSum) <= 0.0001d)
        {
            return new BossFragmentPoint(
                polygon.Average(point => point.X),
                polygon.Average(point => point.Y));
        }

        double divisor = 3d * crossSum;
        return new BossFragmentPoint((float)(xSum / divisor), (float)(ySum / divisor));
    }

    private static IReadOnlyList<BossFragmentPoint> BuildDistributedSeeds(
        BossFragmentRect bounds,
        int count,
        ulong seed)
    {
        var candidates = new List<BossFragmentPoint>(CandidateCount + 1)
        {
            new(bounds.X + bounds.Width * 0.5f, bounds.Y + bounds.Height * 0.5f)
        };
        int offset = (int)(Mix(seed) % 997UL) + 1;
        for (int i = 0; i < CandidateCount; i++)
        {
            int index = offset + i;
            float x = 0.06f + RadicalInverse(index, 2) * 0.88f;
            float y = 0.06f + RadicalInverse(index, 3) * 0.88f;
            candidates.Add(new BossFragmentPoint(
                bounds.X + bounds.Width * x,
                bounds.Y + bounds.Height * y));
        }

        var selected = new List<BossFragmentPoint>(count) { candidates[0] };
        var used = new HashSet<int> { 0 };
        while (selected.Count < count)
        {
            int bestIndex = -1;
            float bestDistance = float.NegativeInfinity;
            for (int i = 1; i < candidates.Count; i++)
            {
                if (used.Contains(i))
                {
                    continue;
                }

                BossFragmentPoint candidate = candidates[i];
                float nearest = selected.Min(point => NormalizedDistanceSquared(candidate, point, bounds));
                if (nearest > bestDistance)
                {
                    bestDistance = nearest;
                    bestIndex = i;
                }
            }

            used.Add(bestIndex);
            selected.Add(candidates[bestIndex]);
        }

        return selected;
    }

    private static IReadOnlyList<BossFragmentPoint> BuildDistributedSeeds(
        IReadOnlyList<BossFragmentPoint> convexBounds,
        int count,
        ulong seed)
    {
        BossFragmentRect bounds = BoundsOf(convexBounds);
        BossFragmentPoint centroid = PolygonCentroid(convexBounds);
        var candidates = new List<BossFragmentPoint>(CandidateCount + 1) { centroid };
        int offset = (int)(Mix(seed) % 997UL) + 1;
        for (int index = 0; index < CandidateCount; index++)
        {
            int sequence = offset + index;
            BossFragmentPoint candidate = new(
                bounds.X + bounds.Width * RadicalInverse(sequence, 2),
                bounds.Y + bounds.Height * RadicalInverse(sequence, 3));
            if (ContainsPoint(convexBounds, candidate))
            {
                candidates.Add(candidate);
            }
        }

        if (candidates.Count < count)
        {
            for (int index = 0; index < convexBounds.Count; index++)
            {
                BossFragmentPoint candidate = new(
                    (convexBounds[index].X + centroid.X) * 0.5f,
                    (convexBounds[index].Y + centroid.Y) * 0.5f);
                if (ContainsPoint(convexBounds, candidate))
                {
                    candidates.Add(candidate);
                }
            }
        }

        count = Math.Min(count, candidates.Count);
        var selected = new List<BossFragmentPoint>(count) { candidates[0] };
        var used = new HashSet<int> { 0 };
        while (selected.Count < count)
        {
            int bestIndex = -1;
            float bestDistance = float.NegativeInfinity;
            for (int index = 1; index < candidates.Count; index++)
            {
                if (used.Contains(index))
                {
                    continue;
                }

                float nearest = selected.Min(point =>
                    NormalizedDistanceSquared(candidates[index], point, bounds));
                if (nearest > bestDistance)
                {
                    bestDistance = nearest;
                    bestIndex = index;
                }
            }

            if (bestIndex < 0)
            {
                break;
            }

            used.Add(bestIndex);
            selected.Add(candidates[bestIndex]);
        }

        return selected;
    }

    private static bool ContainsPoint(
        IReadOnlyList<BossFragmentPoint> convexPolygon,
        BossFragmentPoint point)
    {
        float orientation = 0f;
        for (int index = 0; index < convexPolygon.Count; index++)
        {
            BossFragmentPoint first = convexPolygon[index];
            BossFragmentPoint second = convexPolygon[(index + 1) % convexPolygon.Count];
            float cross = (second.X - first.X) * (point.Y - first.Y)
                - (second.Y - first.Y) * (point.X - first.X);
            if (MathF.Abs(cross) <= 0.001f)
            {
                continue;
            }

            float sign = MathF.Sign(cross);
            if (orientation == 0f)
            {
                orientation = sign;
            }
            else if (sign != orientation)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsFinite(BossFragmentPoint point) =>
        float.IsFinite(point.X) && float.IsFinite(point.Y);

    private static bool HasNoSeparatingAxis(
        IReadOnlyList<BossFragmentPoint> axesSource,
        IReadOnlyList<BossFragmentPoint> other,
        float separationEpsilon)
    {
        for (int index = 0; index < axesSource.Count; index++)
        {
            BossFragmentPoint first = axesSource[index];
            BossFragmentPoint second = axesSource[(index + 1) % axesSource.Count];
            float edgeX = second.X - first.X;
            float edgeY = second.Y - first.Y;
            float length = MathF.Sqrt(edgeX * edgeX + edgeY * edgeY);
            if (length <= 0.0001f)
            {
                continue;
            }

            float axisX = -edgeY / length;
            float axisY = edgeX / length;
            Project(axesSource, axisX, axisY, out float sourceMinimum, out float sourceMaximum);
            Project(other, axisX, axisY, out float otherMinimum, out float otherMaximum);
            if (MathF.Min(sourceMaximum, otherMaximum)
                - MathF.Max(sourceMinimum, otherMinimum) <= separationEpsilon)
            {
                return false;
            }
        }

        return true;
    }

    private static void Project(
        IReadOnlyList<BossFragmentPoint> points,
        float axisX,
        float axisY,
        out float minimum,
        out float maximum)
    {
        minimum = Dot(points[0], axisX, axisY);
        maximum = minimum;
        for (int index = 1; index < points.Count; index++)
        {
            float projection = Dot(points[index], axisX, axisY);
            minimum = Math.Min(minimum, projection);
            maximum = Math.Max(maximum, projection);
        }
    }

    private static bool IsValidBounds(BossFragmentRect bounds) =>
        float.IsFinite(bounds.X)
        && float.IsFinite(bounds.Y)
        && float.IsFinite(bounds.Width)
        && float.IsFinite(bounds.Height)
        && bounds.Width > 1f
        && bounds.Height > 1f;

    private static List<BossFragmentPoint> ClipToHalfPlane(
        IReadOnlyList<BossFragmentPoint> polygon,
        float normalX,
        float normalY,
        float limit)
    {
        var output = new List<BossFragmentPoint>(polygon.Count + 1);
        BossFragmentPoint previous = polygon[^1];
        float previousDistance = Dot(previous, normalX, normalY) - limit;
        bool previousInside = previousDistance <= 0.001f;
        foreach (BossFragmentPoint current in polygon)
        {
            float currentDistance = Dot(current, normalX, normalY) - limit;
            bool currentInside = currentDistance <= 0.001f;
            if (currentInside != previousInside)
            {
                float denominator = previousDistance - currentDistance;
                float amount = Math.Abs(denominator) <= 0.0001f
                    ? 0f
                    : previousDistance / denominator;
                output.Add(new BossFragmentPoint(
                    previous.X + (current.X - previous.X) * amount,
                    previous.Y + (current.Y - previous.Y) * amount));
            }

            if (currentInside)
            {
                output.Add(current);
            }

            previous = current;
            previousDistance = currentDistance;
            previousInside = currentInside;
        }

        return output;
    }

    private static float Dot(BossFragmentPoint point, float x, float y) => point.X * x + point.Y * y;

    private static float Cross(
        BossFragmentPoint origin,
        BossFragmentPoint first,
        BossFragmentPoint second) =>
        (first.X - origin.X) * (second.Y - origin.Y)
        - (first.Y - origin.Y) * (second.X - origin.X);

    private static float NormalizedDistanceSquared(
        BossFragmentPoint first,
        BossFragmentPoint second,
        BossFragmentRect bounds)
    {
        float dx = (first.X - second.X) / bounds.Width;
        float dy = (first.Y - second.Y) / bounds.Height;
        return dx * dx + dy * dy;
    }

    private static float RadicalInverse(int value, int radix)
    {
        float inverse = 1f / radix;
        float factor = inverse;
        float result = 0f;
        while (value > 0)
        {
            result += value % radix * factor;
            value /= radix;
            factor *= inverse;
        }

        return result;
    }

    private static ulong Mix(ulong value)
    {
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    private static int FindRoot(int[] parents, int index)
    {
        while (parents[index] != index)
        {
            parents[index] = parents[parents[index]];
            index = parents[index];
        }

        return index;
    }

}
