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

        // Cap how much a single tick can advance the clock. The animation runs on the main
        // thread, overlapping main-thread run/save loading; when loading stalls the thread the
        // next ProcessFrame delta spikes. Without a cap that spike jumps the frame index over
        // several frames (visible stutter). Clamping to ~2 nominal frames turns a load hitch
        // into a brief pause that resumes in order instead of skipping frames.
        var secondsPerFrame = frames.Length > 0 && duration > 0f ? duration / frames.Length : 0f;
        var maxStep = secondsPerFrame > 0f ? secondsPerFrame * 2f : double.MaxValue;

        while (elapsed < duration)
        {
            if (cancelToken.IsCancellationRequested)
            {
                break;
            }

            var progress = duration <= 0f ? 1f : (float)(elapsed / duration);
            var frameIndex = Math.Clamp((int)(progress * frames.Length), 0, frames.Length - 1);
            SetFrame(frames[frameIndex]);

            elapsed += Math.Min(await this.AwaitProcessFrame(cancelToken), maxStep);
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
