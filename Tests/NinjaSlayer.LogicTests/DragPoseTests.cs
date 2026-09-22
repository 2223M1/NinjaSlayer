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
                Assert.Equal(0, DragPoseMath.AimSample(contour, Vector2.Zero, pointer, [], -7, reference, 0).Limited);
                float limited = DragPoseMath.AimSample(contour, Vector2.Zero, pointer, targets, -7, reference, 0).Limited;
                Assert.InRange(limited, Math.Min(0, Math.Min(a, b)), Math.Max(0, Math.Max(a, b)));
            }
            foreach (Vector2 target in targets)
            {
                float rotation = DragPoseMath.AimSample(contour, Vector2.Zero, target, targets, -7, reference, 0).Limited;
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

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    public void InertiaUsesElapsedTimeAndDoesNotOvershootAnOrdinaryTarget(int fps)
    {
        var follow = new DragAimFollow();
        var aim = new DragAimSample(.5f, -.8f, .8f);
        float previous = 0f;
        for (int frame = 0; frame < fps; frame++)
        {
            float angle = follow.Advance(aim, 0f, false, 1f / fps);
            Assert.InRange(angle, previous, .5f);
            Assert.Equal(angle, follow.Advance(aim, 100f, true, 0f));
            previous = angle;
        }
        Assert.Equal(.5f, previous, 5);
        follow.Reset(0f);
        follow.Advance(aim, 0f, false, .14f);
        Assert.InRange(follow.Angle, .45f, .455f);
    }

    [Fact]
    public void RetargetingPreservesMomentumAndConvergesWithoutRestarting()
    {
        var follow = new DragAimFollow();
        follow.Advance(new(.5f, -1f, 1f), 0, false, .05f);
        float angle = follow.Angle, velocity = follow.Velocity;
        follow.Advance(new(-.5f, -1f, 1f), 0, false, 0);
        Assert.Equal(angle, follow.Angle);
        Assert.Equal(velocity, follow.Velocity);
        for (int frame = 0; frame < 60; frame++) follow.Advance(new(-.5f, -1f, 1f), 0, false, 1f / 60f);
        Assert.Equal(-.5f, follow.Angle, 5);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void BoundaryPushBouncesOnceThenSettlesAndCanBePushedAgain(int side)
    {
        var follow = new DragAimFollow();
        follow.Reset(side * .4f);
        var aim = new DragAimSample(side * .6f, -.4f, .4f);
        float peak = 0;
        for (int frame = 0; frame < 120; frame++)
        {
            float angle = follow.Advance(aim, side * 8f, true, 1f / 120f);
            peak = Math.Max(peak, side * angle - .4f);
            Assert.InRange(side * angle, .4f - .00001f, .4f + DragAimFollow.MaximumOvershoot + .00001f);
        }
        Assert.InRange(peak, .035f, DragAimFollow.MaximumOvershoot);
        Assert.Equal(side * .4f, follow.Angle, 5);
        for (int frame = 0; frame < 60; frame++) follow.Advance(aim, 0, true, 1f / 120f);
        Assert.Equal(side * .4f, follow.Angle, 5);
        follow.Advance(aim, side * 8f, true, 1f / 120f);
        Assert.True(side * follow.Angle > .4f);
    }

    [Fact]
    public void FastPushWaitsForBodyToReachEdgeAndGeometryChangesDoNotGenerateRebounds()
    {
        var follow = new DragAimFollow();
        var edge = new DragAimSample(.7f, 0f, .4f);
        follow.Advance(edge, 8f, true, 1f / 120f);
        float peak = 0f;
        for (int frame = 0; frame < 120; frame++)
            peak = Math.Max(peak, follow.Advance(edge, 0, true, 1f / 120f));
        Assert.InRange(peak, .41f, .4f + DragAimFollow.MaximumOvershoot);
        Assert.Equal(.4f, follow.Angle, 5);
        float previous = follow.Angle;
        for (int frame = 0; frame < 60; frame++)
        {
            float angle = follow.Advance(default, 0, false, 1f / 60f);
            Assert.InRange(angle, 0f, previous);
            previous = angle;
        }
        Assert.Equal(0f, follow.Angle);
        follow.Reset(0f);
        follow.Advance(edge, .01f, true, .25f);
        Assert.InRange(follow.Angle, 0f, .4f);
        follow.Reset(0f);
        follow.Advance(edge, 8f, false, .25f);
        Assert.InRange(follow.Angle, 0f, .4f);
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
