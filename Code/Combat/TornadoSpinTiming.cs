namespace NinjaSlayer.Code.Combat;

internal enum TornadoAudioMode { PerHit, Spin }

internal static class TornadoSpinTiming
{
    // Exact source-frame durations; the source WAVs are never rewritten.
    internal const float IntroSeconds = 1.3699773242630386f;
    // Outro starts at the final hit and may outlast the animation; only Intro must fit before impact.
    internal static float SecondsToFinalHit(int hits, CombatActionSpeed speed) => hits <= 0 ? 0f :
        hits * CombatActionTiming.Resolve(speed, CombatActionTiming.AttackNormalSeconds, CombatActionTiming.AttackFastSeconds)
        + (hits - 1) * CombatActionTiming.Resolve(speed, CombatActionTiming.DamageRecoveryNormalSeconds, CombatActionTiming.DamageRecoveryFastSeconds);

    internal static TornadoAudioMode Select(int hits, float seconds, bool narakuVoice) =>
        hits < 4 || narakuVoice || seconds < IntroSeconds ? TornadoAudioMode.PerHit : TornadoAudioMode.Spin;
}
