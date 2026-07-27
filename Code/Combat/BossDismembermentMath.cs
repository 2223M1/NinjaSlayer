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

internal static class BossDismembermentMath
{
    private const int MaximumPieces = 9;
    private const int CandidateCount = 72;

    public static int ResolvePieceCount(float width, float height, int availableParts, bool detachedPart)
    {
        if (width <= 1f || height <= 1f || availableParts <= 0)
        {
            return 0;
        }

        float metric = MathF.Sqrt(width * height);
        int minimum = detachedPart ? 2 : 5;
        int maximum = detachedPart ? 4 : 8;
        int divisor = detachedPart ? 72 : 90;
        int desired = Math.Clamp((int)MathF.Round(metric / divisor), minimum, maximum);
        return Math.Clamp(desired, 1, Math.Min(availableParts, MaximumPieces));
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
        var cells = new List<BossFragmentCell>(seeds.Count);
        for (int i = 0; i < seeds.Count; i++)
        {
            BossFragmentPoint seedPoint = seeds[i];
            List<BossFragmentPoint> polygon =
            [
                new(bounds.X, bounds.Y),
                new(bounds.X + bounds.Width, bounds.Y),
                new(bounds.X + bounds.Width, bounds.Y + bounds.Height),
                new(bounds.X, bounds.Y + bounds.Height)
            ];

            for (int j = 0; j < seeds.Count && polygon.Count > 0; j++)
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
            float angle = MathF.PI * (0.2f + 0.6f * randomA);
            radialX = MathF.Cos(angle);
            radialY = -MathF.Sin(angle);
        }
        else
        {
            radialX /= radialLength;
            radialY /= radialLength;
        }

        float tangent = (randomB - 0.5f) * 0.5f;
        float directionX = radialX - radialY * tangent;
        float directionY = radialY * 0.3f + radialX * tangent - (0.62f + 0.28f * randomA);
        directionY = MathF.Min(directionY, -0.18f);
        float directionLength = MathF.Sqrt(directionX * directionX + directionY * directionY);
        directionX /= directionLength;
        directionY /= directionLength;

        float massScale = MathF.Sqrt(Math.Clamp(areaRatio, 0.2f, 3f));
        float speed = Math.Clamp(770f / massScale, 520f, 1050f) * (0.9f + randomB * 0.2f);
        float spinMagnitude = Math.Clamp(540f / massScale, 220f, 840f)
            * (0.82f + randomA * 0.28f);
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
}
