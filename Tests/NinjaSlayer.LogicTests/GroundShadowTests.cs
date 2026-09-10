using System.Numerics;
using NinjaSlayer.Code.Combat;
using Xunit;

namespace NinjaSlayer.LogicTests;

public class GroundShadowTests
{
    [Fact]
    public void SupportFollowsTheLowBodyBandIncludingRotation()
    {
        Vector2[] standing = [new(-20, -200), new(20, -200), new(40, 0), new(-40, 0)];
        ShadowSupport idle = GroundShadowMath.Measure(standing);
        Assert.Equal(80f, idle.Width);
        Vector2[] lying = standing.Select(p => Vector2.Transform(p, Matrix3x2.CreateRotation(MathF.PI / 2))).ToArray();
        ShadowSupport turned = GroundShadowMath.Measure(lying);
        Assert.True(turned.Center > idle.Center);
        Assert.True(float.IsFinite(turned.Width));
        ShadowSupport moved = GroundShadowMath.Measure(standing.Select(p => p + new Vector2(120, -80)).ToArray());
        Assert.Equal(idle.Width, moved.Width);
        Assert.Equal(idle.Center + 120, moved.Center);
        Assert.Equal(idle.Bottom - 80, moved.Bottom);
    }

    [Fact]
    public void SpinNeverLosesItsGroundFootprintAndReturnsExactly()
    {
        Assert.Equal(1f, GroundShadowMath.SpinWidth(0));
        Assert.Equal(.55f, GroundShadowMath.SpinWidth(MathF.PI / 2), 5);
        Assert.Equal(1f, GroundShadowMath.SpinWidth(MathF.PI), 5);
        for (int degree = 0; degree <= 720; degree++)
            Assert.InRange(GroundShadowMath.SpinWidth(degree * MathF.PI / 180), .54999f, 1.00001f);
    }

    [Fact]
    public void AltitudeFadesContinuouslyWithoutAnArbitraryCutoff()
    {
        Assert.Equal((1f, 1f, 1f), GroundShadowMath.Airborne(0, 300));
        Assert.Equal((1f, 1f, 1f), GroundShadowMath.Airborne(-20, 300));
        var low = GroundShadowMath.Airborne(100, 300);
        var high = GroundShadowMath.Airborne(400, 300);
        Assert.InRange(high.Alpha, 0.01f, low.Alpha);
        Assert.InRange(high.Width, .78f, low.Width);
        Assert.InRange(high.Depth, .85f, low.Depth);
        Assert.True(Math.Abs(GroundShadowMath.Airborne(299, 300).Alpha - GroundShadowMath.Airborne(301, 300).Alpha) < .01f);
    }

    [Fact]
    public void ActionAccentsRetainNativeIroncladProportions()
    {
        Assert.Equal(new Vector2(1.2475132f, .7855182f), GroundShadowMath.ActionScale(ShadowActionKind.Attack));
        Assert.Equal(new Vector2(1.0878955f, 1.0878956f), GroundShadowMath.ActionScale(ShadowActionKind.SlowAttack));
        Assert.True(GroundShadowMath.ActionScale(ShadowActionKind.Hurt).X < 1);
        Assert.Equal(1f, GroundShadowMath.ActionScale(ShadowActionKind.Cast).Y);
    }
}
