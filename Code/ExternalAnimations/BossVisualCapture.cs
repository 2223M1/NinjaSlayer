using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Helpers;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed class BossVisualCapture : IDisposable
{
    private Node? _root;
    private SubViewport? _viewport;
    private Node2D? _visual;

    private BossVisualCapture(
        Node root,
        SubViewport viewport,
        Node2D visual,
        Rect2 textureBounds,
        Transform2D bodyGlobalTransform,
        ulong readyAfterFrame,
        long captureStartedTicks,
        long setupElapsedTicks)
    {
        _root = root;
        _viewport = viewport;
        _visual = visual;
        TextureBounds = textureBounds;
        BodyGlobalTransform = bodyGlobalTransform;
        ReadyAfterFrame = readyAfterFrame;
        CaptureStartedTicks = captureStartedTicks;
        SetupElapsedTicks = setupElapsedTicks;
    }

    public Rect2 TextureBounds { get; }
    public Transform2D BodyGlobalTransform { get; }
    public ulong ReadyAfterFrame { get; }
    public long CaptureStartedTicks { get; }
    public long SetupElapsedTicks { get; }

    public Vector2I PixelSize => _viewport is { } viewport && GodotObject.IsInstanceValid(viewport)
        ? viewport.Size
        : Vector2I.Zero;

    public long EstimatedTextureBytes => (long)PixelSize.X * PixelSize.Y * 4L;

    public Node? PresentationParent => _root is { } root && GodotObject.IsInstanceValid(root)
        ? root.GetParent()
        : null;

    public Node2D? Visual => _visual is { } visual
        && GodotObject.IsInstanceValid(visual)
        && visual.IsInsideTree()
            ? visual
            : null;

    public Texture2D? Texture
    {
        get
        {
            if (!IsReady
                || _viewport is not { } viewport
                || !GodotObject.IsInstanceValid(viewport))
            {
                return null;
            }

            try
            {
                ViewportTexture texture = viewport.GetTexture();
                return GodotObject.IsInstanceValid(texture) ? texture : null;
            }
            catch
            {
                return null;
            }
        }
    }

    public bool IsReady => Engine.GetProcessFrames() >= ReadyAfterFrame
        && _viewport is { } viewport
        && GodotObject.IsInstanceValid(viewport)
        && viewport.IsInsideTree();

    public static BossVisualCapture? TryCreate(
        Node presentationParent,
        Node2D sourceBody,
        Rect2 bodyLocalBounds)
    {
        long setupStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        Node? root = null;
        SubViewport? viewport = null;
        Node2D? duplicate = null;
        try
        {
            Rect2 textureBounds = ResolveSquareBounds(bodyLocalBounds);
            int logicalSide = Math.Max(1, Mathf.CeilToInt(textureBounds.Size.X));
            int renderSide = checked(logicalSide * 2);
            root = new Node
            {
                Name = "NinjaSlayerBossVisualCapture",
                ProcessMode = Node.ProcessModeEnum.Always
            };
            presentationParent.AddChildSafely(root);
            if (!GodotObject.IsInstanceValid(root) || !root.IsInsideTree())
            {
                throw new InvalidOperationException("The boss capture root could not enter the scene tree.");
            }

            viewport = new SubViewport
            {
                Name = "Viewport",
                TransparentBg = true,
                Disable3D = true,
                Size = new Vector2I(renderSide, renderSide),
                Size2DOverride = new Vector2I(logicalSide, logicalSide),
                Size2DOverrideStretch = true,
                RenderTargetClearMode = SubViewport.ClearMode.Always,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
                CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Linear
            };
            root.AddChildSafely(viewport);
            if (!GodotObject.IsInstanceValid(viewport) || !viewport.IsInsideTree())
            {
                throw new InvalidOperationException("The boss capture viewport could not enter the scene tree.");
            }

            duplicate = DuplicateVisualOnly(sourceBody)
                ?? throw new InvalidOperationException("The boss visual could not be duplicated for capture.");
            viewport.AddChildSafely(duplicate);
            if (!GodotObject.IsInstanceValid(duplicate) || !duplicate.IsInsideTree())
            {
                throw new InvalidOperationException("The duplicated boss visual could not enter the capture viewport.");
            }

            duplicate.TopLevel = false;
            duplicate.Position = -textureBounds.Position;
            duplicate.Rotation = 0f;
            duplicate.Scale = Vector2.One;
            duplicate.Skew = 0f;
            duplicate.Visible = true;
            PrepareVisualClone(duplicate);
            FreezeSpineAnimation(duplicate);
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            return new BossVisualCapture(
                root,
                viewport,
                duplicate,
                textureBounds,
                sourceBody.GlobalTransform,
                Engine.GetProcessFrames() + 2UL,
                started,
                started - setupStarted);
        }
        catch (Exception exception)
        {
            if (root != null && GodotObject.IsInstanceValid(root))
            {
                root.QueueFreeSafely();
            }
            else if (viewport != null && GodotObject.IsInstanceValid(viewport))
            {
                viewport.QueueFreeSafely();
            }
            else if (duplicate != null && GodotObject.IsInstanceValid(duplicate))
            {
                duplicate.Free();
            }

            Scripts.Entry.Logger.Warn($"Boss visual capture initialization failed: {exception}");
            return null;
        }
    }

    public void Dispose()
    {
        _visual = null;
        _viewport = null;
        Node? root = Interlocked.Exchange(ref _root, null);
        if (root != null && GodotObject.IsInstanceValid(root))
        {
            root.QueueFreeSafely();
        }
    }

    private static Rect2 ResolveSquareBounds(Rect2 bounds)
    {
        if (!float.IsFinite(bounds.Position.X)
            || !float.IsFinite(bounds.Position.Y)
            || !float.IsFinite(bounds.Size.X)
            || !float.IsFinite(bounds.Size.Y)
            || bounds.Size.X <= 1f
            || bounds.Size.Y <= 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                "Boss capture bounds must be finite and larger than one pixel.");
        }

        float side = Math.Max(Math.Max(bounds.Size.X, bounds.Size.Y), 1f);
        Vector2 padding = (new Vector2(side, side) - bounds.Size) * 0.5f;
        return new Rect2(bounds.Position - padding, new Vector2(side, side));
    }

    private static Node2D? DuplicateVisualOnly(Node2D source)
    {
        const Node.DuplicateFlags flags = Node.DuplicateFlags.Groups;
        if (source.Duplicate((int)flags) is not Node2D duplicate)
        {
            return null;
        }

        duplicate.Name = "CapturedBossVisual";
        return duplicate;
    }

    private static void PrepareVisualClone(Node node)
    {
        node.SetProcess(false);
        node.SetPhysicsProcess(false);
        node.SetProcessInput(false);
        node.SetProcessUnhandledInput(false);
        node.SetProcessUnhandledKeyInput(false);
        switch (node)
        {
            case GpuParticles2D gpuParticles:
                gpuParticles.Emitting = false;
                gpuParticles.Visible = false;
                break;
            case CpuParticles2D cpuParticles:
                cpuParticles.Emitting = false;
                cpuParticles.Visible = false;
                break;
            case AudioStreamPlayer audio:
                audio.Stop();
                break;
            case AudioStreamPlayer2D audio2D:
                audio2D.Stop();
                break;
            case AnimationPlayer animation:
                animation.Stop();
                break;
            case Godot.Timer timer:
                timer.Stop();
                break;
            case CollisionObject2D collision:
                collision.ProcessMode = Node.ProcessModeEnum.Disabled;
                break;
        }

        foreach (Node child in node.GetChildren())
        {
            PrepareVisualClone(child);
        }
    }

    private static void FreezeSpineAnimation(Node2D visual)
    {
        try
        {
            var sprite = new MegaSprite(Variant.CreateFrom(visual));
            using MegaAnimationState? animation = sprite.TryGetAnimationState();
            animation?.SetTimeScale(0f);
        }
        catch
        {
            // Non-Spine bodies need no animation state.
        }
    }
}
