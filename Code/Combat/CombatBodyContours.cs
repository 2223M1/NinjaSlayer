using System.Numerics;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.Combat;

internal readonly record struct NinjaSlayerFormCalibration(
    float Scale, Vector2 Position, Vector2 Core, Vector2 Foot, Vector2 Hand)
{
    internal const float GroundY = -6.45f;
    internal const float FullScale = .3732865f;
    internal const float OneSoulScale = .11261151355f;

    // Centered runtime texture pixels; alternate forms center the solid body at the slot.
    internal static NinjaSlayerFormCalibration For(NinjaSlayerFormKind form) => form switch
    {
        NinjaSlayerFormKind.Normal or NinjaSlayerFormKind.Naraku => new(.33f, new(-166f, -183f),
            new(580f, 30.30303f), new(295f, 535f), new(854f, -110f)),
        NinjaSlayerFormKind.FullyReleasedNaraku => new(FullScale, new(-18.4300336f, -177.4451434f),
            new(64.99405f, 112.94429f), new(-228.06919f, 458.08017f), new(553f, 163f)),
        NinjaSlayerFormKind.OneBodyOneSoul => new(OneSoulScale, new(50.6151592f, -157.2368166f),
            new(-352.45948f, -449.28479f), new(-633.5f, 1339f), new(-794.5f, -1063f)),
        _ => throw new ArgumentOutOfRangeException(nameof(form))
    };
}

internal static class CombatBodyContours
{
    // Episode-26 delivered reconstruction: previous solid-body support mapped through
    // the reference registration; lowest foot updated from the delivered alpha.
    internal static readonly Vector2[] OneBodyOneSoul =
    [
        new(-353.442f,-1325.026f),new(-284.432f,-1296.020f),new(-252.427f,-1264.014f),
        new(-72.405f,-1015.972f),new(-55.403f,-980.966f),new(-41.402f,-922.957f),
        new(-125.438f,224.219f),new(-522.520f,1201.363f),new(-557.526f,1262.372f),
        new(-623.538f,1331.381f),new(-633.5f,1339f),new(-658.543f,1323.379f),
        new(-675.545f,1290.374f),new(-749.543f,602.265f),new(-857.532f,-756.948f),
        new(-791.515f,-1063.994f),new(-399.449f,-1319.026f)
    ];

    internal static Vector2[] ForForm(NinjaSlayerFormKind form) => form switch
    {
        NinjaSlayerFormKind.OneBodyOneSoul => OneBodyOneSoul,
        NinjaSlayerFormKind.FullyReleasedNaraku => FullyReleasedNaraku,
        _ => NinjaSlayer
    };
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

    // The accepted upright raster bakes in -6.97381292 degrees around its canvas
    // center. Rotate the solid-body support by the same affine; exclude smoke.
    internal static readonly Vector2[] FullyReleasedNaraku =
    [
        new(-514.14757f,384.26850f),new(-514.12629f,376.20627f),new(-513.23383f,367.03002f),
        new(-512.72689f,362.93820f),new(-482.39566f,149.67775f),new(-481.64589f,147.57113f),
        new(-361.69511f,-90.75598f),new(-360.82392f,-91.86999f),new(-112.93390f,-305.54858f),
        new(-110.19893f,-307.89803f),new(47.98900f,-324.22532f),new(291.40677f,-253.25500f),
        new(612.04237f,308.97432f),new(612.89228f,315.92254f),new(611.87841f,324.10618f),
        new(610.62170f,330.30462f),new(594.18433f,401.82954f),new(591.81361f,407.15679f),
        new(587.45768f,412.72688f),new(582.10915f,418.41838f),new(580.24536f,419.65382f),
        new(479.22153f,442.08565f),new(-219.01436f,457.98003f),new(-228.06919f,458.08017f),
        new(-432.60274f,457.91251f),new(-438.92260f,455.66320f),new(-446.35647f,452.54270f),
        new(-452.79775f,449.30079f),new(-457.13240f,446.80865f),new(-462.58107f,443.44532f),
        new(-464.80911f,441.70295f),new(-470.50061f,436.35441f),new(-477.42755f,429.14209f),
        new(-510.94821f,393.95168f),new(-513.29766f,391.21671f)
    ];
}
