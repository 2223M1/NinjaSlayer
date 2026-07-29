namespace NinjaSlayer.Code.Combat;

internal static class SoftContactSolver
{
    private const float Restitution = 0.32f;
    private const float Friction = 0.22f;
    private const float MaximumContactCorrectionRatio = 0.08f;
    private const float VisibleBounceClosingSpeed = 40f;

    public static void SolvePositions(SoftContactManifold manifold)
    {
        for (int pointIndex = 0; pointIndex < manifold.PointCount; pointIndex++)
        {
            SoftContactPoint point = manifold[pointIndex];
            float firstInverseMass = manifold.First.GetEffectiveInverseMass(point.FirstU, point.FirstV);
            float secondInverseMass = manifold.Second.GetEffectiveInverseMass(point.SecondU, point.SecondV);
            float denominator = firstInverseMass + secondInverseMass;
            if (denominator <= 0.0001f || point.Penetration <= 0f)
            {
                continue;
            }

            float maximumCorrection = Math.Min(
                manifold.First.ShortDimension,
                manifold.Second.ShortDimension) * MaximumContactCorrectionRatio;
            float correction = Math.Min(point.Penetration * 0.68f, maximumCorrection);
            float lambda = correction / denominator;
            manifold.First.ApplyContactPositionImpulse(
                point.FirstU,
                point.FirstV,
                Multiply(manifold.Normal, -1f),
                lambda);
            manifold.Second.ApplyContactPositionImpulse(
                point.SecondU,
                point.SecondV,
                manifold.Normal,
                lambda);
        }
    }

    public static SoftContactVelocityResult SolveVelocities(SoftContactManifold manifold)
    {
        bool bounced = false;
        float maximumClosingSpeed = 0f;
        float maximumSeparatingSpeed = 0f;
        for (int pointIndex = 0; pointIndex < manifold.PointCount; pointIndex++)
        {
            SoftContactPoint point = manifold[pointIndex];
            BossFragmentPoint firstVelocity = manifold.First.GetVelocityAt(point.FirstU, point.FirstV);
            BossFragmentPoint secondVelocity = manifold.Second.GetVelocityAt(point.SecondU, point.SecondV);
            BossFragmentPoint relative = Subtract(secondVelocity, firstVelocity);
            float normalSpeed = Dot(relative, manifold.Normal);
            if (!float.IsFinite(normalSpeed) || !float.IsFinite(point.PreSolveNormalSpeed))
            {
                continue;
            }

            maximumClosingSpeed = Math.Max(
                maximumClosingSpeed,
                Math.Max(0f, -point.PreSolveNormalSpeed));

            float firstInverseMass = manifold.First.GetEffectiveInverseMass(point.FirstU, point.FirstV);
            float secondInverseMass = manifold.Second.GetEffectiveInverseMass(point.SecondU, point.SecondV);
            float denominator = firstInverseMass + secondInverseMass;
            if (denominator <= 0.0001f)
            {
                continue;
            }

            float targetNormalSpeed = manifold.IsNewContact && point.PreSolveNormalSpeed < -1f
                ? -Restitution * point.PreSolveNormalSpeed
                : 0f;
            float normalImpulse = Math.Max(0f, (targetNormalSpeed - normalSpeed) / denominator);
            if (normalImpulse <= 0.0001f)
            {
                continue;
            }

            ApplyImpulsePair(manifold, point, manifold.Normal, normalImpulse);
            firstVelocity = manifold.First.GetVelocityAt(point.FirstU, point.FirstV);
            secondVelocity = manifold.Second.GetVelocityAt(point.SecondU, point.SecondV);
            relative = Subtract(secondVelocity, firstVelocity);
            float separatingSpeed = Dot(relative, manifold.Normal);
            maximumSeparatingSpeed = Math.Max(maximumSeparatingSpeed, separatingSpeed);
            bounced |= manifold.IsNewContact
                && point.PreSolveNormalSpeed <= -VisibleBounceClosingSpeed
                && separatingSpeed > 1f;
            BossFragmentPoint tangentVelocity = Subtract(
                relative,
                Multiply(manifold.Normal, Dot(relative, manifold.Normal)));
            float tangentLength = Length(tangentVelocity);
            if (tangentLength <= 0.001f)
            {
                continue;
            }

            BossFragmentPoint tangent = Multiply(tangentVelocity, 1f / tangentLength);
            float tangentImpulse = Math.Clamp(
                -Dot(relative, tangent) / denominator,
                -Friction * normalImpulse,
                Friction * normalImpulse);
            ApplyImpulsePair(manifold, point, tangent, tangentImpulse);
        }

        return new SoftContactVelocityResult(
            bounced,
            maximumClosingSpeed,
            maximumSeparatingSpeed);
    }

    private static void ApplyImpulsePair(
        SoftContactManifold manifold,
        SoftContactPoint point,
        BossFragmentPoint direction,
        float impulse)
    {
        manifold.First.ApplyVelocityImpulse(
            point.FirstU,
            point.FirstV,
            direction,
            -impulse);
        manifold.Second.ApplyVelocityImpulse(
            point.SecondU,
            point.SecondV,
            direction,
            impulse);
    }

    private static BossFragmentPoint Subtract(BossFragmentPoint first, BossFragmentPoint second) =>
        new(first.X - second.X, first.Y - second.Y);

    private static BossFragmentPoint Multiply(BossFragmentPoint point, float value) =>
        new(point.X * value, point.Y * value);

    private static float Dot(BossFragmentPoint first, BossFragmentPoint second) =>
        first.X * second.X + first.Y * second.Y;

    private static float Length(BossFragmentPoint point) =>
        MathF.Sqrt(point.X * point.X + point.Y * point.Y);
}
