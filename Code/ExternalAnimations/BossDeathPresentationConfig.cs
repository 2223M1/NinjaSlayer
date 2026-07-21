using Godot;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed record BossDeathPartSpec(
    string MonsterId,
    string BoneName,
    float LaunchAngleDegrees,
    float FlightSpeedPixelsPerSecond,
    float RotationSpeedDegreesPerSecond,
    float MaximumFlightSeconds = 2f);

internal static class BossDeathPresentationConfig
{
    // Add verified boss bone specifications here. An empty registry intentionally leaves every
    // boss on its original death animation while the shared soul/explosion ending remains active.
    private static readonly IReadOnlyDictionary<string, BossDeathPartSpec> PartSpecs =
        new Dictionary<string, BossDeathPartSpec>(StringComparer.Ordinal);

    public static bool TryGetPartSpec(string monsterId, out BossDeathPartSpec spec) =>
        PartSpecs.TryGetValue(monsterId, out spec!);

    public static Vector2 GetVelocity(BossDeathPartSpec spec) =>
        Vector2.Right.Rotated(Mathf.DegToRad(spec.LaunchAngleDegrees))
        * spec.FlightSpeedPixelsPerSecond;
}
