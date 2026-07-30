using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Helpers;
using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.Code.ExternalAnimations;

public sealed partial class BossVisualCapture : Node, IDisposable
{
    private enum CaptureState
    {
        Initializing,
        MeasuringParts,
        BuildingAtlas,
        WaitingForFrame,
        WaitingForPreparedResources,
        Ready,
        Failed,
        Disposed
    }

    private const int PartsPerFrame = 2;
    private const float PreferredScreenSupersampling = 2f;
    private const float MinimumScreenSupersampling = 1f;
    private const int AtlasGutterPixels = 4;
    private const int MaximumAtlasPixels = 4096;

    private readonly List<BossCapturedFragmentRenderSurface.PreparedResource> _preparedFragments = [];
    private CaptureState _state;
    private SubViewport? _viewport;
    private Node2D? _template;
    private BossSemanticPartBuilder? _semanticBuilder;
    private IReadOnlyList<BossSemanticPartDefinition>? _semanticParts;
    private IReadOnlyList<AtlasPlacement>? _atlasPlacements;
    private int _nextAtlasPart;
    private int _nextPreparedFragment;
    private int _atlasMergeCount;
    private float _atlasDensity;
    private ulong _readyAfterFrame;
    private ulong _lastWorkFrame = ulong.MaxValue;
    private long _readyElapsedTicks;
    private int _disposeStarted;
    private ulong _seed;
    private string? _detachedBoneName;
    private Color[]? _templateSlotColors;
    private Shader? _fragmentShader;
    private Transform2D _bodyToPresentation;
    private float[]? _mappedAreas;
    private float[]? _massRatios;
    private bool _atlasComplete;
    private long _measurementCpuTicks;
    private long _atlasCpuTicks;
    private long _fragmentPreparationCpuTicks;
    private long _maximumCpuFrameTicks;

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
    public long MeasurementCpuTicks => _measurementCpuTicks;
    public long AtlasCpuTicks => _atlasCpuTicks;
    public long FragmentPreparationCpuTicks => _fragmentPreparationCpuTicks;
    public long MaximumCpuFrameTicks => _maximumCpuFrameTicks;
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

