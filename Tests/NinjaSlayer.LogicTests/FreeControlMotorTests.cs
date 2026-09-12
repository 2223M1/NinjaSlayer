using NinjaSlayer.Code.Combat;
using System.Numerics;

namespace NinjaSlayer.LogicTests;

public sealed class FreeControlMotorTests
{
    private static void Step(FreeControlMotor motor, bool press = false, bool held = true, bool ground = false,
        float wall = 0f, float axis = 0f, bool dash = false, float dt = 1f / 60f) =>
        motor.Step(dt, axis, press, held, dash, false, ground, wall, 1f);

    [Fact]
    public void GroundJumpAndOneAirJumpThenLandingRestoresBothAbilities()
    {
        var motor = new FreeControlMotor();
        Step(motor, press: true, ground: true);
        Assert.Equal(-FreeControlMotor.JumpSpeed, motor.Velocity.Y);
        Assert.True(motor.HasAirJump);
        Step(motor);
        Step(motor, press: true);
        Assert.False(motor.HasAirJump);
        Step(motor); Step(motor, press: true);
        Assert.True(motor.Velocity.Y > -FreeControlMotor.JumpSpeed);
        motor.Velocity = Vector2.Zero;
        Step(motor, ground: true, held: false, dt: .12f);
        Assert.True(motor.HasAirJump && motor.HasAirDash);
    }

    [Fact]
    public void CoyoteJumpDoesNotConsumeAirJumpAndReleaseCutsHeight()
    {
        var motor = new FreeControlMotor();
        Step(motor, ground: true); Step(motor);
        Step(motor, press: true);
        Assert.True(motor.HasAirJump);
        Step(motor, held: false);
        Assert.InRange(motor.Velocity.Y, -341f, -339f);
    }

    [Fact]
    public void LandingConsumesBufferedPressOnce()
    {
        var motor = new FreeControlMotor();
        Step(motor, press: true); // consume the air jump
        for (int i = 0; i < 7; i++) Step(motor);
        Step(motor, press: true);
        motor.Velocity = new(0f, 300f);
        Step(motor, ground: true);
        Assert.Equal(-850f, motor.Velocity.Y);
        Step(motor);
        Assert.True(motor.Velocity.Y > -850f);
    }

    [Fact]
    public void WallSlideAndWallJumpPushAwayAndRestoreAirActions()
    {
        var motor = new FreeControlMotor();
        motor.Reset(new(0f, 600f));
        Step(motor, wall: -1f, axis: 1f);
        Assert.Equal(160f, motor.Velocity.Y);
        Step(motor, press: true, wall: -1f, axis: 1f);
        Assert.Equal(new Vector2(-520f, -850f), motor.Velocity);
        Step(motor, axis: 1f);
        Assert.Equal(-520f, motor.Velocity.X);
    }

    [Fact]
    public void DashHasFixedWindowAndOnlyOneAirUse()
    {
        var motor = new FreeControlMotor();
        Step(motor, dash: true, axis: -1f);
        Assert.Equal(new Vector2(-1200f, 0f), motor.Velocity);
        Assert.False(motor.HasAirDash);
        for (int i = 0; i < 30; i++) Step(motor);
        Assert.False(motor.Dashing);
        Step(motor, dash: true);
        Assert.False(motor.Dashing);
    }

    [Theory]
    [InlineData(0f, 0)] [InlineData(199f, 0)] [InlineData(200f, 1)]
    [InlineData(400f, 4)] [InlineData(1000f, 25)] [InlineData(3000f, 100)]
    public void ContactDamageUsesSpeedThresholdAndCap(float speed, int damage) =>
        Assert.Equal(damage, FreeControlMotor.CollisionDamage(speed));
}
