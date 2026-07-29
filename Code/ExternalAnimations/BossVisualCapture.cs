using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Helpers;

namespace NinjaSlayer.Code.ExternalAnimations;

public sealed partial class BossVisualCapture : Node, IDisposable
{
    private enum CaptureState
    {
        MeasuringParts,
        BuildingAtlas,
        WaitingForFrame,
        Ready,
        Failed,
        Disposed
    }

    private const int PartsPerFrame = 2;
    private const float PreferredScreenSupersampling = 2f;
    private const float MinimumScreenSupersampling = 1f;
    private const int AtlasGutterPixels = 4;
    private const int MaximumAtlasPixels = 4096;

    private readonly List<Node2D> _atlasVisuals = [];
    private CaptureState _state;
    private SubViewport? _viewport;
    private Node2D? _template;
    private BossSemanticPartBuilder? _semanticBuilder;
    private IReadOnlyList<BossSemanticPartDefinition>? _semanticParts;
    private IReadOnlyList<AtlasPlacement>? _atlasPlacements;
    private int _nextAtlasPart;
    private int _atlasMergeCount;
    private float _atlasDensity;
    private ulong _readyAfterFrame;
    private ulong _lastWorkFrame = ulong.MaxValue;
    private long _readyElapsedTicks;
    private int _disposeStarted;
    private ulong _seed;

    private BossVisualCapture()
    {
        Name = "NinjaSlayerBossVisualCapture";
        ProcessMode = ProcessModeEnum.Always;
    }

    public Rect2 BodyLocalBounds { get; private set; }
    public Transform2D BodyToSceneContainer { get; private set; }
    public Transform2D BaselineSceneToGlobal { get; private set; }
    public Vector2 BaselineScenePosition { get; private set; }
    public Vector2 BaselineSceneScale { get; private set; }
    public Transform2D BodyBaselineGlobalTransform =>
        BaselineSceneToGlobal * BodyToSceneContainer;
    public Rect2 BodyBaselineScreenBounds { get; private set; }
    public long CaptureStartedTicks { get; private set; }
    public long SetupElapsedTicks { get; private set; }
    public long ReadyElapsedTicks => _readyElapsedTicks;
    public string FailureReason { get; private set; } = string.Empty;
    internal BossFragmentPartition? Partition { get; private set; }

    public Vector2I PixelSize => _viewport is { } viewport
        && GodotObject.IsInstanceValid(viewport)
            ? viewport.Size
            : Vector2I.Zero;

    public long EstimatedTextureBytes => (long)PixelSize.X * PixelSize.Y * 4L;
    public float AtlasDensity => _atlasDensity;
    public Node? PresentationParent => IsInsideTree() ? GetParent() : null;

    public bool IsReady => _state == CaptureState.Ready
        && _viewport is { } viewport
        && GodotObject.IsInstanceValid(viewport)
        && viewport.IsInsideTree();

