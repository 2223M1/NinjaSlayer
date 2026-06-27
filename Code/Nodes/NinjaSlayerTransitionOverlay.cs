using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using NinjaSlayer.Code.Transition;

namespace NinjaSlayer.Code.Nodes;

[GlobalClass]
public partial class NinjaSlayerTransitionOverlay : Control
{
    public const string NodeName = "NinjaSlayerTransitionOverlay";

    private TextureRect? frameView;

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

        if (frameView != null)
        {
            return;
        }

        frameView = new TextureRect
        {
            Name = "FrameView",
            MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
        };
        frameView.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(frameView);
    }

    public async Task PlayAsync(float duration, CancellationToken cancelToken = default)
    {
        EnsureInitialized();

        var frames = NinjaSlayerTransitionFrames.GetFrames();
        Visible = true;
        var elapsed = 0.0;

        while (elapsed < duration)
        {
            if (cancelToken.IsCancellationRequested)
            {
                break;
            }

            var progress = duration <= 0f ? 1f : (float)(elapsed / duration);
            var frameIndex = Math.Clamp((int)(progress * frames.Length), 0, frames.Length - 1);
            SetFrame(frames[frameIndex]);

            elapsed += await this.AwaitProcessFrame(cancelToken);
        }

        SetFrame(frames[^1]);
        Visible = false;
    }

    private void SetFrame(Texture2D frame)
    {
        if (frameView == null || !GodotObject.IsInstanceValid(frame))
        {
            return;
        }

        frameView.Texture = frame;
    }

    public static NinjaSlayerTransitionOverlay GetOrCreate(NTransition transition)
    {
        var existing = transition.GetNodeOrNull<NinjaSlayerTransitionOverlay>(NodeName);
        if (existing != null)
        {
            existing.EnsureInitialized();
            transition.MoveChild(existing, -1);
            return existing;
        }

        var overlay = new NinjaSlayerTransitionOverlay
        {
            Name = NodeName,
            Visible = false,
        };
        overlay.EnsureInitialized();
        transition.AddChild(overlay);
        transition.MoveChild(overlay, -1);
        return overlay;
    }
}
