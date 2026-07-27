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

    public SoftFragmentBody First { get; } = first;
    public int FirstParticle { get; } = firstParticle;
    public SoftFragmentBody Second { get; } = second;
    public int SecondParticle { get; } = secondParticle;
    public float RestLength { get; } = Math.Max(0.5f, restLength);
    public bool Broken { get; private set; }
    public bool CanBreak { get; set; } = true;

    public void BeginSubstep() => _lambda = 0f;

    public bool Solve(float seconds)
    {
        if (Broken)
        {
            return false;
        }

        if (!First.HasFiniteState || !Second.HasFiniteState)
        {
            Broken = true;
            return true;
        }

        BossFragmentPoint firstPosition = First.GetParticlePosition(FirstParticle);
        BossFragmentPoint secondPosition = Second.GetParticlePosition(SecondParticle);
        BossFragmentPoint delta = Subtract(secondPosition, firstPosition);
        float distance = Length(delta);
        if (CanBreak && distance > RestLength * 2.4f + 42f)
        {
            Broken = true;
            return true;
        }

        if (distance <= 0.001f)
        {
            return false;
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
        return false;
    }

    private static BossFragmentPoint Subtract(BossFragmentPoint firstPoint, BossFragmentPoint secondPoint) =>
        new(firstPoint.X - secondPoint.X, firstPoint.Y - secondPoint.Y);

    private static BossFragmentPoint Multiply(BossFragmentPoint point, float value) =>
        new(point.X * value, point.Y * value);

    private static float Length(BossFragmentPoint point) =>
        MathF.Sqrt(point.X * point.X + point.Y * point.Y);
}
