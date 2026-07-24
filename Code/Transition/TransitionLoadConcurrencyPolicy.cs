namespace NinjaSlayer.Code.Transition;

internal static class TransitionLoadConcurrencyPolicy
{
    internal const int VanillaConcurrentLoadLimit = 128;
    internal const int VisibleTransitionConcurrentLoadLimit = 8;

    public static int Resolve(bool transitionVisible) => transitionVisible
        ? VisibleTransitionConcurrentLoadLimit
        : VanillaConcurrentLoadLimit;
}
