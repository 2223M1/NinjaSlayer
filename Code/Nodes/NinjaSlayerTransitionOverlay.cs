using System.Diagnostics;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using NinjaSlayer.Code.Transition;
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

    private VideoStreamPlayer? videoPlayer;
    private TransitionPerformanceTrace? performanceTrace;

    public override void _Ready()
    {
        EnsureInitialized();
    }

    public override void _Process(double delta)
    {
        // The overlay outlives every session under the persistent NTransition. Without an early
        // exit its three interop calls ran on every frame of the game for nothing.
        if (performanceTrace is null)
        {
            SetProcess(false);
            return;
        }

        double? videoPosition = videoPlayer is not null
            && GodotObject.IsInstanceValid(videoPlayer)
            && videoPlayer.IsPlaying()
                ? videoPlayer.StreamPosition
                : null;
        performanceTrace?.RecordFrame(delta, videoPosition);
    }

    private void EnsureInitialized()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        ZAsRelative = false;
        ZIndex = 100;

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
        EnsureInitialized();
        if (videoPlayer == null)
        {
            return;
        }

        using var hoverTipSuppression = NinjaSlayerHoverTipSuppression.Acquire();
        TransitionPerformanceTrace? trace = performanceTrace;
        SetProcess(true);
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

            // Assigning Stream instantiates the Theora decoder on the main thread. The overlay is
            // reused across sessions, so only the first transition of a process pays for it.
            if (!ReferenceEquals(videoPlayer.Stream, stream))
            {
                videoPlayer.Stream = stream;
            }
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
            // MarkVideoStopped is idempotent; the local trace also covers the case where the
            // session detached it before this task unwound.
            trace?.MarkVideoStopped();
            StopPlayback();
        }
    }

    public void StopPlayback()
    {
        if (videoPlayer != null && GodotObject.IsInstanceValid(videoPlayer))
        {
            performanceTrace?.MarkVideoStopped();
            videoPlayer.Stop();
        }

        if (GodotObject.IsInstanceValid(this))
        {
            Visible = false;
        }
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
