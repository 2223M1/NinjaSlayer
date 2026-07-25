using System.Diagnostics;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Transition;

internal readonly record struct TransitionSeekPrimerHandoff(
    VideoStreamPlayer? Player,
    bool EnableFrameCorrection,
    string Status);

internal static class NinjaSlayerTransitionSeekPrimer
{
    private const double StreamLoadTimeoutSeconds = 8.0;
    private const double DecoderStartTimeoutSeconds = 1.0;
    private const double FirstProbePositionSeconds = 0.75;
    private const double ValidationProbePositionSeconds = 1.25;
    private static readonly object SyncRoot = new();

    private static PrimerPhase phase;
    private static long generation;
    private static SubViewport? probeViewport;
    private static VideoStreamPlayer? probePlayer;
    private static bool correctionValidated;
    private static int failureLogged;

    public static void TryStart()
    {
        NGame? game = NGame.Instance;
        if (game is null || !GodotObject.IsInstanceValid(game) || !game.IsInsideTree())
        {
            return;
        }

        long currentGeneration;
        lock (SyncRoot)
        {
            if (phase != PrimerPhase.Idle)
            {
                return;
            }

            phase = PrimerPhase.Running;
            currentGeneration = ++generation;
        }

        TaskHelper.RunSafely(RunAsync(game, currentGeneration));
    }

    public static TransitionSeekPrimerHandoff TakeForPlayback()
    {
        VideoStreamPlayer? player = null;
        VideoStreamPlayer? playerToDiscard = null;
        SubViewport? viewport = null;
        bool enableCorrection = false;
        string status;

        lock (SyncRoot)
        {
            PrimerPhase previous = phase;
            generation++;
            phase = PrimerPhase.Claimed;
            status = previous == PrimerPhase.Ready && !correctionValidated
                ? "ready_slow"
                : previous.ToString().ToLowerInvariant();
            if (previous == PrimerPhase.Ready)
            {
                player = probePlayer;
                enableCorrection = correctionValidated;
            }
            else
            {
                playerToDiscard = probePlayer;
            }

            viewport = probeViewport;
            probePlayer = null;
            probeViewport = null;
        }

        if (player is not null && GodotObject.IsInstanceValid(player))
        {
            player.Stop();
            player.GetParent()?.RemoveChild(player);
        }
        else
        {
            player = null;
            enableCorrection = false;
        }

        if (playerToDiscard is not null && GodotObject.IsInstanceValid(playerToDiscard))
        {
            playerToDiscard.Stop();
        }

        if (viewport is not null && GodotObject.IsInstanceValid(viewport))
        {
            viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            viewport.QueueFreeSafely();
        }

        return new TransitionSeekPrimerHandoff(player, enableCorrection, status);
    }

