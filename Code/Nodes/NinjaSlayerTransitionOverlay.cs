using System.Diagnostics;
using Godot;
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
    private const float PlaybackTimeoutPaddingSeconds = 1f;
    private const double DecoderPrewarmTimeoutSeconds = 8.0;
    private const double DecoderPrewarmCompletionMarginSeconds = 1.0 / 24.0;

    private VideoStreamPlayer? videoPlayer;
    private TransitionPerformanceTrace? performanceTrace;
    private bool decoderPrewarmActive;
    private bool decoderPrewarmPlaybackStarted;
    private long decoderPrewarmGeneration;
    private double decoderPrewarmElapsed;

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
        ProcessDecoderPrewarm(delta);
    }

    public override void _ExitTree()
    {
        if (decoderPrewarmActive)
        {
            NinjaSlayerTransitionVideoPrewarmer.NotifyOverlayExited(this, decoderPrewarmGeneration);
        }
    }

    private void EnsureInitialized()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        ZAsRelative = false;
        ZIndex = 100;
        SetProcess(true);

        if (videoPlayer != null)
        {
            return;
        }

        var aspectContainer = new AspectRatioContainer
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
        NinjaSlayerTransitionVideoPrewarmer.PrepareForPlayback();
        EnsureInitialized();
        if (videoPlayer == null)
        {
            return;
        }

        using var hoverTipSuppression = NinjaSlayerHoverTipSuppression.Acquire();
        TransitionPerformanceTrace? trace = performanceTrace;
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

            long playStartedAt = Stopwatch.GetTimestamp();
            try
            {
                videoPlayer.Play();
            }
            finally
            {
                trace?.RecordPlayCall(Stopwatch.GetElapsedTime(playStartedAt));
            }
            trace?.MarkVideoStarted();

            double elapsed = 0.0;
            bool firstProcessFrame = true;
            float timeout = Math.Max(duration, 0f) + PlaybackTimeoutPaddingSeconds;
            while (videoPlayer.IsPlaying() && elapsed < timeout)
            {
                elapsed += await this.AwaitProcessFrame(cancelToken);
                if (firstProcessFrame)
                {
                    trace?.RecordFirstPostPlayFrame();
                    firstProcessFrame = false;
                }
            }

            if (videoPlayer.IsPlaying())
            {
                Entry.Logger.Warn($"NinjaSlayer transition video exceeded its {timeout:0.###}s playback timeout.");
            }
        }
        finally
        {
            trace?.MarkVideoStopped();
            videoPlayer.Stop();
            Visible = false;
        }
    }

    internal bool TryStartDecoderPrewarm(long generation)
    {
        EnsureInitialized();
        if (videoPlayer == null || decoderPrewarmActive || videoPlayer.IsPlaying())
        {
            return false;
        }

        decoderPrewarmGeneration = generation;
        decoderPrewarmElapsed = 0.0;
        decoderPrewarmPlaybackStarted = false;
        decoderPrewarmActive = true;
        videoPlayer.Volume = 0f;
        videoPlayer.Modulate = Colors.Transparent;
        SelfModulate = Colors.White;
        Visible = true;
        return true;
    }

    internal void StopDecoderPrewarmForPlayback()
    {
        AbortDecoderPrewarm(clearStream: false);
    }

    internal string GetDecoderPrewarmDiagnostic()
    {
        double position = videoPlayer is not null && GodotObject.IsInstanceValid(videoPlayer)
            ? videoPlayer.StreamPosition
            : 0.0;
        return $"active={decoderPrewarmActive}, playback_started={decoderPrewarmPlaybackStarted}, elapsed_ms={decoderPrewarmElapsed * 1000.0:0}, position={position:0.###}";
    }

    internal void AbortDecoderPrewarm(bool clearStream)
    {
        decoderPrewarmActive = false;
        decoderPrewarmPlaybackStarted = false;
        if (videoPlayer != null && GodotObject.IsInstanceValid(videoPlayer))
        {
            videoPlayer.Stop();
            videoPlayer.Volume = 1f;
            videoPlayer.Modulate = Colors.White;
            if (clearStream)
            {
                videoPlayer.Stream = null;
            }
        }

        SelfModulate = Colors.White;
        Visible = false;
    }

    public void StopPlayback()
    {
        if (videoPlayer != null && GodotObject.IsInstanceValid(videoPlayer))
        {
            performanceTrace?.MarkVideoStopped();
            videoPlayer.Stop();
        }
        Visible = false;
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

    private void ProcessDecoderPrewarm(double delta)
    {
        if (!decoderPrewarmActive || videoPlayer == null)
        {
            return;
        }

        decoderPrewarmElapsed += Math.Max(delta, 0.0);
        try
        {
            if (!decoderPrewarmPlaybackStarted)
            {
                TransitionVideoLoadPollResult loadResult =
                    NinjaSlayerTransitionVideo.PollPreloadedStream(out VideoStream? stream, out string? diagnostic);
                if (loadResult == TransitionVideoLoadPollResult.Waiting)
                {
                    CheckDecoderPrewarmTimeout();
                    return;
                }

                if (loadResult == TransitionVideoLoadPollResult.Failed || stream is null)
                {
                    FailDecoderPrewarm(diagnostic ?? "the preloaded stream was unavailable");
                    return;
                }

                videoPlayer.Stream = stream;
                _ = videoPlayer.GetVideoTexture();
                videoPlayer.Play();
                decoderPrewarmPlaybackStarted = true;
                return;
            }

            _ = videoPlayer.GetVideoTexture();
            double completionPosition = Math.Max(
                NinjaSlayerAudio.TransitionVisualSeconds - DecoderPrewarmCompletionMarginSeconds,
                0.0);
            if (!videoPlayer.IsPlaying() || videoPlayer.StreamPosition >= completionPosition)
            {
                CompleteDecoderPrewarm();
                return;
            }

            CheckDecoderPrewarmTimeout();
        }
        catch (Exception ex)
        {
            FailDecoderPrewarm(ex.Message);
        }
    }

    private void CheckDecoderPrewarmTimeout()
    {
        if (decoderPrewarmElapsed >= DecoderPrewarmTimeoutSeconds)
        {
            FailDecoderPrewarm($"the {DecoderPrewarmTimeoutSeconds:0.#}s prewarm timeout elapsed");
        }
    }

    private void CompleteDecoderPrewarm()
    {
        long generation = decoderPrewarmGeneration;
        decoderPrewarmActive = false;
        decoderPrewarmPlaybackStarted = false;
        videoPlayer?.Stop();
        if (videoPlayer != null)
        {
            videoPlayer.Volume = 1f;
            videoPlayer.Modulate = Colors.White;
        }
        SelfModulate = Colors.White;
        Visible = false;
        _ = NinjaSlayerTransitionVideoPrewarmer.Complete(this, generation);
    }

    private void FailDecoderPrewarm(string diagnostic)
    {
        long generation = decoderPrewarmGeneration;
        NinjaSlayerTransitionVideoPrewarmer.Fail(this, generation, diagnostic);
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
