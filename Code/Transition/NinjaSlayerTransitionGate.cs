using MegaCrit.Sts2.Core.Nodes;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Transition;

internal static class NinjaSlayerTransitionGate
{
    private static readonly object SyncRoot = new();
    private static CancellationTokenSource? _animationCancellation;
    private static Task? _ownedAnimationTask;
    internal static bool Pending { get; set; }

    /// <summary>
    /// The currently-playing transition video, started by the FadeOut patch and
    /// awaited by the reveal (RoomFadeIn/FadeIn) patches so asset loading overlaps the
    /// animation instead of producing a black hold afterwards.
    /// </summary>
    internal static Task? AnimationTask { get; private set; }

    internal static void StartAnimation(
        NTransition transition,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task> startAnimation)
    {
        CancellationTokenSource source;
        Task task;
        lock (SyncRoot)
        {
            CancelAnimationLocked();
            source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            task = startAnimation(source.Token);
            _animationCancellation = source;
            _ownedAnimationTask = task;
            AnimationTask = task;
        }

        _ = RunWatchdog(task, transition, source.Token);
    }

    internal static Task? TakeAnimation()
    {
        lock (SyncRoot)
        {
            Task? task = AnimationTask;
            AnimationTask = null;
            return task;
        }
    }

    internal static void CompleteAnimation(Task animationTask)
    {
        lock (SyncRoot)
        {
            if (!ReferenceEquals(_ownedAnimationTask, animationTask))
            {
                return;
            }

            CancelAnimationLocked();
        }
    }

    internal static void CancelPendingRequest()
    {
        Pending = false;
    }

    private static async Task RunWatchdog(
        Task animationTask,
        NTransition transition,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            lock (SyncRoot)
            {
                if (!ReferenceEquals(_ownedAnimationTask, animationTask))
                {
                    return;
                }

                Entry.Logger.Error("NinjaSlayer transition exceeded 30 seconds; forcing input and screen release.");
                CancelAnimationLocked();
            }

            NinjaSlayerTransitionPatch.ForceReleaseTransition(transition);
        }
        catch (OperationCanceledException)
        {
            bool releaseTransition;
            lock (SyncRoot)
            {
                releaseTransition = ReferenceEquals(_ownedAnimationTask, animationTask);
                if (releaseTransition)
                {
                    CancelAnimationLocked();
                }
            }

            if (releaseTransition)
            {
                NinjaSlayerTransitionPatch.ForceReleaseTransition(transition);
            }
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"NinjaSlayer transition watchdog failed: {ex}");
        }
    }

    private static void CancelAnimationLocked()
    {
        _animationCancellation?.Cancel();
        _animationCancellation?.Dispose();
        _animationCancellation = null;
        _ownedAnimationTask = null;
        AnimationTask = null;
    }
}