    private static async Task RunAsync(NGame game, long currentGeneration)
    {
        SubViewport? viewport = null;
        VideoStreamPlayer? player = null;
        bool retainedForPlayback = false;
        try
        {
            VideoStream? stream = await WaitForStreamAsync(game, currentGeneration);
            if (stream is null || !IsCurrent(currentGeneration))
            {
                return;
            }

            viewport = CreateProbeViewport();
            player = CreateProbePlayer(stream);
            viewport.AddChild(player);
            game.AddChildSafely(viewport);
            if (!RegisterProbe(currentGeneration, viewport, player))
            {
                return;
            }

            player.Play();
            long decoderStartedAt = Stopwatch.GetTimestamp();
            while (player.IsPlaying()
                   && player.StreamPosition < TransitionFrameDropClock.FrameDurationSeconds * 2.0)
            {
                if (!IsCurrent(currentGeneration)
                    || Stopwatch.GetElapsedTime(decoderStartedAt).TotalSeconds >= DecoderStartTimeoutSeconds)
                {
                    Fail(currentGeneration, "decoder did not advance before the probe timeout");
                    return;
                }

                await game.AwaitProcessFrame();
            }

            if (!IsCurrent(currentGeneration))
            {
                return;
            }

            if (!player.IsPlaying()
                && player.StreamPosition < TransitionFrameDropClock.FrameDurationSeconds * 2.0)
            {
                Fail(currentGeneration, "decoder stopped before the probe could begin");
                return;
            }

            TimeSpan coldSeek = MeasureSeek(player, FirstProbePositionSeconds);
            await game.AwaitProcessFrame();
            if (!IsCurrent(currentGeneration))
            {
                return;
            }

            TimeSpan validatedSeek = MeasureSeek(player, ValidationProbePositionSeconds);
            await game.AwaitProcessFrame();
            if (!IsCurrent(currentGeneration))
            {
                return;
            }

            player.Stop();

            bool validated = TransitionSeekPrimerPolicy.CanEnableFrameCorrection(validatedSeek);
            if (!MarkReady(currentGeneration, validated))
            {
                return;
            }

            retainedForPlayback = true;
            Entry.Logger.Info(
                $"NinjaSlayer transition seek primer: status=ready, " +
                $"cold_seek={coldSeek.TotalMilliseconds:F2}ms, " +
                $"validated_seek={validatedSeek.TotalMilliseconds:F2}ms, " +
                $"frame_correction={(validated ? "enabled" : "disabled")}.");
        }
        catch (Exception ex)
        {
            Fail(currentGeneration, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (!retainedForPlayback)
            {
                ClearRegisteredProbe(currentGeneration, viewport, player);
                if (viewport is not null && GodotObject.IsInstanceValid(viewport))
                {
                    viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
                    viewport.QueueFreeSafely();
                }
                else if (player is not null && GodotObject.IsInstanceValid(player))
                {
                    player.QueueFreeSafely();
                }
            }
        }
    }

    private static async Task<VideoStream?> WaitForStreamAsync(NGame game, long currentGeneration)
    {
        long startedAt = Stopwatch.GetTimestamp();
        while (IsCurrent(currentGeneration))
        {
            TransitionVideoLoadPollResult result =
                NinjaSlayerTransitionVideo.PollPreloadedStream(
                    out VideoStream? stream,
                    out string? diagnostic);
            if (result == TransitionVideoLoadPollResult.Loaded)
            {
                return stream;
            }

            if (result == TransitionVideoLoadPollResult.Failed)
            {
                Fail(currentGeneration, diagnostic ?? "video stream preload failed");
                return null;
            }

            if (Stopwatch.GetElapsedTime(startedAt).TotalSeconds >= StreamLoadTimeoutSeconds)
            {
                Fail(currentGeneration, "video stream preload timed out");
                return null;
            }

            await game.AwaitProcessFrame();
        }

        return null;
    }

    private static TimeSpan MeasureSeek(VideoStreamPlayer player, double positionSeconds)
    {
        long startedAt = Stopwatch.GetTimestamp();
        player.StreamPosition = positionSeconds;
        return Stopwatch.GetElapsedTime(startedAt);
    }

    private static bool RegisterProbe(
        long currentGeneration,
        SubViewport viewport,
        VideoStreamPlayer player)
    {
        lock (SyncRoot)
        {
            if (phase != PrimerPhase.Running || generation != currentGeneration)
            {
                return false;
            }

            probeViewport = viewport;
            probePlayer = player;
            return true;
        }
    }

    private static bool MarkReady(long currentGeneration, bool validated)
    {
        lock (SyncRoot)
        {
            if (phase != PrimerPhase.Running || generation != currentGeneration)
            {
                return false;
            }

            correctionValidated = validated;
            phase = PrimerPhase.Ready;
            return true;
        }
    }

    private static void ClearRegisteredProbe(
        long currentGeneration,
        SubViewport? viewport,
        VideoStreamPlayer? player)
    {
        lock (SyncRoot)
        {
            if (generation != currentGeneration)
            {
                return;
            }

            if (ReferenceEquals(probeViewport, viewport))
            {
                probeViewport = null;
            }

            if (ReferenceEquals(probePlayer, player))
            {
                probePlayer = null;
            }
        }
    }

    private static bool IsCurrent(long currentGeneration)
    {
        lock (SyncRoot)
        {
            return phase == PrimerPhase.Running && generation == currentGeneration;
        }
    }

    private static void Fail(long currentGeneration, string diagnostic)
    {
        lock (SyncRoot)
        {
            if (phase != PrimerPhase.Running || generation != currentGeneration)
            {
                return;
            }

            phase = PrimerPhase.Failed;
        }

        if (Interlocked.Exchange(ref failureLogged, 1) == 0)
        {
            Entry.Logger.Warn(
                $"NinjaSlayer transition seek primer unavailable; formal playback will continue " +
                $"without corrective Seek ({diagnostic}).");
        }
    }

    private static SubViewport CreateProbeViewport() => new()
    {
        Name = "NinjaSlayerTransitionSeekProbeViewport",
        Size = Vector2I.One,
        Size2DOverride = Vector2I.One,
        TransparentBg = true,
        Disable3D = true,
        GuiDisableInput = true,
        HandleInputLocally = false,
        AudioListenerEnable2D = false,
        AudioListenerEnable3D = false,
        ProcessMode = Node.ProcessModeEnum.Always,
        RenderTargetUpdateMode = SubViewport.UpdateMode.Always
    };

    private static VideoStreamPlayer CreateProbePlayer(VideoStream stream) => new()
    {
        Name = "NinjaSlayerTransitionSeekProbePlayer",
        Stream = stream,
        Volume = 0f,
        Expand = true,
        Visible = true,
        ProcessMode = Node.ProcessModeEnum.Always
    };

    private enum PrimerPhase
    {
        Idle,
        Running,
        Ready,
        Claimed,
        Failed
    }
}
