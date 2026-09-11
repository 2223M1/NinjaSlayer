using System.Numerics;
using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class DragPoseTests
{
    [Fact]
    public void EmptySideIsUprightAndTargetsBoundBothPitchDirections()
    {
        Vector2[] points = CombatBodyContours.NinjaSlayer.Select(p => (p - new Vector2(580, 30.30303f)) * .33f).ToArray();
        foreach (float side in new[] { -1f, 1f })
        {
            Vector2[] contour = points.Select(p => new Vector2(p.X * side, p.Y)).ToArray();
            float reference = side < 0 ? MathF.PI : 0f;
            Vector2[] targets = [new(600 * side, -500), new(700 * side, -40)];
            float a = GroundedPoseMath.AimAngle(contour, Vector2.Zero, targets[0], -7, reference, 0);
            float b = GroundedPoseMath.AimAngle(contour, Vector2.Zero, targets[1], -7, reference, 0);
            foreach (float y in new[] { -2000f, -500f, -180f, 0f, 2000f })
            {
                Vector2 pointer = new(600 * side, y);
                Assert.Equal(0, DragPoseMath.LimitedAngle(contour, Vector2.Zero, pointer, [], -7, reference, 0));
                float limited = DragPoseMath.LimitedAngle(contour, Vector2.Zero, pointer, targets, -7, reference, 0);
                Assert.InRange(limited, Math.Min(0, Math.Min(a, b)), Math.Max(0, Math.Max(a, b)));
            }
            foreach (Vector2 target in targets)
            {
                float rotation = DragPoseMath.LimitedAngle(contour, Vector2.Zero, target, targets, -7, reference, 0);
                Vector2 core = new(0, GroundedPoseMath.CoreY(contour, rotation, -7));
                Vector2 facing = new(MathF.Cos(reference + rotation), MathF.Sin(reference + rotation));
                Assert.True(Vector2.Dot(facing, Vector2.Normalize(target - core)) > .99999f);
            }
        }
    }

    [Fact]
    public void ReversingTurnStartsAtTheCurrentAngle()
    {
        float current = DragPoseMath.TurnAngle(0, 180, .05f, .15f);
        float duration = .15f * current / 180;
        Assert.Equal(current, DragPoseMath.TurnAngle(current, 0, 0, duration));
        Assert.Equal(0, DragPoseMath.TurnAngle(current, 0, duration, duration));
        Assert.Equal(90, DragPoseMath.TurnAngle(0, 180, .075f, .15f));
    }

    [Fact]
    public void ChargeRemainsBoundedAtEveryPreviewStrengthAndFrameRate()
    {
        foreach (TornadoChargeProfile profile in new[]
        {
            new TornadoChargeProfile(32, .25f, 1.04f, .94f, .8f, .004f, 8),
            TornadoChargeProfile.Default,
            new TornadoChargeProfile(48, .35f, 1.12f, .86f, 1.8f, .012f, 12)
        })
        foreach (int fps in new[] { 30, 60, 120, 144 })
        {
            Assert.Equal((0f, Vector2.One), profile.Sample(0));
            for (int i = 0; i <= fps * 10; i++)
            {
                float time = (float)i / fps;
                var pose = profile.Sample(time);
                Assert.InRange(pose.Back, 0, profile.Back + profile.TrembleX);
                Assert.InRange(pose.Scale.X, 1, profile.ScaleX);
                Assert.InRange(pose.Scale.Y, profile.ScaleY - profile.TrembleY, 1);
                if (time >= profile.Seconds)
                    Assert.InRange(pose.Back, profile.Back - profile.TrembleX, profile.Back + profile.TrembleX);
            }
        }
    }
}