    internal IReadOnlyList<BossCapturedFragmentRenderSurface.PreparedResource>
        TakePreparedFragments()
    {
        if (!IsReady || Partition == null || _preparedFragments.Count != Partition.Fragments.Count)
        {
            return [];
        }

        BossCapturedFragmentRenderSurface.PreparedResource[] result =
            _preparedFragments.ToArray();
        _preparedFragments.Clear();
        return result;
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
                _detachedBoneName = detachedBoneName,
                _state = CaptureState.Initializing
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

            capture._lastWorkFrame = Engine.GetProcessFrames();
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
        CaptureState stateAtFrameStart = _state;
        long frameStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            switch (_state)
            {
                case CaptureState.Initializing:
                    AdvanceInitialization();
                    break;
                case CaptureState.MeasuringParts:
                    AdvanceMeasurements();
                    break;
                case CaptureState.BuildingAtlas:
                    AdvanceAtlasBuild();
                    break;
                case CaptureState.WaitingForFrame:
                    CompleteAtlasCapture(frame);
                    break;
                case CaptureState.WaitingForPreparedResources:
                    break;
            }

            if (Partition != null
                && _nextPreparedFragment < Partition.Fragments.Count)
            {
                AdvanceFragmentPreparation();
            }

            TryMarkReady();
        }
        catch (Exception exception)
        {
            Fail($"semantic atlas capture failed: {exception.Message}");
            Scripts.Entry.Logger.Warn($"Boss visual capture failed: {exception}");
        }
        finally
        {
            long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - frameStarted;
            _maximumCpuFrameTicks = Math.Max(_maximumCpuFrameTicks, elapsed);
            switch (stateAtFrameStart)
            {
                case CaptureState.Initializing:
                case CaptureState.MeasuringParts:
                    _measurementCpuTicks += elapsed;
                    break;
                case CaptureState.BuildingAtlas:
                case CaptureState.WaitingForFrame:
                    _atlasCpuTicks += elapsed;
                    break;
            }
        }
    }

    public override void _ExitTree()
    {
        SetProcess(false);
        ReleaseTemporaryVisuals();
        ReleasePreparedFragments();
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

    private void AdvanceInitialization()
    {
        Node2D template = _template
            ?? throw new InvalidOperationException("The semantic capture template expired.");
        if (!BossSemanticPartBuilder.TryCreate(
                template,
                BodyLocalBounds,
                _detachedBoneName,
                out BossSemanticPartBuilder? builder,
                out string failureReason)
            || builder == null)
        {
            throw new InvalidOperationException(failureReason);
        }

        _semanticBuilder = builder;
        _state = CaptureState.MeasuringParts;
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
                PrepareFragmentResources();
                MoveTemplateIntoViewport();
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

    private void PrepareFragmentResources()
    {
        BossFragmentPartition partition = Partition
            ?? throw new InvalidOperationException("The semantic fragment partition is unavailable.");
        if (GetParent() is not CanvasItem presentationParent)
        {
            throw new InvalidOperationException(
                "The combat VFX container cannot provide a canvas transform.");
        }

        _bodyToPresentation = presentationParent.GetGlobalTransform().AffineInverse()
            * BaselineSceneToGlobal
            * BodyToSceneContainer;
        _fragmentShader = ResourceLoader.Load<Shader>(
            BossCapturedFragmentRenderSurface.ShaderPath)
            ?? throw new InvalidOperationException("The captured fragment shader is unavailable.");
        _mappedAreas = partition.Fragments
            .Select(descriptor => ResolveMappedArea(descriptor.Cell, _bodyToPresentation))
            .ToArray();
        float[] massWeights = partition.Fragments
            .Select(descriptor => Math.Max(0.0001f, descriptor.BodyAreaRatio))
            .ToArray();
        float averageMassWeight = Math.Max(0.0001f, massWeights.Average());
        _massRatios = massWeights
            .Select(weight => Math.Clamp(weight / averageMassWeight, 0.25f, 3f))
            .ToArray();
    }

    private void MoveTemplateIntoViewport()
    {
        Node2D template = _template
            ?? throw new InvalidOperationException("The semantic capture template expired.");
        SubViewport viewport = _viewport
            ?? throw new InvalidOperationException("The semantic atlas viewport expired.");
        template.Reparent(viewport, keepGlobalTransform: false);
        EnsureInsideTree(template, "reusable isolated Spine atlas part");
        template.TopLevel = false;
        template.Position = Vector2.Zero;
        template.Rotation = 0f;
        template.Scale = Vector2.One;
        template.Skew = 0f;
        template.Visible = false;
        _templateSlotColors = CaptureSlotColors(template);
    }

    private void AdvanceFragmentPreparation()
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            BossFragmentPartition partition = Partition
                ?? throw new InvalidOperationException("The semantic fragment partition expired.");
            Shader shader = _fragmentShader
                ?? throw new InvalidOperationException("The captured fragment shader expired.");
            float[] mappedAreas = _mappedAreas
                ?? throw new InvalidOperationException("The mapped fragment areas expired.");
            float[] massRatios = _massRatios
                ?? throw new InvalidOperationException("The fragment mass ratios expired.");
            if (_nextPreparedFragment >= partition.Fragments.Count)
            {
                return;
            }

            int index = _nextPreparedFragment;
            BossCapturedFragmentDescriptor descriptor = partition.Fragments[index];
            if (!BossCapturedFragmentRenderSurface.TryPrepare(
                    descriptor,
                    _bodyToPresentation,
                    shader,
                    massRatios[index],
                    BossDismembermentMath.ResolveCollisionPadding(mappedAreas[index]),
                    mappedAreas[index],
                    out BossCapturedFragmentRenderSurface.PreparedResource? prepared)
                || prepared == null)
            {
                throw new InvalidOperationException(
                    $"fragment {descriptor.FragmentIndex} could not be prepared");
            }

            _preparedFragments.Add(prepared);
            _nextPreparedFragment++;
        }
        finally
        {
            _fragmentPreparationCpuTicks +=
                System.Diagnostics.Stopwatch.GetTimestamp() - started;
        }
    }

    private void TryMarkReady()
    {
        if (!_atlasComplete
            || Partition == null
            || _preparedFragments.Count != Partition.Fragments.Count)
        {
            return;
        }

        _readyElapsedTicks =
            System.Diagnostics.Stopwatch.GetTimestamp() - CaptureStartedTicks;
        _state = CaptureState.Ready;
        SetProcess(false);
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

        if (_nextAtlasPart >= _atlasPlacements.Count)
        {
            _atlasComplete = true;
            _state = CaptureState.WaitingForPreparedResources;
            return;
        }

        AtlasPlacement placement = _atlasPlacements[_nextAtlasPart];
        ApplySlotIsolation(
            _template,
            placement.Part.SlotIndices,
            _templateSlotColors
                ?? throw new InvalidOperationException("The Spine slot-color snapshot expired."));
        _template.Position = placement.ContentPixels.Position
            - placement.Part.SourceBounds.Position * _atlasDensity;
        _template.Rotation = 0f;
        _template.Scale = Vector2.One * _atlasDensity;
        _template.Skew = 0f;
        _template.Visible = true;
        _viewport.RenderTargetClearMode = _nextAtlasPart == 0
            ? SubViewport.ClearMode.Once
            : SubViewport.ClearMode.Never;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        _readyAfterFrame = Engine.GetProcessFrames() + 1UL;
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
        _nextAtlasPart++;
        if (_nextAtlasPart >= (_atlasPlacements?.Count ?? 0))
        {
            _atlasComplete = true;
            if (_template is { } template && GodotObject.IsInstanceValid(template))
            {
                template.Visible = false;
            }
            ReleaseTemplate();
            _state = CaptureState.WaitingForPreparedResources;
            return;
        }

        _state = CaptureState.BuildingAtlas;
        AdvanceAtlasBuild();
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

    private static Color[] CaptureSlotColors(Node2D visual)
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
        try
        {
            var colors = new Color[slots.Count];
            for (int index = 0; index < slots.Count; index++)
            {
                GodotObject slot = slots[index];
                colors[index] = slot.HasMethod("get_color")
                    ? slot.Call("get_color").AsColor()
                    : Colors.White;
            }

            return colors;
        }
        finally
        {
            foreach (GodotObject slot in slots)
            {
                slot.Dispose();
            }
        }
    }

    private static void ApplySlotIsolation(
        Node2D visual,
        IReadOnlyList<int> visibleSlotIndices,
        Color[] originalColors)
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
            if (slots.Count != originalColors.Length)
            {
                throw new InvalidOperationException(
                    "The Spine slot list changed while the semantic atlas was captured.");
            }

            for (int index = 0; index < slots.Count; index++)
            {
                GodotObject slot = slots[index];
                if (!slot.HasMethod("set_color"))
                {
                    throw new MissingMethodException(
                        "SpineSlot.set_color is unavailable for semantic isolation.");
                }

                Color color = originalColors[index];
                if (!visible.Contains(index))
                {
                    color.A = 0f;
                }

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

    private static float ResolveMappedArea(
        BossFragmentCell cell,
        Transform2D bodyToPresentation)
    {
        if (cell.Vertices.Count < 3)
        {
            return 0f;
        }

        double twiceArea = 0d;
        for (int index = 0; index < cell.Vertices.Count; index++)
        {
            Vector2 current = bodyToPresentation * ToVector2(cell.Vertices[index]);
            Vector2 next = bodyToPresentation
                * ToVector2(cell.Vertices[(index + 1) % cell.Vertices.Count]);
            twiceArea += current.X * next.Y - next.X * current.Y;
        }

        return (float)Math.Abs(twiceArea * 0.5d);
    }

    private static Vector2 ToVector2(BossFragmentPoint point) => new(point.X, point.Y);

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
        ReleasePreparedFragments();
        ReleaseViewport();
        Partition = null;
    }

    private void ReleaseTemporaryVisuals()
    {
        ReleaseTemplate();
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

    private void ReleasePreparedFragments()
    {
        for (int index = _preparedFragments.Count - 1; index >= 0; index--)
        {
            _preparedFragments[index].Dispose();
        }

        _preparedFragments.Clear();
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
