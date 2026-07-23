using NinjaSlayer.Code.Lifecycle;

namespace NinjaSlayer.Code.Combat;

public static class ScreenShakeSuppressionContext
{
    private static readonly AsyncScopeDepth Suppression = new();

    public static bool IsSuppressed => Suppression.IsActive;

    public static IDisposable Suppress() => Suppression.Enter();
}
