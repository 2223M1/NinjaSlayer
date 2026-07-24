using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class ExtendedHealthBarLayoutTests
{
    [Fact]
    public void ExtraLifeExtendsRightWithoutMovingTheBarOrBlockLeftEdges()
    {
        ExtendedHealthBarLayout vanilla = ExtendedHealthBarLayoutCalculator.Calculate(
            creatureBoundsLeft: 100f,
            creatureBoundsWidth: 250f,
            vanillaPadding: 0f,
            widthMultiplier: 1f,
            blockWidth: 60f);
        ExtendedHealthBarLayout extended = ExtendedHealthBarLayoutCalculator.Calculate(
            creatureBoundsLeft: 100f,
            creatureBoundsWidth: 250f,
            vanillaPadding: 0f,
            widthMultiplier: 2f,
            blockWidth: 60f);

        Assert.Equal(vanilla.BarLeft, extended.BarLeft);
        Assert.Equal(vanilla.BlockLeft, extended.BlockLeft);
        Assert.Equal(250f, extended.BarRight - vanilla.BarRight);
    }

    [Fact]
    public void VanillaMonsterPaddingRemainsPartOfTheStableLeftAnchor()
    {
        ExtendedHealthBarLayout layout = ExtendedHealthBarLayoutCalculator.Calculate(
            creatureBoundsLeft: 80f,
            creatureBoundsWidth: 200f,
            vanillaPadding: 24f,
            widthMultiplier: 1.5f,
            blockWidth: 60f);

        Assert.Equal(68f, layout.BarLeft);
        Assert.Equal(50f, layout.BlockLeft);
        Assert.Equal(336f, layout.BarWidth);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(0.5f)]
    public void InvalidOrShrinkingMultipliersFallBackToVanillaWidth(float multiplier)
    {
        ExtendedHealthBarLayout layout = ExtendedHealthBarLayoutCalculator.Calculate(
            creatureBoundsLeft: 0f,
            creatureBoundsWidth: 250f,
            vanillaPadding: 0f,
            widthMultiplier: multiplier,
            blockWidth: 60f);

        Assert.Equal(250f, layout.BarWidth);
    }
}
