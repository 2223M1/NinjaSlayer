using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class YamotoKokiGridLayoutMathTests
{
    [Fact]
    public void OnePlayerAndCompanionMatchVanillaTwoPlayerCenters()
    {
        IReadOnlyList<YamotoKokiSlotPosition> positions =
            YamotoKokiGridLayoutMath.Calculate([300f, 300f], 1f, false);

        Assert.Equal(-300f, positions[0].X);
        Assert.Equal(-670f, positions[1].X);
        Assert.Equal(200f, positions[0].Y);
        Assert.Equal(200f, positions[1].Y);
    }

    [Fact]
    public void SixteenSlotsUseAllGridRowsWithoutSameRowOverlap()
    {
        float[] widths = Enumerable.Repeat(220f, 16).ToArray();
        IReadOnlyList<YamotoKokiSlotPosition> positions =
            YamotoKokiGridLayoutMath.Calculate(widths, 1f, false);

        Assert.Equal(16, positions.Count);
        Assert.Equal(4, positions.Select(position => position.Row).Distinct().Count());
        foreach (IGrouping<int, YamotoKokiSlotPosition> row in positions.GroupBy(position => position.Row))
        {
            YamotoKokiSlotPosition[] ordered = row.OrderByDescending(position => position.X).ToArray();
            for (int i = 1; i < ordered.Length; i++)
            {
                Assert.True(MathF.Abs(ordered[i - 1].X - ordered[i].X) >= 225f);
            }
        }
    }
}
