using MegaCrit.Sts2.Core.Commands;
using NinjaSlayer.Code.Lifecycle;

namespace NinjaSlayer.Code.Combat;

internal static class TornadoFistFinisherCadenceContext
{
    private static readonly AsyncScopeDepth ActiveScope = new();

    public static bool IsActive => ActiveScope.IsActive;

    public static async Task<T> Run<T>(Func<Task<T>> action)
    {
        using IDisposable scope = ActiveScope.Enter();
        return await action();
    }

    public static Task WaitUnlessActive(
        float fastSeconds,
        float standardSeconds,
        bool ignoreCombatEnd,
        CancellationToken cancellationToken) =>
        IsActive
            ? Task.CompletedTask
            : Cmd.CustomScaledWait(fastSeconds, standardSeconds, ignoreCombatEnd, cancellationToken);
}