    public Texture2D? Texture
    {
        get
        {
            if (!IsReady || _viewport is not { } viewport)
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

    internal static BossVisualCapture? TryCreate(
        Node presentationParent,
        Node2D sourceBody,
        Rect2 fallbackBodyLocalBounds,
        Transform2D bodyToSceneContainer,
        CombatSceneBaseline baseline,
        bool canSplitSpine,
        ulong seed,
        string? detachedBoneName)
    {
        long setupStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        BossVisualCapture? capture = null;
        try
        {
            if (!canSplitSpine)
            {
                throw new InvalidOperationException(
                    "The boss is not using an accessible Spine death pose.");
            }

            ValidateBounds(fallbackBodyLocalBounds);
            capture = new BossVisualCapture
            {
                CaptureStartedTicks = setupStarted,
                BodyToSceneContainer = bodyToSceneContainer,
                BaselineSceneToGlobal = baseline.SceneToGlobal,
                BaselineScenePosition = baseline.Position,
                BaselineSceneScale = baseline.Scale,
                _seed = seed,
                _state = CaptureState.MeasuringParts
            };
            presentationParent.AddChildSafely(capture);
            EnsureInsideTree(capture, "boss capture root");

            Node2D template = DuplicateVisualOnly(sourceBody)
                ?? throw new InvalidOperationException(
                    "The boss visual could not be duplicated for semantic capture.");
            capture._template = template;
            PrepareVisualClone(template);
            template.Visible = false;
            capture.AddChildSafely(template);
            EnsureInsideTree(template, "boss semantic capture template");
            template.TopLevel = false;
            template.Position = Vector2.Zero;
            template.Rotation = 0f;
            template.Scale = Vector2.One;
            template.Skew = 0f;
            FreezeSpineAnimation(template);

            capture.BodyLocalBounds = ResolveCaptureBounds(
                template,
                fallbackBodyLocalBounds);
            ValidateBounds(capture.BodyLocalBounds);
            capture.BodyBaselineScreenBounds = TransformBounds(
                capture.BodyLocalBounds,
                capture.BodyBaselineGlobalTransform);
            ValidateBounds(capture.BodyBaselineScreenBounds);

            if (!BossSemanticPartBuilder.TryCreate(
                    template,
                    capture.BodyLocalBounds,
                    detachedBoneName,
                    out BossSemanticPartBuilder? builder,
                    out string failureReason)
                || builder == null)
            {
                throw new InvalidOperationException(failureReason);
            }

            capture._semanticBuilder = builder;
            capture._lastWorkFrame = Engine.GetProcessFrames();
            builder.MeasureNext(PartsPerFrame);
            capture.SetupElapsedTicks =
                System.Diagnostics.Stopwatch.GetTimestamp() - setupStarted;
            capture.SetProcess(true);
            return capture;
        }
        catch (Exception exception)
        {
            if (capture != null && GodotObject.IsInstanceValid(capture))
            {
                capture.Fail($"capture initialization failed: {exception.Message}");
                capture.Dispose();
            }

            Scripts.Entry.Logger.Warn($"Boss visual capture initialization failed: {exception}");
            return null;
        }
    }

    public override void _Process(double delta)
    {
        _ = delta;
        ulong frame = Engine.GetProcessFrames();
        if (_lastWorkFrame == frame)
        {
            return;
        }

        _lastWorkFrame = frame;
        try
        {
            switch (_state)
            {
                case CaptureState.MeasuringParts:
                    AdvanceMeasurements();
                    break;
                case CaptureState.BuildingAtlas:
                    AdvanceAtlasBuild();
                    break;
                case CaptureState.WaitingForFrame:
                    CompleteAtlasCapture(frame);
                    break;
            }
        }
        catch (Exception exception)
        {
            Fail($"semantic atlas capture failed: {exception.Message}");
            Scripts.Entry.Logger.Warn($"Boss visual capture failed: {exception}");
        }
    }

    public override void _ExitTree()
    {
        SetProcess(false);
        ReleaseTemporaryVisuals();
        ReleaseViewport();
        Partition = null;
        _semanticBuilder = null;
        _semanticParts = null;
        _atlasPlacements = null;
        if (_state != CaptureState.Disposed)
        {
            _state = CaptureState.Disposed;
        }
    }

    public new void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        _state = CaptureState.Disposed;
        SetProcess(false);
        if (GodotObject.IsInstanceValid(this))
        {
            this.QueueFreeSafely();
        }
    }

    private void AdvanceMeasurements()
    {
        BossSemanticPartBuilder builder = _semanticBuilder
            ?? throw new InvalidOperationException("The semantic part builder expired.");
        builder.MeasureNext(PartsPerFrame);
        if (!builder.IsMeasured)
        {
            return;
        }

        _semanticParts = builder.Complete();
        PrepareAtlas();
        _state = CaptureState.BuildingAtlas;
    }

    private void PrepareAtlas()
    {
        IReadOnlyList<BossSemanticPartDefinition> parts = _semanticParts
            ?? throw new InvalidOperationException("Semantic parts are unavailable.");
        float baselinePixelsPerUnit = ResolveMaximumScale(BodyBaselineGlobalTransform);
        if (!float.IsFinite(baselinePixelsPerUnit) || baselinePixelsPerUnit <= 0f)
        {
            throw new InvalidOperationException(
                "The frozen battle-view body scale is invalid.");
        }

        float preferredDensity = baselinePixelsPerUnit * PreferredScreenSupersampling;
        float minimumDensity = baselinePixelsPerUnit * MinimumScreenSupersampling;
        while (true)
        {
            if (TryPackAtlas(parts, preferredDensity, out AtlasLayout? layout)
                || TryPackAtlas(parts, minimumDensity, out layout))
            {
                _semanticParts = parts;
                _atlasPlacements = layout!.Placements;
                _atlasDensity = layout.Density;
                _viewport = CreateViewport(layout.PixelSize);
                this.AddChildSafely(_viewport);
                EnsureInsideTree(_viewport, "boss semantic atlas viewport");

                BossAtlasSemanticPart[] atlasParts = layout.Placements
                    .Select(placement => new BossAtlasSemanticPart(
                        placement.Part,
                        ToUvRect(placement.ContentPixels, layout.PixelSize)))
                    .ToArray();
                Partition = BossFragmentPartitioner.BuildSemanticPartition(
                    atlasParts,
                    BodyLocalBounds,
                    _seed,
                    (_semanticBuilder?.MergedPartCount ?? 0) + _atlasMergeCount);
                return;
            }

            if (!BossFragmentPartitioner.TryMergeSmallestRelatedPart(
                    parts,
                    out IReadOnlyList<BossSemanticPartDefinition> merged))
            {
                throw new InvalidOperationException(
                    "The semantic atlas does not fit within 4096x4096 pixels at battle-view density.");
            }

            parts = merged;
            _atlasMergeCount++;
        }
    }

    private void AdvanceAtlasBuild()
    {
        if (_viewport == null
            || _semanticParts == null
            || _atlasPlacements == null
            || _template == null)
        {
            throw new InvalidOperationException("The semantic atlas build state expired.");
        }

        int built = 0;
        while (_nextAtlasPart < _atlasPlacements.Count && built < PartsPerFrame)
        {
            AtlasPlacement placement = _atlasPlacements[_nextAtlasPart++];
            Node2D clone = DuplicateVisualOnly(_template)
                ?? throw new InvalidOperationException(
                    $"Spine part '{placement.Part.PrimaryBoneName}' could not be duplicated.");
            PrepareVisualClone(clone);
            clone.Visible = false;
            _viewport.AddChildSafely(clone);
            EnsureInsideTree(clone, "isolated Spine atlas part");
            clone.TopLevel = false;
            FreezeSpineAnimation(clone);
            IsolateSlots(clone, placement.Part.SlotIndices);
            clone.Position = placement.ContentPixels.Position
                - placement.Part.SourceBounds.Position * _atlasDensity;
            clone.Rotation = 0f;
            clone.Scale = Vector2.One * _atlasDensity;
            clone.Skew = 0f;
            clone.Visible = true;
            _atlasVisuals.Add(clone);
            built++;
        }

        if (_nextAtlasPart < _atlasPlacements.Count)
        {
            return;
        }

        ReleaseTemplate();
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        _readyAfterFrame = Engine.GetProcessFrames() + 2UL;
        _state = CaptureState.WaitingForFrame;
    }

    private void CompleteAtlasCapture(ulong frame)
    {
        if (frame < _readyAfterFrame)
        {
            return;
        }

        if (_viewport == null)
        {
            throw new InvalidOperationException("The semantic atlas viewport expired.");
        }

        Texture2D texture = _viewport.GetTexture();
        if (!GodotObject.IsInstanceValid(texture)
            || texture.GetWidth() <= 1
            || texture.GetHeight() <= 1)
        {
            throw new InvalidOperationException("The semantic boss atlas texture is invalid.");
        }

        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        ReleaseAtlasVisuals();
        _readyElapsedTicks =
            System.Diagnostics.Stopwatch.GetTimestamp() - CaptureStartedTicks;
        _state = CaptureState.Ready;
        SetProcess(false);
    }

    private static bool TryPackAtlas(
        IReadOnlyList<BossSemanticPartDefinition> parts,
        float density,
        out AtlasLayout? layout)
    {
        layout = null;
        if (!float.IsFinite(density) || density <= 0f || parts.Count < 2)
        {
            return false;
        }

        AtlasItem[] items = parts
            .Select(part => new AtlasItem(
                part,
                new Vector2I(
                    Math.Max(2, Mathf.CeilToInt(part.SourceBounds.Size.X * density)),
                    Math.Max(2, Mathf.CeilToInt(part.SourceBounds.Size.Y * density)))))
            .OrderByDescending(item => item.ContentSize.Y)
            .ThenByDescending(item => item.ContentSize.X)
            .ThenBy(item => item.Part.DrawOrder)
            .ToArray();
        int largestWidth = items.Max(item => item.ContentSize.X + AtlasGutterPixels * 2);
        int largestHeight = items.Max(item => item.ContentSize.Y + AtlasGutterPixels * 2);
        if (largestWidth > MaximumAtlasPixels || largestHeight > MaximumAtlasPixels)
        {
            return false;
        }

        long totalArea = items.Sum(item =>
            (long)(item.ContentSize.X + AtlasGutterPixels * 2)
            * (item.ContentSize.Y + AtlasGutterPixels * 2));
        int firstWidth = Math.Clamp(
            Mathf.CeilToInt(MathF.Sqrt(totalArea)),
            largestWidth,
            MaximumAtlasPixels);
        firstWidth = Math.Min(MaximumAtlasPixels, Align(firstWidth, 64));
        for (int width = firstWidth; width <= MaximumAtlasPixels; width += 64)
        {
            if (!TryPackRows(items, width, out IReadOnlyList<AtlasPlacement>? placements,
                    out Vector2I usedSize))
            {
                continue;
            }

            layout = new AtlasLayout(usedSize, density, placements!);
            return true;
        }

        if (firstWidth != MaximumAtlasPixels
            && TryPackRows(items, MaximumAtlasPixels, out IReadOnlyList<AtlasPlacement>? final,
                out Vector2I finalSize))
        {
            layout = new AtlasLayout(finalSize, density, final!);
            return true;
        }

        return false;
    }

    private static bool TryPackRows(
        IReadOnlyList<AtlasItem> items,
        int atlasWidth,
        out IReadOnlyList<AtlasPlacement>? placements,
        out Vector2I usedSize)
    {
        var result = new List<AtlasPlacement>(items.Count);
        int x = 0;
        int y = 0;
        int rowHeight = 0;
        int usedWidth = 0;
        foreach (AtlasItem item in items)
        {
            int width = item.ContentSize.X + AtlasGutterPixels * 2;
            int height = item.ContentSize.Y + AtlasGutterPixels * 2;
            if (x > 0 && x + width > atlasWidth)
            {
                x = 0;
                y += rowHeight;
                rowHeight = 0;
            }

            if (y + height > MaximumAtlasPixels)
            {
                placements = null;
                usedSize = default;
                return false;
            }

            result.Add(new AtlasPlacement(
                item.Part,
                new Rect2I(
                    x + AtlasGutterPixels,
                    y + AtlasGutterPixels,
                    item.ContentSize.X,
                    item.ContentSize.Y)));
            x += width;
            rowHeight = Math.Max(rowHeight, height);
            usedWidth = Math.Max(usedWidth, x);
        }

        int usedHeight = y + rowHeight;
        placements = result
            .OrderBy(placement => placement.Part.DrawOrder)
            .ToArray();
        usedSize = new Vector2I(
            Math.Clamp(Align(usedWidth, 2), 2, MaximumAtlasPixels),
            Math.Clamp(Align(usedHeight, 2), 2, MaximumAtlasPixels));
        return true;
    }

    private static SubViewport CreateViewport(Vector2I pixelSize) => new()
    {
        Name = "BossSemanticAtlas",
        TransparentBg = true,
        Disable3D = true,
        Size = pixelSize,
        Size2DOverride = pixelSize,
        Size2DOverrideStretch = true,
        RenderTargetClearMode = SubViewport.ClearMode.Always,
        RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Linear,
        ProcessMode = ProcessModeEnum.Always
    };

    private static Node2D? DuplicateVisualOnly(Node2D source)
    {
        const Node.DuplicateFlags flags = Node.DuplicateFlags.Groups;
        if (source.Duplicate((int)flags) is not Node2D duplicate)
        {
            return null;
        }

        duplicate.Name = "CapturedBossSpinePart";
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
                collision.ProcessMode = ProcessModeEnum.Disabled;
                break;
            case Control control:
                control.Visible = false;
                break;
        }

        foreach (Node child in node.GetChildren())
        {
            PrepareVisualClone(child);
        }
    }

