namespace NinjaSlayer.Code.Combat;

internal sealed class SoftRagdollLink(
    SoftFragmentBody first,
    int firstParticle,
    SoftFragmentBody second,
    int secondParticle,
    float restLength)
{
    private const float Compliance = 0.0025f;
    private float _lambda;
    private float _fatigueSeconds;

    public SoftFragmentBody First { get; } = first;
    public int FirstParticle { get; } = firstParticle;
    public SoftFragmentBody Second { get; } = second;
    public int SecondParticle { get; } = secondParticle;
    public float RestLength { get; } = Math.Max(0.5f, restLength);
    public bool Broken { get; private set; }
    public bool CanBreak { get; set; } = true;
    public float AgeSeconds { get; private set; }
    public float MinimumBreakAgeSeconds { get; set; }
    public float BreakStretchRatio { get; set; } = 2.4f;
    public float BreakPadding { get; set; } = 42f;
    public float FatigueThresholdSeconds { get; set; }
    public float BreakDeadlineSeconds { get; set; } = float.PositiveInfinity;
    public float BreakTimeSeconds { get; private set; } = -1f;
    public float AccumulatedFatigueSeconds => _fatigueSeconds;

    public bool BeginSubstep(float seconds)
    {
        if (Broken)
        {
            return false;
        }

        AgeSeconds += Math.Max(0f, seconds);
        if (!First.HasFiniteState || !Second.HasFiniteState)
        {
            Break();
            return true;
        }

        float distance = Length(Subtract(
            Second.GetParticlePosition(SecondParticle),
            First.GetParticlePosition(FirstParticle)));
        bool overloaded = distance > RestLength * Math.Max(1f, BreakStretchRatio)
            + Math.Max(0f, BreakPadding);
        _fatigueSeconds = overloaded
            ? _fatigueSeconds + Math.Max(0f, seconds)
            : Math.Max(0f, _fatigueSeconds - Math.Max(0f, seconds));
        _lambda = 0f;
        if (CanBreak
            && AgeSeconds >= Math.Max(0f, MinimumBreakAgeSeconds)
            && ((overloaded && _fatigueSeconds >= Math.Max(0f, FatigueThresholdSeconds))
                || AgeSeconds >= Math.Max(MinimumBreakAgeSeconds, BreakDeadlineSeconds)))
        {
            Break();
            return true;
        }

        return false;
    }

    public void Solve(float seconds)
    {
        if (Broken)
        {
            return;
        }

        BossFragmentPoint firstPosition = First.GetParticlePosition(FirstParticle);
        BossFragmentPoint secondPosition = Second.GetParticlePosition(SecondParticle);
        BossFragmentPoint delta = Subtract(secondPosition, firstPosition);
        float distance = Length(delta);
        if (distance <= 0.001f)
        {
            return;
        }

        float firstInverseMass = First.GetParticleInverseMass(FirstParticle);
        float secondInverseMass = Second.GetParticleInverseMass(SecondParticle);
        float alpha = Compliance / Math.Max(seconds * seconds, 0.000001f);
        float deltaLambda = (-(distance - RestLength) - alpha * _lambda)
            / Math.Max(firstInverseMass + secondInverseMass + alpha, 0.0001f);
        _lambda += deltaLambda;
        BossFragmentPoint normal = Multiply(delta, 1f / distance);
        First.ApplyParticleCorrection(
            FirstParticle,
            Multiply(normal, -firstInverseMass * deltaLambda));
        Second.ApplyParticleCorrection(
            SecondParticle,
            Multiply(normal, secondInverseMass * deltaLambda));
    }

    private void Break()
    {
        Broken = true;
        BreakTimeSeconds = AgeSeconds;
    }

    private static BossFragmentPoint Subtract(BossFragmentPoint firstPoint, BossFragmentPoint secondPoint) =>
        new(firstPoint.X - secondPoint.X, firstPoint.Y - secondPoint.Y);

    private static BossFragmentPoint Multiply(BossFragmentPoint point, float value) =>
        new(point.X * value, point.Y * value);

    private static float Length(BossFragmentPoint point) =>
        MathF.Sqrt(point.X * point.X + point.Y * point.Y);
}
