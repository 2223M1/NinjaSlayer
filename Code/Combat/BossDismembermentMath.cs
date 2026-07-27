namespace NinjaSlayer.Code.Combat;

internal readonly record struct BossFragmentPoint(float X, float Y);

internal readonly record struct BossFragmentRect(float X, float Y, float Width, float Height);

internal sealed record BossFragmentCell(
    BossFragmentPoint Seed,
    IReadOnlyList<BossFragmentPoint> Vertices)
{
    public float Area => BossDismembermentMath.PolygonArea(Vertices);
    public BossFragmentPoint Centroid => BossDismembermentMath.PolygonCentroid(Vertices);
}

internal readonly record struct BossFragmentLaunch(
    float VelocityX,
    float VelocityY,
    float AngularVelocityDegrees);

internal readonly record struct BossFragmentLink(
    int FirstIndex,
    int SecondIndex);

internal readonly record struct BossFragmentAllocation(
    int BodyPieces,
    int DetachedPieces);

internal static class BossDismembermentMath
{
    public const int MaximumPieces = 16;
    private const int CandidateCount = 128;

    public static int ResolvePieceCount(float width, float height, int availableParts, bool detachedPart)
    {
        if (width <= 1f || height <= 1f || availableParts <= 0)
        {
            return 0;
        }

        float metric = MathF.Sqrt(width * height);
        int minimum = detachedPart ? 3 : 8;
        int maximum = detachedPart ? 6 : MaximumPieces;
        int divisor = detachedPart ? 56 : 58;
        int desired = Math.Clamp((int)MathF.Round(metric / divisor), minimum, maximum);
        return Math.Clamp(desired, 1, Math.Min(availableParts, MaximumPieces));
    }

    public static int ResolveSpinePieceCount(int visibleSlots, bool detachedPart)
    {
        if (visibleSlots <= 0)
        {
            return 0;
        }

        int maximum = detachedPart ? 6 : MaximumPieces;
        return Math.Min(visibleSlots, maximum);
    }

    public static BossFragmentAllocation AllocateSpinePieces(
        int bodySlots,
        int detachedSlots)
    {
        int detached = ResolveSpinePieceCount(detachedSlots, detachedPart: true);
        int body = ResolveSpinePieceCount(bodySlots, detachedPart: false);
        body = Math.Min(body, MaximumPieces - detached);
        return new BossFragmentAllocation(body, detached);
    }

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

    public static BossFragmentPoint ResolveBurstDirection(
        int index,
        int count,
        float rotationRadians,
        float jitter)
    {
        count = Math.Max(1, count);
        index = Math.Clamp(index, 0, count - 1);
        jitter = Math.Clamp(jitter, -1f, 1f);
        float sector = MathF.Tau / count;
        float angle = rotationRadians + sector * index + sector * jitter * 0.42f;
        return new BossFragmentPoint(MathF.Cos(angle), MathF.Sin(angle));
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

        BossFragmentPoint[] seeds = requestedSeeds
            .Take(MaximumPieces)
            .Select(seedPoint => new BossFragmentPoint(
                Math.Clamp(seedPoint.X, bounds.X + 0.5f, bounds.X + bounds.Width - 0.5f),
                Math.Clamp(seedPoint.Y, bounds.Y + 0.5f, bounds.Y + bounds.Height - 0.5f)))
            .ToArray();
        if (seeds.Length == 0)
        {
            return [];
        }

        var cells = new List<BossFragmentCell>(seeds.Length);
        for (int i = 0; i < seeds.Length; i++)
        {
            BossFragmentPoint seedPoint = seeds[i];
            List<BossFragmentPoint> polygon =
            [
                new(bounds.X, bounds.Y),
                new(bounds.X + bounds.Width, bounds.Y),
                new(bounds.X + bounds.Width, bounds.Y + bounds.Height),
                new(bounds.X, bounds.Y + bounds.Height)
            ];

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

    public static BossFragmentLaunch ResolveLaunch(
        BossFragmentPoint pieceCenter,
        BossFragmentPoint explosionCenter,
        float areaRatio,
        float randomA,
        float randomB)
    {
        randomA = Math.Clamp(randomA, 0f, 1f);
        randomB = Math.Clamp(randomB, 0f, 1f);
        float radialX = pieceCenter.X - explosionCenter.X;
        float radialY = pieceCenter.Y - explosionCenter.Y;
        float radialLength = MathF.Sqrt(radialX * radialX + radialY * radialY);
        if (radialLength <= 0.001f)
        {
            float angle = MathF.Tau * randomA;
            radialX = MathF.Cos(angle);
            radialY = MathF.Sin(angle);
        }
        else
        {
            radialX /= radialLength;
            radialY /= radialLength;
        }

        float tangent = (randomB - 0.5f) * 1.4f;
        float radialWeight = 0.9f + randomA * 0.45f;
        float upwardImpulse = 0.08f + (1f - randomA) * 0.34f;
        float directionX = radialX * radialWeight - radialY * tangent;
        float directionY = radialY * radialWeight + radialX * tangent - upwardImpulse;
        float directionLength = MathF.Sqrt(directionX * directionX + directionY * directionY);
        directionX /= directionLength;
        directionY /= directionLength;

        float massScale = MathF.Sqrt(Math.Clamp(areaRatio, 0.2f, 3f));
        float speed = Math.Clamp(860f / massScale, 560f, 1240f) * (0.76f + randomB * 0.48f);
        float spinMagnitude = Math.Clamp(680f / massScale, 260f, 1080f)
            * (0.7f + randomA * 0.62f);
        float spinSign = randomB < 0.5f ? -1f : 1f;
        return new BossFragmentLaunch(
            directionX * speed,
            directionY * speed,
            spinMagnitude * spinSign);
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
