using NinjaSlayer.Code.Lifecycle;

namespace NinjaSlayer.Code.Combat;

internal static class AttackIntentPreviewContext
{
    private static readonly AsyncScopeDepth Scope = new();

    public static bool IsActive => Scope.IsActive;

    public static IDisposable Enter() => Scope.Enter();
}
