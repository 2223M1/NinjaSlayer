using System.Numerics;
using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class GroundedPoseTests
{
    [Fact]
    public void BodyAlwaysTouchesSupportLineAcrossRotationAndMirroring()
    {
        foreach (Vector2[] contour in new[] { CombatBodyContours.NinjaSlayer, CombatBodyContours.YamotoKoki, CombatBodyContours.FullyReleasedNaraku })
        foreach (float mirror in new[] { -1f, 1f })
        foreach (float stretch in new[] { 0.5f, 1f, 1.5f })
        {
            Vector2[] points = contour.Select(p => new Vector2(p.X * mirror * stretch, p.Y * 0.33f)).ToArray();
            for (int degrees = -180; degrees <= 180; degrees += 3)
            {
                float angle = degrees * MathF.PI / 180f;
                float coreY = GroundedPoseMath.CoreY(points, angle, -7f);
                Assert.Equal(-7f, points.Max(p => GroundedPoseMath.Rotate(p, angle).Y + coreY), 3);
            }
        }
    }

    [Fact]
    public void AimUsesTheGroundCorrectedCoreForHighAndLowTargets()
    {
        Vector2[] points = CombatBodyContours.NinjaSlayer.Select(p => (p - new Vector2(580f, 30.30303f)) * 0.33f).ToArray();
        foreach (float side in new[] { -1f, 1f })
        foreach (float y in new[] { -600f, -173f, -40f })
        foreach (bool kick in new[] { false, true })
        {
            Vector2[] mirrored = points.Select(p => new Vector2(p.X * side, p.Y)).ToArray();
            Vector2 target = new(600f * side, y);
            float reference = kick ? MathF.PI * 0.5f : side < 0f ? MathF.PI : 0f;
            float angle = GroundedPoseMath.AimAngle(mirrored, Vector2.Zero, target, -7f, reference, 0f);
            Vector2 actualCore = new(0, GroundedPoseMath.CoreY(mirrored, angle, -7f));
            Vector2 direction = Vector2.Normalize(target - actualCore);
            Vector2 aim = new(MathF.Cos(reference + angle), MathF.Sin(reference + angle));
            Assert.True(Vector2.Dot(direction, aim) > 0.99999f);
        }
    }

    [Theory]
    [InlineData(60f, 0f, 0f)]
    [InlineData(60f, 150f, 60f)]
    [InlineData(180f, 150f, 150f)]
    [InlineData(-100f, 0f, -100f)]
    public void DescendingAttackStopsAtGround(float travel, float altitude, float expected) =>
        Assert.Equal(expected, GroundedPoseMath.ClampDescent(travel, altitude));
}
