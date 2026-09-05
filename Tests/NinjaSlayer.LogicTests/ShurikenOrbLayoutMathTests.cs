using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class ShurikenOrbLayoutMathTests
{
    [Fact]
    public void FirstStandardOrbMatchesVanillaSingleSlotPosition()
    {
        float radius = 225f + (300f - 225f) * (-2f / 7f);
        float angle = MathF.PI / 180f * -150f;
        var expected = new ShurikenOrbSlotPosition(
            -MathF.Cos(angle) * radius,
            MathF.Sin(angle) * radius);

        AssertClose(expected, ShurikenOrbLayoutMath.GetStandardPosition(1, 0, isLocal: true));
    }

    [Fact]
    public void RemovingShurikenFromNumberingMatchesVanillaRemainingCapacity()
    {
        ShurikenOrbSlotPosition[] positions = Enumerable.Range(0, 3)
            .Select(index => ShurikenOrbLayoutMath.GetStandardPosition(3, index, isLocal: true))
            .ToArray();

        AssertClose(new ShurikenOrbSlotPosition(194.85572f, -112.5f), positions[0]);
        AssertClose(new ShurikenOrbSlotPosition(-9.81353f, -224.78586f), positions[1]);
        AssertClose(new ShurikenOrbSlotPosition(-203.91925f, -95.08909f), positions[2]);
    }

    [Fact]
    public void RemoteLayoutUsesVanillaThreeQuarterRadius()
    {
        for (int capacity = 1; capacity <= 9; capacity++)
        {
            for (int index = 0; index < capacity; index++)
            {
                ShurikenOrbSlotPosition local =
                    ShurikenOrbLayoutMath.GetStandardPosition(capacity, index, true);
                ShurikenOrbSlotPosition remote =
                    ShurikenOrbLayoutMath.GetStandardPosition(capacity, index, false);
                AssertClose(
                    new ShurikenOrbSlotPosition(local.X * 0.75f, local.Y * 0.75f),
                    remote);
            }
        }
    }

    private static void AssertClose(
        ShurikenOrbSlotPosition expected,
        ShurikenOrbSlotPosition actual)
    {
        float dx = expected.X - actual.X;
        float dy = expected.Y - actual.Y;
        Assert.InRange(MathF.Sqrt(dx * dx + dy * dy), 0f, 0.001f);
    }
}