    private static void FreezeSpineAnimation(Node2D visual)
    {
        var sprite = new MegaSprite(Variant.CreateFrom(visual));
        using MegaAnimationState? animation = sprite.TryGetAnimationState();
        animation?.SetTimeScale(0f);
    }

    private static void IsolateSlots(
        Node2D visual,
        IReadOnlyList<int> visibleSlotIndices)
    {
        var sprite = new MegaSprite(Variant.CreateFrom(visual));
        using MegaSkeleton skeleton = sprite.GetSkeleton()
            ?? throw new InvalidOperationException(
                "The isolated Spine clone has no skeleton.");
        if (!skeleton.BoundObject.HasMethod("get_slots"))
        {
            throw new MissingMethodException("SpineSkeleton.get_slots is unavailable.");
        }

        Godot.Collections.Array<GodotObject> slots = skeleton.BoundObject
            .Call("get_slots")
            .AsGodotArray<GodotObject>();
        var visible = new HashSet<int>(visibleSlotIndices);
        try
        {
            for (int index = 0; index < slots.Count; index++)
            {
                if (visible.Contains(index))
                {
                    continue;
                }

                GodotObject slot = slots[index];
                if (!slot.HasMethod("set_color"))
                {
                    throw new MissingMethodException(
                        "SpineSlot.set_color is unavailable for semantic isolation.");
                }

                Color color = slot.HasMethod("get_color")
                    ? slot.Call("get_color").AsColor()
                    : Colors.White;
                color.A = 0f;
                slot.Call("set_color", color);
            }
        }
        finally
        {
            foreach (GodotObject slot in slots)
            {
                slot.Dispose();
            }
        }
    }

