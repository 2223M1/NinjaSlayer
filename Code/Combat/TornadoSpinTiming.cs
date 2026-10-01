namespace NinjaSlayer.Code.Combat;

internal enum TornadoAudioMode { PerHit, Spin }

internal static class TornadoSpinTiming
{
    // Exact source-frame durations; the source WAVs are never rewritten.
    internal const float IntroSeconds = 1.3699773242630386f;
    internal const float OutroSeconds = 1.1145578231292517f;
    internal const float ShortSeconds = IntroSeconds + OutroSeconds;

    internal static float RemainingSeconds(int hits, CombatActionSpeed speed) => hits <= 0 ? 0f :
        hits * CombatActionTiming.Resolve(speed, CombatActionTiming.AttackNormalSeconds, CombatActionTiming.AttackFastSeconds)
        + (hits - 1) * CombatActionTiming.Resolve(speed, CombatActionTiming.DamageRecoveryNormalSeconds, CombatActionTiming.DamageRecoveryFastSeconds)
        + CombatActionTiming.Presentation(speed, 0.1f);

    internal static TornadoAudioMode Select(int hits, float seconds, bool narakuVoice) =>
        hits < 4 || narakuVoice || seconds < ShortSeconds ? TornadoAudioMode.PerHit : TornadoAudioMode.Spin;
}
