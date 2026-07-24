using Godot;
using MegaCrit.Sts2.Core.Nodes;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Transition;

internal static class NinjaSlayerTransitionVideoPrewarmer
{
    private const int MaxAttempts = 2;
    private static readonly object SyncRoot = new();
    private static readonly TransitionVideoPrewarmState State = new(MaxAttempts);
    private static NinjaSlayerTransitionOverlay? _prewarmedOverlay;
    private static int _failureLogged;
    private static int _handoffLogged;

    public static void TryStart()
    {
        NGame? game = NGame.Instance;
        if (game is null || !GodotObject.IsInstanceValid(game.Transition))
        {
            return;
        }

        NinjaSlayerTransitionVideo.BeginPreload();

        long generation;
        lock (SyncRoot)
        {
            if (!State.TryBegin(out generation))
            {
                return;
            }
        }

        NinjaSlayerTransitionOverlay? overlay = null;
        try
        {
            overlay = NinjaSlayerTransitionOverlay.GetOrCreate(game.Transition);
            lock (SyncRoot)
            {
                _prewarmedOverlay = overlay;
            }

            if (!overlay.TryStartDecoderPrewarm(generation))
            {
                Fail(overlay, generation, "the official transition player was already busy");
                return;
            }

            Entry.Logger.Info("NinjaSlayer official transition player decoder prewarm started.");
        }
        catch (Exception ex)
        {
            Fail(overlay, generation, $"could not initialize the official transition player: {ex.Message}");
        }
    }

    public static void PrepareForPlayback()
    {
        NinjaSlayerTransitionOverlay? overlay;
        TransitionVideoPrewarmPhase phase;
        lock (SyncRoot)
        {
            phase = State.Phase;
            State.BeginPlayback();
            overlay = _prewarmedOverlay;
            _prewarmedOverlay = null;
        }

        if (Interlocked.Exchange(ref _handoffLogged, 1) == 0)
        {
            string detail = overlay is not null && GodotObject.IsInstanceValid(overlay)
                ? overlay.GetDecoderPrewarmDiagnostic()
                : "overlay=unavailable";
            Entry.Logger.Info($"NinjaSlayer transition prewarm handoff: phase={phase}, {detail}.");
        }

        if (overlay is null || !GodotObject.IsInstanceValid(overlay))
        {
            return;
        }

        try
        {
            overlay.StopDecoderPrewarmForPlayback();
        }
        catch (Exception ex)
        {
            LogFailureOnce($"could not hand the prewarmed player to formal playback: {ex.Message}");
        }
    }

    internal static bool Complete(NinjaSlayerTransitionOverlay overlay, long generation)
    {
        bool completed;
        lock (SyncRoot)
        {
            completed = ReferenceEquals(_prewarmedOverlay, overlay) && State.TryMarkWarmed(generation);
        }

        if (completed)
        {
            Entry.Logger.Info("NinjaSlayer official transition player completed a full decoder prewarm.");
        }

        return completed;
    }

    internal static void Fail(
        NinjaSlayerTransitionOverlay? overlay,
        long generation,
        string diagnostic)
    {
        bool failed;
        lock (SyncRoot)
        {
            failed = State.TryReturnToIdle(generation);
            if (failed)
            {
                _prewarmedOverlay = null;
            }
        }

        if (!failed)
        {
            return;
        }

        NinjaSlayerTransitionVideo.AllowPreloadRetry();
        if (overlay is not null && GodotObject.IsInstanceValid(overlay))
        {
            overlay.AbortDecoderPrewarm(clearStream: true);
        }
        LogFailureOnce($"decoder prewarm failed; formal playback will continue normally ({diagnostic})");
    }

    internal static void NotifyOverlayExited(NinjaSlayerTransitionOverlay overlay, long generation)
    {
        lock (SyncRoot)
        {
            if (!ReferenceEquals(_prewarmedOverlay, overlay) || !State.TryReturnToIdle(generation))
            {
                return;
            }

            _prewarmedOverlay = null;
        }
    }

    private static void LogFailureOnce(string diagnostic)
    {
        if (Interlocked.Exchange(ref _failureLogged, 1) == 0)
        {
            Entry.Logger.Warn($"NinjaSlayer transition video {diagnostic}.");
        }
    }
}
