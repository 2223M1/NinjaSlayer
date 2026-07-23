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

    public override void _Ready()
    {
        EnsureInitialized();
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
        try
        {
            videoPlayer.Stream = NinjaSlayerTransitionVideo.GetStream();
            Visible = true;
            videoPlayer.Play();
            double elapsed = 0.0;
            float timeout = Math.Max(duration, 0f) + PlaybackTimeoutPaddingSeconds;
            while (videoPlayer.IsPlaying() && elapsed < timeout)
            {
                elapsed += await this.AwaitProcessFrame(cancelToken);
            }

            if (videoPlayer.IsPlaying())
            {
                Entry.Logger.Warn($"NinjaSlayer transition video exceeded its {timeout:0.###}s playback timeout.");
            }
        }
        finally
        {
            videoPlayer.Stop();
            Visible = false;
        }
    }

    public void StopPlayback()
    {
        if (videoPlayer != null && GodotObject.IsInstanceValid(videoPlayer))
        {
            videoPlayer.Stop();
        }
        Visible = false;
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
