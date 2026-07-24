using NinjaSlayer.Code.ExternalAnimations;

namespace NinjaSlayer.LogicTests;

public sealed class ArchitectRagdollMathTests
{
    [Fact]
    public void CubicEaseHasStableEndpointsAndIsMonotonic()
    {
        Assert.Equal(0f, ArchitectRagdollMath.EaseOutCubic(-1f));
        Assert.Equal(1f, ArchitectRagdollMath.EaseOutCubic(2f));

        float previous = 0f;
        for (int step = 0; step <= 100; step++)
        {
            float current = ArchitectRagdollMath.EaseOutCubic(step / 100f);
            Assert.True(current >= previous);
            previous = current;
        }
    }

    [Fact]
    public void ParabolaStartsAtRestLiftsBeforeLandingAndEndsAtDrop()
    {
        Assert.Equal(0f, ArchitectRagdollMath.ParabolicOffset(0f, 20f, 7f));
        Assert.True(ArchitectRagdollMath.ParabolicOffset(0.25f, 20f, 7f) < 0f);
        Assert.Equal(7f, ArchitectRagdollMath.ParabolicOffset(1f, 20f, 7f));
    }

    [Theory]
    [InlineData(10f, 0f, 1f, 1f, 90f, 10f)]
    [InlineData(0f, 10f, 1f, 1f, 180f, -10f)]
    [InlineData(5f, 7f, -2f, 3f, 0f, 21f)]
    public void RotatedScaledYMatchesGodotBasisProjection(
        float x,
        float y,
        float scaleX,
        float scaleY,
        float rotationDegrees,
        float expected)
    {
        Assert.Equal(
            expected,
            ArchitectRagdollMath.RotatedScaledY(
                x,
                y,
                scaleX,
                scaleY,
                rotationDegrees),
            precision: 4);
    }
}
