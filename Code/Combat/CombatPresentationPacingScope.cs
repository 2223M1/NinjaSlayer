using MegaCrit.Sts2.Core.Commands;
using NinjaSlayer.Code.ExternalAnimations;

namespace NinjaSlayer.Code.Combat;

internal readonly record struct CombatPresentationPacingPolicy(
    bool SkipDamageRecovery,
    bool SkipPowerRecovery)
{
    public static CombatPresentationPacingPolicy RapidCard { get; } = new(true, true);
    public static CombatPresentationPacingPolicy PreserveDamage { get; } = new(false, true);
    public static CombatPresentationPacingPolicy ComboDamage { get; } = new(true, false);
}

internal static class CombatPresentationPacingScope
{
    private static readonly AsyncLocal<ScopeFrame?> Current = new();

    public static ScopeLease Begin(CombatPresentationPacingPolicy policy)
    {
        ScopeFrame? previous = Current.Value;
        Current.Value = new ScopeFrame(policy, previous);
        return new ScopeLease(previous);
    }

    public static Task WaitForDamageRecovery(
        float fastSeconds,
        float standardSeconds,
        bool ignoreCombatEnd,
        CancellationToken cancellationToken)
    {
        if (Current.Value?.Policy.SkipDamageRecovery == true && !NinjaSlayerAttackExecution.NeedsDamageRecovery)
        {
            NinjaSlayerAttackExecution.DeferFinalRecovery();
            return Task.CompletedTask;
        }
        if (NinjaSlayerAttackExecution.CurrentCommand?.Attacker is { } attacker)
            NinjaSlayerRapidAnimationCoordinator.BeginDamageRecovery(
                attacker, fastSeconds, standardSeconds);
        return Cmd.CustomScaledWait(
                fastSeconds,
                standardSeconds,
                ignoreCombatEnd,
                cancellationToken);
    }

    public static Task WaitForPowerRecovery(
        float fastSeconds,
        float standardSeconds,
        bool ignoreCombatEnd,
        CancellationToken cancellationToken) =>
        Current.Value?.Policy.SkipPowerRecovery == true
            ? Task.CompletedTask
            : Cmd.CustomScaledWait(
                fastSeconds,
                standardSeconds,
                ignoreCombatEnd,
                cancellationToken);

    internal sealed record ScopeFrame(
        CombatPresentationPacingPolicy Policy,
        ScopeFrame? Previous);

    internal readonly struct ScopeLease(ScopeFrame? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
