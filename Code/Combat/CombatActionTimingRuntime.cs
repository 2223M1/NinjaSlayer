using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;

namespace NinjaSlayer.Code.Combat;

internal static class CombatActionTimingRuntime
{
    public static CombatActionSpeed CurrentSpeed => SaveManager.Instance.PrefsSave.FastMode switch
    {
        FastModeType.Normal => CombatActionSpeed.Normal,
        FastModeType.Fast => CombatActionSpeed.Fast,
        FastModeType.Instant => CombatActionSpeed.Instant,
        _ => throw new ArgumentOutOfRangeException()
    };

    public static float AttackSeconds => Resolve(
        CombatActionTiming.AttackNormalSeconds,
        CombatActionTiming.AttackFastSeconds);

    public static float SlowAttackSeconds => Resolve(
        CombatActionTiming.SlowAttackNormalSeconds,
        CombatActionTiming.SlowAttackFastSeconds);

    public static float CastSeconds => Resolve(
        CombatActionTiming.CastNormalSeconds,
        CombatActionTiming.CastFastSeconds);

    public static float DamageRecoverySeconds => Resolve(
        CombatActionTiming.DamageRecoveryNormalSeconds,
        CombatActionTiming.DamageRecoveryFastSeconds);

    public static float ConsecutiveAttackSeconds => Resolve(
        CombatActionTiming.ConsecutiveAttackNormalSeconds,
        CombatActionTiming.ConsecutiveAttackFastSeconds);

    public static float CompanionSlowAttackSeconds => CombatActionTiming.ResolveCompanion(
        CurrentSpeed,
        CombatActionTiming.SlowAttackNormalSeconds);

    public static float CompanionDamageRecoverySeconds => CombatActionTiming.ResolveCompanion(
        CurrentSpeed,
        CombatActionTiming.DamageRecoveryNormalSeconds);

    public static float CompanionConsecutiveAttackSeconds => CombatActionTiming.ResolveCompanion(
        CurrentSpeed,
        CombatActionTiming.ConsecutiveAttackNormalSeconds);

    public static float Resolve(float normalSeconds, float fastSeconds) =>
        CombatActionTiming.Resolve(CurrentSpeed, normalSeconds, fastSeconds);
}
