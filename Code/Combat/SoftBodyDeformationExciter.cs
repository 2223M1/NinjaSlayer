namespace NinjaSlayer.Code.Combat;

internal sealed class SoftBodyDeformationExciter
{
    private readonly BossFragmentPoint[] _squashPattern;
    private readonly BossFragmentPoint[] _bendPattern;
    private readonly float _shortDimension;

    public SoftBodyDeformationExciter(
        IReadOnlyList<BossFragmentPoint> restPoints,
        BossFragmentPoint restCenter,
        float shortDimension,
        ulong seed)
    {
        _shortDimension = Math.Max(1f, shortDimension);
        float phase = ResolveUnit(seed) * MathF.Tau;
        BossFragmentPoint axis = new(MathF.Cos(phase), MathF.Sin(phase));
        BossFragmentPoint perpendicular = new(-axis.Y, axis.X);
        _squashPattern = BuildSquashPattern(restPoints, restCenter, axis, perpendicular);
        _bendPattern = BuildBendPattern(restPoints, restCenter, axis, perpendicular);
        RemoveRigidComponents(_squashPattern, restPoints, restCenter);
        RemoveRigidComponents(_bendPattern, restPoints, restCenter);
        NormalizePattern(_squashPattern);
        Orthogonalize(_bendPattern, _squashPattern);
        NormalizePattern(_bendPattern);
    }

    public void AddLaunchExcitation(
        BossFragmentPoint linearVelocityDelta,
        float angularVelocityDelta,
        float bodyRotation,
        float strength,
        Span<BossFragmentPoint> velocityDeltas)
    {
        velocityDeltas.Clear();
        strength = Math.Clamp(strength, 0f, 1f);
        float speed = Length(linearVelocityDelta);
        if (strength <= 0f
            || (!float.IsFinite(speed) || speed <= 0.001f)
                && (!float.IsFinite(angularVelocityDelta)
                    || MathF.Abs(angularVelocityDelta) <= 0.001f))
        {
            return;
        }

        float cosine = MathF.Cos(bodyRotation);
        float sine = MathF.Sin(bodyRotation);
        BossFragmentPoint localVelocity = new(
            linearVelocityDelta.X * cosine + linearVelocityDelta.Y * sine,
            -linearVelocityDelta.X * sine + linearVelocityDelta.Y * cosine);
        float phase = MathF.Atan2(localVelocity.Y, localVelocity.X);
        float modalSpeed = speed * strength * 6f;
        float squashVelocity = MathF.Cos(phase) * modalSpeed;
        float bendVelocity = MathF.Sin(phase) * modalSpeed
            + angularVelocityDelta * _shortDimension * 0.32f;
        float maximumModeVelocity = _shortDimension * MathF.Tau * 2.2f * 0.9f;
        squashVelocity = Math.Clamp(
            squashVelocity,
            -maximumModeVelocity,
            maximumModeVelocity);
        bendVelocity = Math.Clamp(
            bendVelocity,
            -maximumModeVelocity,
            maximumModeVelocity);

        for (int index = 0; index < velocityDeltas.Length; index++)
        {
            BossFragmentPoint local = Add(
                Multiply(_squashPattern[index], squashVelocity),
                Multiply(_bendPattern[index], bendVelocity));
            velocityDeltas[index] = new BossFragmentPoint(
                local.X * cosine - local.Y * sine,
                local.X * sine + local.Y * cosine);
        }
    }

    private static BossFragmentPoint[] BuildSquashPattern(
        IReadOnlyList<BossFragmentPoint> points,
        BossFragmentPoint center,
        BossFragmentPoint axis,
        BossFragmentPoint perpendicular)
    {
        float axisExtent = ResolveExtent(points, center, axis);
        float perpendicularExtent = ResolveExtent(points, center, perpendicular);
        var result = new BossFragmentPoint[points.Count];
        for (int index = 0; index < points.Count; index++)
        {
            BossFragmentPoint rest = Subtract(points[index], center);
            float x = Dot(rest, axis) / axisExtent;
            float y = Dot(rest, perpendicular) / perpendicularExtent;
            result[index] = Add(
                Multiply(axis, x),
                Multiply(perpendicular, -y * 0.82f));
        }

        return result;
    }

