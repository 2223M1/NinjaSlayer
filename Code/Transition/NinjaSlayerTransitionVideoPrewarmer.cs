using Godot;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Transition;

internal static class NinjaSlayerTransitionVideoPrewarmer
{
    private const int MaxAttempts = 2;
    private static readonly object SyncRoot = new();
    private static readonly TransitionVideoPrewarmState State = new(MaxAttempts);
    private static NinjaSlayerTransitionPrewarmPlayer? _activePlayer;
    private static int _failureLogged;

    public static void TryStart(Node owner)
    {
        if (!GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree())
        {
            return;
        }

        NinjaSlayerTransitionVideo.BeginPreload();

        NinjaSlayerTransitionPrewarmPlayer player;
        long generation;
        lock (SyncRoot)
        {
            if (!State.TryBegin(out generation))
            {
                return;
            }

            player = new NinjaSlayerTransitionPrewarmPlayer
            {
                Name = NinjaSlayerTransitionPrewarmPlayer.NodeName
            };
            player.Configure(generation);
            _activePlayer = player;
        }

        try
        {
            owner.AddChild(player);
        }
        catch (Exception ex)
        {
            Fail(player, generation, $"could not attach the prewarm player: {ex.Message}");
        }
    }

    public static void PrepareForPlayback()
    {
        NinjaSlayerTransitionPrewarmPlayer? player = null;
        lock (SyncRoot)
        {
            long? interruptedGeneration = State.BeginPlayback();
            if (interruptedGeneration.HasValue
                && _activePlayer is { } active
                && active.Generation == interruptedGeneration.Value)
            {
                player = active;
                _activePlayer = null;
            }
        }

        StopWithoutThrowing(player, "formal playback takeover");
    }

    internal static void Complete(NinjaSlayerTransitionPrewarmPlayer player, long generation)
    {
        bool completed;
        lock (SyncRoot)
        {
            completed = ReferenceEquals(_activePlayer, player) && State.TryMarkWarmed(generation);
            if (completed)
            {
                _activePlayer = null;
            }
        }

        if (!completed)
        {
            return;
        }

        StopWithoutThrowing(player, "prewarm completion");
        Entry.Logger.Info("NinjaSlayer transition video decoder prewarm completed.");
    }

    internal static void Fail(
        NinjaSlayerTransitionPrewarmPlayer player,
        long generation,
        string diagnostic)
    {
        bool failed;
        lock (SyncRoot)
        {
            failed = ReferenceEquals(_activePlayer, player) && State.TryReturnToIdle(generation);
            if (failed)
            {
                _activePlayer = null;
            }
        }

        if (!failed)
        {
            return;
        }

        NinjaSlayerTransitionVideo.AllowPreloadRetry();
        StopWithoutThrowing(player, "prewarm failure");
        if (Interlocked.Exchange(ref _failureLogged, 1) == 0)
        {
            Entry.Logger.Warn($"NinjaSlayer transition video decoder prewarm failed; formal playback will continue normally ({diagnostic}).");
        }
    }

    internal static void NotifyExited(NinjaSlayerTransitionPrewarmPlayer player, long generation)
    {
        lock (SyncRoot)
        {
            if (!ReferenceEquals(_activePlayer, player) || !State.TryReturnToIdle(generation))
            {
                return;
            }

            _activePlayer = null;
        }
    }

    private static void StopWithoutThrowing(
        NinjaSlayerTransitionPrewarmPlayer? player,
        string reason)
    {
        if (player is null || !GodotObject.IsInstanceValid(player))
        {
            return;
        }

        try
        {
            player.StopAndRelease();
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"Could not stop NinjaSlayer transition decoder prewarm during {reason}: {ex}");
        }
    }
}
