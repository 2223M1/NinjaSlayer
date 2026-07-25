using System.Diagnostics;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using NinjaSlayer.Code.Transition;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Nodes;

[GlobalClass]
public partial class NinjaSlayerTransitionOverlay : Control
{
    public const string NodeName = "NinjaSlayerTransitionOverlay";
    private const string CanvasLayerName = "NinjaSlayerTransitionCanvasLayer";
    private const int CanvasLayerIndex = 100;
    private const float VideoAspectRatio = 16f / 9f;

    private AspectRatioContainer? aspectContainer;
    private VideoStreamPlayer? videoPlayer;
    private TransitionPerformanceTrace? performanceTrace;
    private TransitionFrameDropClock? formalFrameDropClock;
    private long formalPlaybackStartedAt;
    private bool completedFormalPlayback;

    public override void _Ready()
    {
        EnsureInitialized();
    }

    public override void _Process(double delta)
    {
        double? videoPosition = videoPlayer is not null
            && GodotObject.IsInstanceValid(videoPlayer)
            && videoPlayer.IsPlaying()
                ? videoPlayer.StreamPosition
                : null;
        performanceTrace?.RecordFrame(delta, videoPosition);
        ProcessFormalFrameDrop();
    }

    private void EnsureInitialized()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        ZAsRelative = false;
        ZIndex = 100;
        ProcessPriority = 1;
        SetProcess(true);

        if (videoPlayer != null)
        {
            return;
        }

        aspectContainer = new AspectRatioContainer
        {
            Name = "VideoAspectContainer",
            MouseFilter = MouseFilterEnum.Ignore,
            Ratio = VideoAspectRatio,
            StretchMode = AspectRatioContainer.StretchModeEnum.Cover,
            ClipContents = true
        };
        aspectContainer.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(aspectContainer);

