using NinjaSlayer.Code.Lifecycle;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class XAttackAudioContext
{
    private static readonly AsyncScopeDepth Suppression = new();

    public static bool SuppressAutomaticSfx => Suppression.IsActive;

    public static IDisposable Suppress() => Suppression.Enter();
}
