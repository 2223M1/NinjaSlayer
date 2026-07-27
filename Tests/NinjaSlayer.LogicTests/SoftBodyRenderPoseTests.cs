using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class SoftBodyRenderPoseTests
{
    [Fact]
    public void ResolverSeparatesTranslationRotationAndScaleFromLocalDeformation()
    {
        SoftFragmentBody body = CreateBody();
        float rotation = MathF.PI * 0.5f;
        float scale = 1.5f;
        BossFragmentPoint translation = new(30f, -20f);
        for (int index = 0; index < SoftFragmentBody.ParticleCount; index++)
        {
            BossFragmentPoint rest = body.GetRestParticlePosition(index);
            BossFragmentPoint target = new(
                translation.X - rest.Y * scale,
                translation.Y + rest.X * scale);
            BossFragmentPoint current = body.GetParticlePosition(index);
            body.ApplyParticleCorrection(
                index,
                new BossFragmentPoint(target.X - current.X, target.Y - current.Y));
        }

        var residuals = new BossFragmentPoint[SoftFragmentBody.ParticleCount];
        Assert.True(SoftBodyRenderPoseResolver.TryResolve(body, 0f, residuals, out SoftBodyRenderPose pose));
        Assert.Equal(translation.X, pose.Position.X, 3);
        Assert.Equal(translation.Y, pose.Position.Y, 3);
        Assert.Equal(rotation, pose.RotationRadians, 3);
        Assert.Equal(scale, pose.UniformScale, 3);
        Assert.InRange(pose.MaximumResidual, 0f, 0.001f);
    }

    [Fact]
    public void ResolverKeepsLocalCornerCompressionInTheResidualField()
    {
        SoftFragmentBody body = CreateBody();
        body.ApplyParticleCorrection(0, new BossFragmentPoint(12f, 8f));
        var residuals = new BossFragmentPoint[SoftFragmentBody.ParticleCount];

        Assert.True(SoftBodyRenderPoseResolver.TryResolve(body, 0f, residuals, out SoftBodyRenderPose pose));
        Assert.True(pose.MaximumResidual > 5f);
        Assert.True(MathF.Abs(residuals[0].X) > 1f || MathF.Abs(residuals[0].Y) > 1f);
    }

    [Fact]
    public void RotationUnwrapRemainsContinuousAcrossPi()
    {
        float previous = MathF.PI - 0.1f;
        float current = -MathF.PI + 0.1f;

        float unwrapped = SoftBodyRenderPoseResolver.Unwrap(current, previous);

        Assert.InRange(unwrapped - previous, 0.19f, 0.21f);
    }

    private static SoftFragmentBody CreateBody()
    {
        BossFragmentPoint[] hull =
        [
            new(-50f, -50f),
            new(50f, -50f),
            new(50f, 50f),
            new(-50f, 50f)
        ];
        return new SoftFragmentBody(
            1,
            new SoftBodyBounds(-50f, -50f, 100f, 100f),
            hull,
            default,
            compressedScale: 1f,
            mass: 1f);
    }
}