        videoPlayer = new VideoStreamPlayer
        {
            Name = "VideoPlayer",
            MouseFilter = MouseFilterEnum.Ignore,
            Expand = true
        };
        videoPlayer.SetAnchorsPreset(LayoutPreset.FullRect);
        aspectContainer.AddChild(videoPlayer);
    }

    public async Task PlayAsync(float duration, CancellationToken cancelToken = default)
    {
        TransitionSeekPrimerHandoff primer = NinjaSlayerTransitionSeekPrimer.TakeForPlayback();
        EnsureInitialized();
        if (primer.Player is not null)
        {
            AdoptPrimedPlayer(primer.Player);
        }
        if (videoPlayer == null)
        {
            return;
        }

        using var hoverTipSuppression = NinjaSlayerHoverTipSuppression.Acquire();
        TransitionPerformanceTrace? trace = performanceTrace;
        TransitionFrameDropClock? playbackClock = null;
        long playStartedAt = 0;
        bool playStarted = false;
        try
        {
            long streamStartedAt = Stopwatch.GetTimestamp();
            VideoStream stream;
            try
            {
                stream = NinjaSlayerTransitionVideo.GetStream();
            }
            finally
            {
                trace?.RecordStreamAcquire(Stopwatch.GetElapsedTime(streamStartedAt));
            }
            videoPlayer.Stream = stream;
            videoPlayer.Volume = 1f;
            videoPlayer.Modulate = Colors.White;
            SelfModulate = Colors.White;
            Visible = true;

            playStartedAt = Stopwatch.GetTimestamp();
            try
            {
                videoPlayer.Play();
                playStarted = true;
            }
            finally
            {
                trace?.RecordPlayCall(Stopwatch.GetElapsedTime(playStartedAt));
            }
            trace?.MarkVideoStarted();

            if (primer.EnableFrameCorrection || completedFormalPlayback)
            {
                playbackClock = new TransitionFrameDropClock(Math.Max(duration, 0f));
                formalPlaybackStartedAt = playStartedAt;
                formalFrameDropClock = playbackClock;
            }
            else
            {
                trace?.RecordFrameDropDisabled(
                    $"seek_primer_{primer.Status}",
                    0.0,
                    TimeSpan.Zero);
            }
            bool firstProcessFrame = true;
            while (videoPlayer.IsPlaying()
                   && Stopwatch.GetElapsedTime(playStartedAt).TotalSeconds < Math.Max(duration, 0f))
            {
                await this.AwaitProcessFrame(cancelToken);
                if (firstProcessFrame)
                {
                    trace?.RecordFirstPostPlayFrame();
                    firstProcessFrame = false;
                }
            }
        }
        finally
        {
            ClearFormalFrameDrop(playbackClock, playStartedAt);
            trace?.MarkVideoStopped();
            videoPlayer.Stop();
            Visible = false;
            completedFormalPlayback |= playStarted;
        }
    }

    private void AdoptPrimedPlayer(VideoStreamPlayer player)
    {
        if (aspectContainer is null || !GodotObject.IsInstanceValid(player))
        {
            return;
        }

        if (videoPlayer is not null
            && GodotObject.IsInstanceValid(videoPlayer)
            && !ReferenceEquals(videoPlayer, player))
        {
            videoPlayer.Stop();
            videoPlayer.GetParent()?.RemoveChild(videoPlayer);
            videoPlayer.QueueFreeSafely();
        }

        player.Name = "VideoPlayer";
        player.MouseFilter = MouseFilterEnum.Ignore;
        player.Expand = true;
        player.ProcessMode = Node.ProcessModeEnum.Inherit;
        player.SetAnchorsPreset(LayoutPreset.FullRect);
        if (player.GetParent() is null)
        {
            aspectContainer.AddChild(player);
        }
        else if (!ReferenceEquals(player.GetParent(), aspectContainer))
        {
            player.Reparent(aspectContainer);
        }

        videoPlayer = player;
    }

    public void StopPlayback()
    {
        formalFrameDropClock = null;
        formalPlaybackStartedAt = 0;
        if (videoPlayer != null && GodotObject.IsInstanceValid(videoPlayer))
        {
            performanceTrace?.MarkVideoStopped();
            videoPlayer.Stop();
        }
        Visible = false;
    }

    private void ProcessFormalFrameDrop()
    {
        TransitionFrameDropClock? clock = formalFrameDropClock;
        if (clock == null
            || formalPlaybackStartedAt == 0
            || videoPlayer == null
            || !GodotObject.IsInstanceValid(videoPlayer)
            || !videoPlayer.IsPlaying())
        {
            return;
        }

        double wallElapsed = Stopwatch.GetElapsedTime(formalPlaybackStartedAt).TotalSeconds;
        if (clock.HasEnded(wallElapsed))
        {
            return;
        }

        TransitionFrameDropDecision decision = clock.Evaluate(wallElapsed, videoPlayer.StreamPosition);
        if (!decision.ShouldSeek)
        {
            return;
        }

        long seekStartedAt = Stopwatch.GetTimestamp();
        try
        {
            videoPlayer.StreamPosition = decision.TargetPositionSeconds;
            TimeSpan seekElapsed = Stopwatch.GetElapsedTime(seekStartedAt);
            performanceTrace?.RecordFrameDrop(decision.SkippedFrames, decision.LagSeconds, seekElapsed);
            if (seekElapsed.TotalSeconds > TransitionFrameDropClock.FrameDurationSeconds)
            {
                DisableFormalFrameDrop(clock, "seek_slow", decision.LagSeconds, seekElapsed);
            }
        }
        catch (Exception ex)
        {
            DisableFormalFrameDrop(
                clock,
                $"seek_exception:{ex.GetType().Name}",
                decision.LagSeconds,
                Stopwatch.GetElapsedTime(seekStartedAt));
        }
    }

    private void DisableFormalFrameDrop(
        TransitionFrameDropClock clock,
        string reason,
        double lagSeconds,
        TimeSpan seekElapsed)
    {
        if (!ReferenceEquals(formalFrameDropClock, clock))
        {
            return;
        }

        formalFrameDropClock = null;
        performanceTrace?.RecordFrameDropDisabled(reason, lagSeconds, seekElapsed);
    }

    private void ClearFormalFrameDrop(TransitionFrameDropClock? clock, long startedAt)
    {
        if (clock == null
            || (!ReferenceEquals(formalFrameDropClock, clock)
                && (formalFrameDropClock != null || formalPlaybackStartedAt != startedAt)))
        {
            return;
        }

        formalFrameDropClock = null;
        formalPlaybackStartedAt = 0;
    }

    internal void AttachPerformanceTrace(TransitionPerformanceTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        performanceTrace = trace;
    }

    internal void DetachPerformanceTrace(TransitionPerformanceTrace trace)
    {
        if (ReferenceEquals(performanceTrace, trace))
        {
            performanceTrace = null;
        }
    }

    public static NinjaSlayerTransitionOverlay GetOrCreate(NTransition transition)
    {
        CanvasLayer? canvasLayer = transition.GetNodeOrNull<CanvasLayer>(CanvasLayerName);
        if (canvasLayer == null)
        {
            canvasLayer = new CanvasLayer
            {
                Name = CanvasLayerName,
                Layer = CanvasLayerIndex
            };
            transition.AddChild(canvasLayer);
        }
        else
        {
            canvasLayer.Layer = CanvasLayerIndex;
        }

        var existing = canvasLayer.GetNodeOrNull<NinjaSlayerTransitionOverlay>(NodeName);
        if (existing != null)
        {
            existing.EnsureInitialized();
            canvasLayer.MoveChild(existing, -1);
            return existing;
        }

        var overlay = new NinjaSlayerTransitionOverlay
        {
            Name = NodeName,
            Visible = false
        };
        overlay.EnsureInitialized();
        canvasLayer.AddChild(overlay);
        canvasLayer.MoveChild(overlay, -1);
        return overlay;
    }
}
