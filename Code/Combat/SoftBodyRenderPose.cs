namespace NinjaSlayer.Code.Combat;

internal readonly record struct SoftBodyRenderPose(
    BossFragmentPoint Position,
    float RotationRadians,
    float UniformScale);

internal static class SoftBodyRenderPoseResolver
{
    private const float MinimumVariance = 0.001f;
    private const float MinimumScale = 0.05f;
    private const float MaximumScale = 4f;

    public static bool TryResolve(
        SoftFragmentBody body,
        float previousRotation,
        Span<BossFragmentPoint> residuals,
        out SoftBodyRenderPose pose)
    {
        if (residuals.Length < SoftFragmentBody.ParticleCount)
        {
            throw new ArgumentException("The residual buffer must hold all soft-body particles.", nameof(residuals));
        }

        BossFragmentPoint restCenter = body.RestCenter;
        BossFragmentPoint currentCenter = body.Center;
        float covarianceA = 0f;
        float covarianceB = 0f;
        float restVariance = 0f;
        for (int index = 0; index < SoftFragmentBody.ParticleCount; index++)
        {
            BossFragmentPoint rest = Subtract(body.GetRestParticlePosition(index), restCenter);
            BossFragmentPoint current = Subtract(body.GetParticlePosition(index), currentCenter);
            if (!IsFinite(rest) || !IsFinite(current))
            {
                pose = default;
                return false;
            }

            covarianceA += rest.X * current.X + rest.Y * current.Y;
            covarianceB += rest.X * current.Y - rest.Y * current.X;
            restVariance += rest.X * rest.X + rest.Y * rest.Y;
        }

        float covarianceMagnitude = MathF.Sqrt(
            covarianceA * covarianceA + covarianceB * covarianceB);
        if (!float.IsFinite(covarianceMagnitude)
            || covarianceMagnitude <= MinimumVariance
            || restVariance <= MinimumVariance)
        {
            pose = default;
            return false;
        }

        float rotation = Unwrap(MathF.Atan2(covarianceB, covarianceA), previousRotation);
        float scale = covarianceMagnitude / restVariance;
        if (!float.IsFinite(rotation)
            || !float.IsFinite(scale)
            || scale < MinimumScale
            || scale > MaximumScale
            || body.ResolveMinimumCellAreaRatio() <= 0f)
        {
            pose = default;
            return false;
        }

        float cosine = MathF.Cos(rotation);
        float sine = MathF.Sin(rotation);
        float inverseScale = 1f / scale;
        for (int index = 0; index < SoftFragmentBody.ParticleCount; index++)
        {
            BossFragmentPoint rest = Subtract(body.GetRestParticlePosition(index), restCenter);
            BossFragmentPoint current = Subtract(body.GetParticlePosition(index), currentCenter);
            BossFragmentPoint unrotated = new(
                (current.X * cosine + current.Y * sine) * inverseScale,
                (-current.X * sine + current.Y * cosine) * inverseScale);
            BossFragmentPoint residual = Subtract(unrotated, rest);
            if (!IsFinite(residual))
            {
                pose = default;
                return false;
            }

            residuals[index] = residual;
        }

        pose = new SoftBodyRenderPose(currentCenter, rotation, scale);
        return true;
    }

    internal static float Unwrap(float rotation, float previousRotation)
    {
        if (!float.IsFinite(rotation) || !float.IsFinite(previousRotation))
        {
            return rotation;
        }

        while (rotation - previousRotation > MathF.PI)
        {
            rotation -= MathF.Tau;
        }

        while (rotation - previousRotation < -MathF.PI)
        {
            rotation += MathF.Tau;
        }

        return rotation;
    }

    private static bool IsFinite(BossFragmentPoint point) =>
        float.IsFinite(point.X) && float.IsFinite(point.Y);

    private static BossFragmentPoint Subtract(BossFragmentPoint first, BossFragmentPoint second) =>
        new(first.X - second.X, first.Y - second.Y);

}