    private static BossFragmentPoint[] BuildBendPattern(
        IReadOnlyList<BossFragmentPoint> points,
        BossFragmentPoint center,
        BossFragmentPoint axis,
        BossFragmentPoint perpendicular)
    {
        float extent = ResolveExtent(points, center, axis);
        float meanSquare = 0f;
        var normalized = new float[points.Count];
        for (int index = 0; index < points.Count; index++)
        {
            float value = Dot(Subtract(points[index], center), axis) / extent;
            normalized[index] = value;
            meanSquare += value * value;
        }

        meanSquare /= Math.Max(1, points.Count);
        var result = new BossFragmentPoint[points.Count];
        for (int index = 0; index < points.Count; index++)
        {
            result[index] = Multiply(
                perpendicular,
                normalized[index] * normalized[index] - meanSquare);
        }

        return result;
    }

    private static void RemoveRigidComponents(
        BossFragmentPoint[] pattern,
        IReadOnlyList<BossFragmentPoint> restPoints,
        BossFragmentPoint restCenter)
    {
        BossFragmentPoint mean = default;
        for (int index = 0; index < pattern.Length; index++)
        {
            mean = Add(mean, pattern[index]);
        }

        mean = Multiply(mean, 1f / Math.Max(1, pattern.Length));
        float angularNumerator = 0f;
        float angularDenominator = 0f;
        for (int index = 0; index < pattern.Length; index++)
        {
            pattern[index] = Subtract(pattern[index], mean);
            BossFragmentPoint radius = Subtract(restPoints[index], restCenter);
            angularNumerator += Cross(radius, pattern[index]);
            angularDenominator += Dot(radius, radius);
        }

        float angular = angularDenominator <= 0.001f
            ? 0f
            : angularNumerator / angularDenominator;
        for (int index = 0; index < pattern.Length; index++)
        {
            BossFragmentPoint radius = Subtract(restPoints[index], restCenter);
            pattern[index] = Subtract(
                pattern[index],
                new BossFragmentPoint(-radius.Y * angular, radius.X * angular));
        }
    }

    private static void Orthogonalize(
        BossFragmentPoint[] pattern,
        BossFragmentPoint[] basis)
    {
        float projection = 0f;
        float denominator = 0f;
        for (int index = 0; index < pattern.Length; index++)
        {
            projection += Dot(pattern[index], basis[index]);
            denominator += Dot(basis[index], basis[index]);
        }

        float scale = denominator <= 0.0001f ? 0f : projection / denominator;
        for (int index = 0; index < pattern.Length; index++)
        {
            pattern[index] = Subtract(pattern[index], Multiply(basis[index], scale));
        }
    }

    private static void NormalizePattern(BossFragmentPoint[] pattern)
    {
        float squared = 0f;
        for (int index = 0; index < pattern.Length; index++)
        {
            squared += Dot(pattern[index], pattern[index]);
        }

        float rms = MathF.Sqrt(squared / Math.Max(1, pattern.Length));
        if (rms <= 0.001f)
        {
            return;
        }

        for (int index = 0; index < pattern.Length; index++)
        {
            pattern[index] = Multiply(pattern[index], 1f / rms);
        }
    }

    private static float ResolveExtent(
        IReadOnlyList<BossFragmentPoint> points,
        BossFragmentPoint center,
        BossFragmentPoint axis)
    {
        float extent = 0f;
        for (int index = 0; index < points.Count; index++)
        {
            extent = Math.Max(extent, MathF.Abs(Dot(Subtract(points[index], center), axis)));
        }

        return Math.Max(1f, extent);
    }

    private static float ResolveUnit(ulong value)
    {
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return (value >> 40) * (1f / (1 << 24));
    }

    private static BossFragmentPoint Add(BossFragmentPoint first, BossFragmentPoint second) =>
        new(first.X + second.X, first.Y + second.Y);

    private static BossFragmentPoint Subtract(BossFragmentPoint first, BossFragmentPoint second) =>
        new(first.X - second.X, first.Y - second.Y);

    private static BossFragmentPoint Multiply(BossFragmentPoint point, float scalar) =>
        new(point.X * scalar, point.Y * scalar);

    private static float Dot(BossFragmentPoint first, BossFragmentPoint second) =>
        first.X * second.X + first.Y * second.Y;

    private static float Cross(BossFragmentPoint first, BossFragmentPoint second) =>
        first.X * second.Y - first.Y * second.X;

    private static float Length(BossFragmentPoint point) =>
        MathF.Sqrt(point.X * point.X + point.Y * point.Y);
}
