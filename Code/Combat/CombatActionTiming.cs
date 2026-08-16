namespace NinjaSlayer.Code.Combat;

internal enum CombatActionSpeed
{
    Normal,
    Fast,
    Instant
}

internal static class CombatActionTiming
{
    public const float AttackNormalSeconds = 0.15f;
    public const float AttackFastSeconds = 0.075f;
    public const float SlowAttackNormalSeconds = 0.2f;
    public const float SlowAttackFastSeconds = 0.1f;
    public const float CastNormalSeconds = 0.25f;
    public const float CastFastSeconds = 0.125f;
    public const float DamageRecoveryNormalSeconds = 0.2f;
    public const float DamageRecoveryFastSeconds = 0.1f;
    public const float ConsecutiveAttackNormalSeconds = 0.15f;
    public const float ConsecutiveAttackFastSeconds = 0.075f;

    public static float Resolve(
        CombatActionSpeed speed,
        float normalSeconds,
        float fastSeconds) => speed switch
        {
            CombatActionSpeed.Normal => normalSeconds,
            CombatActionSpeed.Fast => fastSeconds,
            CombatActionSpeed.Instant => 0f,
            _ => throw new ArgumentOutOfRangeException(nameof(speed), speed, null)
        };
}