    private static Rect2 ResolveCaptureBounds(Node2D visual, Rect2 fallbackBounds)
    {
        try
        {
            var sprite = new MegaSprite(Variant.CreateFrom(visual));
            using MegaSkeleton? skeleton = sprite.GetSkeleton();
            if (skeleton != null)
            {
                Rect2 spineBounds = skeleton.GetBounds();
                if (IsValidBounds(spineBounds))
                {
                    return spineBounds.Grow(2f);
                }
            }
        }
        catch (Exception exception)
        {
            Scripts.Entry.Logger.Warn(
                $"Boss Spine bounds were unavailable; using visual bounds: {exception.Message}");
        }

        return fallbackBounds;
    }

    private static float ResolveMaximumScale(Transform2D transform) =>
        Math.Max(transform.X.Length(), transform.Y.Length());

    private static Rect2 TransformBounds(Rect2 bounds, Transform2D transform)
    {
        Vector2[] points =
        [
            transform * bounds.Position,
            transform * new Vector2(bounds.End.X, bounds.Position.Y),
            transform * bounds.End,
            transform * new Vector2(bounds.Position.X, bounds.End.Y)
        ];
        float minimumX = points.Min(point => point.X);
        float minimumY = points.Min(point => point.Y);
        float maximumX = points.Max(point => point.X);
        float maximumY = points.Max(point => point.Y);
        return new Rect2(
            minimumX,
            minimumY,
            maximumX - minimumX,
            maximumY - minimumY);
    }

