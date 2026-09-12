using System.Numerics;
using NinjaSlayer.Code.Combat;
using Xunit;

namespace NinjaSlayer.LogicTests;

public sealed class FreeControlPhysicsTests
{
    private static FreeControlPhysics Create() => new(new(500f, 500f), Vector2.Zero, new(1400f, 700f),
        [new(-50f, -100f), new(50f, -100f), new(50f, 100f), new(-50f, 100f)]);

    [Fact]
    public void WalkJumpAndWallContactsWorkWithoutGodot()
    {
        using var physics = Create();
        var motor = new FreeControlMotor();
        void Step(float axis, bool jump = false, bool held = false)
        {
            motor.Velocity = physics.Velocity;
            motor.Step(FreeControlPhysics.StepSeconds, axis, jump, held, false, false,
                physics.Grounded, physics.WallNormal, 1f);
            physics.Velocity = motor.Velocity;
            physics.Step(Vector2.Zero);
        }
        for (int i = 0; i < 90; i++) Step(0f);
        Assert.True(physics.Grounded);
        Assert.InRange(physics.Position.Y, 599f, 601f);
        float x = physics.Position.X;
        for (int i = 0; i < 20; i++) Step(1f);
        Assert.True(physics.Position.X > x + 100f);
        Step(0f, true, true);
        for (int i = 0; i < 10; i++) Step(0f, held: true);
        Assert.True(physics.Position.Y < 490f);
        Step(0f, true, true);
        Assert.True(physics.Velocity.Y < -800f);
        for (int i = 0; i < 180; i++) Step(1f);
        Assert.True(physics.Grounded);
        Assert.InRange(physics.Position.X, 1348f, 1351f);
        Assert.True(physics.WallNormal < -.9f);
    }

    [Theory]
    [InlineData(-30f, -80f)]
    [InlineData(30f, 0f)]
    [InlineData(0f, 80f)]
    public void MouseGripMovesBodyAndHangsUnderGravity(float x, float y)
    {
        using var physics = Create();
        Vector2 local = new(x, y), pointer = physics.Position + local;
        physics.Grab(local);
        Vector2 origin = pointer;
        for (int i = 1; i <= 30; i++)
        {
            pointer = origin + new Vector2(i * 8f, -i * 8f);
            physics.Step(pointer);
        }
        Assert.True(physics.Position.X > 600f, $"Body did not follow: {physics.Position}, grip {physics.GripPoint}, pointer {pointer}");
        for (int i = 0; i < 1800; i++) physics.Step(pointer);
        Assert.InRange(Vector2.Distance(physics.GripPoint, pointer), 0f, 3f);
        Assert.True(physics.Position.Y > pointer.Y, $"Body {physics.Position}, pointer {pointer}, angle {physics.Rotation}");
        Assert.True(Math.Abs(physics.Rotation) > .1f);
        physics.AngularVelocity = 12f;
        physics.Release();
        physics.Step(pointer);
        Assert.False(physics.Grabbing);
        Assert.True(Math.Abs(physics.AngularVelocity) > 10f);
        physics.ResumeWalking();
        Assert.False(physics.Ragging);
        Assert.Equal(0f, physics.AngularVelocity);
    }
}
