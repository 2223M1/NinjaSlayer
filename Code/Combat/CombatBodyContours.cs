using System.Numerics;

namespace NinjaSlayer.Code.Combat;

internal static class CombatBodyContours
{
    // Centered texture pixels. Alpha >= 32, keyed green removed; the authored subject
    // masks exclude trailing cloth, smoke and the katana before taking the convex hull.
    internal static readonly Vector2[] NinjaSlayer =
    [
        new(250,500),new(329,-12),new(342,-44),new(415,-185),new(577,-266),
        new(604,-276),new(610,-278),new(615,-279),new(628,-281),new(632,-280),
        new(638,-278),new(652,-270),new(657,-267),new(857,-108),new(859,-104),
        new(860,-92),new(860,-88),new(858,-76),new(762,499),new(761,501),
        new(752,507),new(749,508),new(745,509),new(295,535),new(279,532),
        new(270,528),new(268,526),new(250,502)
    ];

    internal static readonly Vector2[] YamotoKoki =
    [
        new(-42.5f,-2),new(-41.5f,-13),new(-23.5f,-91),new(10.5f,-170),
        new(11.5f,-172),new(37.5f,-219),new(42.5f,-228),new(45.5f,-233),
        new(49.5f,-238),new(52.5f,-241),new(57.5f,-245),new(60.5f,-247),
        new(70.5f,-252),new(86.5f,-250),new(98.5f,-246),new(106.5f,-241),
        new(116.5f,-231),new(121.5f,-221),new(205.5f,20),new(225.5f,227),
        new(225.5f,235),new(224.5f,237),new(222.5f,239),new(218.5f,241),
        new(-4.5f,252),new(-11.5f,251),new(-14.5f,250),new(-18.5f,248),
        new(-22.5f,245),new(-25.5f,242),new(-27.5f,239),new(-42.5f,5)
    ];

    internal static readonly Vector2[] FullyReleasedNaraku =
    [
        new(-557,319),new(-556,311),new(-554,302),new(-553,298),new(-497,90),
        new(-496,88),new(-348,-134),new(-347,-135),new(-75,-317),new(-72,-319),
        new(87,-316),new(320,-216),new(570,381),new(570,388),new(568,396),
        new(566,402),new(541,471),new(538,476),new(533,481),new(527,486),
        new(525,487),new(422,497),new(-273,428),new(-282,427),new(-485,402),
        new(-491,399),new(-498,395),new(-504,391),new(-508,388),new(-513,384),
        new(-515,382),new(-520,376),new(-526,368),new(-555,329),new(-557,326)
    ];
}