    private static Rect2 ToUvRect(Rect2I pixels, Vector2I atlasSize) => new(
        pixels.Position.X / (float)atlasSize.X,
        pixels.Position.Y / (float)atlasSize.Y,
        pixels.Size.X / (float)atlasSize.X,
        pixels.Size.Y / (float)atlasSize.Y);

    private static int Align(int value, int alignment) =>
        checked((value + alignment - 1) / alignment * alignment);

    private static void ValidateBounds(Rect2 bounds)
    {
        if (!IsValidBounds(bounds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                "Boss capture bounds must be finite and larger than one pixel.");
        }
    }

    private static bool IsValidBounds(Rect2 bounds) =>
        float.IsFinite(bounds.Position.X)
        && float.IsFinite(bounds.Position.Y)
        && float.IsFinite(bounds.Size.X)
        && float.IsFinite(bounds.Size.Y)
        && bounds.Size.X > 1f
        && bounds.Size.Y > 1f;

    private void Fail(string reason)
    {
        if (_state is CaptureState.Failed or CaptureState.Disposed)
        {
            return;
        }

        FailureReason = reason;
        _state = CaptureState.Failed;
        SetProcess(false);
        ReleaseTemporaryVisuals();
        ReleaseViewport();
        Partition = null;
    }

    private void ReleaseTemporaryVisuals()
    {
        ReleaseTemplate();
        ReleaseAtlasVisuals();
    }

    private void ReleaseTemplate()
    {
        Node2D? template = _template;
        _template = null;
        if (template != null && GodotObject.IsInstanceValid(template))
        {
            template.QueueFreeSafely();
        }
    }

    private void ReleaseAtlasVisuals()
    {
        for (int index = _atlasVisuals.Count - 1; index >= 0; index--)
        {
            Node2D visual = _atlasVisuals[index];
            if (GodotObject.IsInstanceValid(visual))
            {
                visual.QueueFreeSafely();
            }
        }

        _atlasVisuals.Clear();
    }

    private void ReleaseViewport()
    {
        SubViewport? viewport = _viewport;
        _viewport = null;
        if (viewport != null && GodotObject.IsInstanceValid(viewport))
        {
            viewport.QueueFreeSafely();
        }
    }

    private static void EnsureInsideTree(Node node, string label)
    {
        if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree())
        {
            throw new InvalidOperationException($"The {label} could not enter the scene tree.");
        }
    }

    private sealed record AtlasItem(
        BossSemanticPartDefinition Part,
        Vector2I ContentSize);

    private sealed record AtlasPlacement(
        BossSemanticPartDefinition Part,
        Rect2I ContentPixels);

    private sealed record AtlasLayout(
        Vector2I PixelSize,
        float Density,
        IReadOnlyList<AtlasPlacement> Placements);
}
