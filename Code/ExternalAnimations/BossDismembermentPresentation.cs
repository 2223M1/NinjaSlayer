using Godot;
using Godot.Collections;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

internal readonly record struct BossDismembermentSpawn(bool Spawned, Task Completion);

public sealed partial class BossDismembermentPresentation : Node2D
{
    private const string ClipShaderPath =
        "res://NinjaSlayer/shaders/vfx/boss_dismemberment_clip.gdshader";
    private const float Gravity = 1220f;
    private const float AirDrag = 0.08f;
    private const float SceneMargin = 128f;
    private const float MaximumFlightSeconds = 4f;
    private const float MinimumBoundsSize = 12f;
    private const int MaximumHierarchyDepth = 128;

    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<FragmentState> _fragments = [];
    private readonly List<PendingFragment> _pending = [];
    private NCombatRoom _room = null!;
    private Node2D _sourceBody = null!;
    private Transform2D _sourceCanvasTransform;
    private Transform2D _canvasToPresentation;
    private Rect2 _bodyLocalBounds;
    private Rect2 _sceneBounds;
    private float _elapsed;
    private ulong _seed;

    public static IEnumerable<string> AssetPaths => [ClipShaderPath];

    internal static BossDismembermentSpawn TrySpawn(
        NCombatRoom room,
        NCreature creature,
        Vector2 bodyExplosionCenter,
        string? detachedBoneName = null,
        Vector2? detachedExplosionCenter = null,
        int zIndex = BossBurstPresentationCoordinator.FragmentZIndex)
    {
        if (!GodotObject.IsInstanceValid(room)
            || !GodotObject.IsInstanceValid(creature)
            || !GodotObject.IsInstanceValid(creature.Body)
            || !room.IsInsideTree())
        {
            return new BossDismembermentSpawn(false, Task.CompletedTask);
        }

        var presentation = new BossDismembermentPresentation
        {
            Name = "NinjaSlayerBossDismemberment",
            ZAsRelative = false,
            ZIndex = zIndex,
            _room = room,
            _sourceBody = creature.Body,
            _sourceCanvasTransform = creature.Body.GetGlobalTransformWithCanvas(),
            _seed = CreateSeed(creature)
        };
        room.CombatVfxContainer.AddChildSafely(presentation);
        if (!GodotObject.IsInstanceValid(presentation) || !presentation.IsInsideTree())
        {
            return StartOriginalFadeFallback(creature);
        }

        try
        {
            presentation.InitializeGeometry(creature);
            Vector2 bodyCenter = presentation.ToLocalPoint(bodyExplosionCenter);
            Vector2? detachedCenter = detachedExplosionCenter.HasValue
                ? presentation.ToLocalPoint(detachedExplosionCenter.Value)
                : null;
            bool spawned = presentation.TryCreateSpineFragments(
                creature,
                bodyCenter,
                detachedBoneName,
                detachedCenter);
            if (!spawned)
            {
                presentation.ClearFragments();
                spawned = presentation.TryCreateClippedBodyFragments(bodyCenter);
            }

            if (!spawned)
            {
                presentation.QueueFreeSafely();
                return StartOriginalFadeFallback(creature);
            }

            presentation.InitializeLaunches();
            creature.Body.Visible = false;
            presentation.SetProcess(true);
            return new BossDismembermentSpawn(true, presentation._completion.Task);
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn(
                $"Boss dismemberment fell back to the original disappearance for "
                + $"{creature.Entity.Monster?.Id.Entry}: {exception}");
            presentation.ClearFragments();
            presentation.QueueFreeSafely();
            return StartOriginalFadeFallback(creature);
        }
    }

    private static BossDismembermentSpawn StartOriginalFadeFallback(NCreature creature)
    {
        try
        {
            if (creature.Entity.Monster is not { ShouldFadeAfterDeath: true }
                || !GodotObject.IsInstanceValid(creature.Body)
                || !creature.Body.IsVisibleInTree())
            {
                return new BossDismembermentSpawn(false, Task.CompletedTask);
            }

            NMonsterDeathVfx? fade = NMonsterDeathVfx.Create(
                creature,
                creature.DeathAnimCancelToken.Token);
            Node? parent = creature.GetParent();
            if (fade == null || parent == null)
            {
                return new BossDismembermentSpawn(false, Task.CompletedTask);
            }

            parent.AddChildSafely(fade);
            if (GodotObject.IsInstanceValid(fade) && fade.IsInsideTree())
            {
                parent.MoveChildSafely(fade, creature.GetIndex());
                return new BossDismembermentSpawn(false, fade.PlayVfx());
            }

            fade.QueueFreeSafely();
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn($"Boss death fade fallback failed: {exception.Message}");
        }

        return new BossDismembermentSpawn(false, Task.CompletedTask);
    }

    public override void _Ready() => SetProcess(false);

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_room)
            || !_room.IsInsideTree()
            || !ReferenceEquals(NCombatRoom.Instance, _room))
        {
            CompleteAndFree();
            return;
        }

        if (BossBurstPresentationCoordinator.IsPresentationPaused(_room))
        {
            return;
        }

        float seconds = Math.Min((float)delta, 0.05f);
        if (seconds <= 0f)
        {
            return;
        }

        _elapsed += seconds;
        float drag = MathF.Exp(-AirDrag * seconds);
        for (int i = _fragments.Count - 1; i >= 0; i--)
        {
            FragmentState fragment = _fragments[i];
            fragment.Velocity = new Vector2(
                fragment.Velocity.X * drag,
                fragment.Velocity.Y + Gravity * seconds);
            fragment.Anchor.Position += fragment.Velocity * seconds;
            fragment.Anchor.RotationDegrees += fragment.AngularVelocityDegrees * seconds;

            if (IsFullyOutsideScene(fragment) || _elapsed >= MaximumFlightSeconds)
            {
                fragment.Anchor.QueueFreeSafely();
                _fragments.RemoveAt(i);
            }
        }

        if (_fragments.Count == 0)
        {
            CompleteAndFree();
        }
    }

    public override void _ExitTree() => _completion.TrySetResult();

    private void InitializeGeometry(NCreature creature)
    {
        _canvasToPresentation = GetGlobalTransformWithCanvas().AffineInverse();
        Rect2 globalBounds = creature.Visuals.Bounds.GetGlobalRect();
        Vector2[] globalCorners = RectCorners(globalBounds);
        Transform2D canvasToBody = _sourceCanvasTransform.AffineInverse();
        _bodyLocalBounds = BoundsOf(globalCorners.Select(point => canvasToBody * point));

        Transform2D sceneToCanvas = _room.SceneContainer.GetGlobalTransformWithCanvas();
        Vector2 sceneSize = _room.SceneContainer.Size;
        Vector2[] sceneCorners =
        [
            _canvasToPresentation * (sceneToCanvas * Vector2.Zero),
            _canvasToPresentation * (sceneToCanvas * new Vector2(sceneSize.X, 0f)),
            _canvasToPresentation * (sceneToCanvas * sceneSize),
            _canvasToPresentation * (sceneToCanvas * new Vector2(0f, sceneSize.Y))
        ];
        _sceneBounds = BoundsOf(sceneCorners).Grow(SceneMargin);
    }

    private bool TryCreateSpineFragments(
        NCreature creature,
        Vector2 bodyExplosionCenter,
        string? detachedBoneName,
        Vector2? detachedExplosionCenter)
    {
        if (!creature.HasSpineAnimation || creature.Visuals.IsUsingPhobiaModeBody)
        {
            return false;
        }

        SpineSource? source = ReadSpineSource(detachedBoneName);
        if (source == null || source.Slots.Count < 2)
        {
            return false;
        }

        int before = _pending.Count;
        IReadOnlyList<SpineSlotRecord> bodySlots = source.Slots
            .Where(slot => !slot.BelongsToDetachedPart)
            .ToArray();
        if (bodySlots.Count > 0)
        {
            CreateSpinePartition(
                bodySlots,
                source.Bounds,
                bodyExplosionCenter,
                detachedPart: false,
                _seed ^ 0x424F4459UL);
        }

        IReadOnlyList<SpineSlotRecord> detachedSlots = source.Slots
            .Where(slot => slot.BelongsToDetachedPart)
            .ToArray();
        if (detachedSlots.Count > 0 && detachedExplosionCenter.HasValue)
        {
            CreateSpinePartition(
                detachedSlots,
                BoundsAroundSlots(detachedSlots, source.Bounds, detachedPart: true),
                detachedExplosionCenter.Value,
                detachedPart: true,
                _seed ^ 0x50415254UL);
        }

        return _pending.Count - before >= 2;
    }

    private void CreateSpinePartition(
        IReadOnlyList<SpineSlotRecord> slots,
        Rect2 partitionBounds,
        Vector2 explosionCenter,
        bool detachedPart,
        ulong seed)
    {
        int desired = BossDismembermentMath.ResolvePieceCount(
            partitionBounds.Size.X,
            partitionBounds.Size.Y,
            slots.Count,
            detachedPart);
        if (desired <= 0)
        {
            return;
        }

        IReadOnlyList<IReadOnlyList<SpineSlotRecord>> groups = ClusterSlots(
            slots,
            desired,
            partitionBounds);
        int minimumSemanticGroups = detachedPart ? 2 : 3;
        if (groups.Count >= minimumSemanticGroups)
        {
            foreach (IReadOnlyList<SpineSlotRecord> group in groups)
            {
                Rect2 fallbackBounds = BoundsAroundSlots(group, partitionBounds, detachedPart);
                CreateSpineSlotPiece(group.Select(slot => slot.Index).ToHashSet(), fallbackBounds, explosionCenter);
            }

            return;
        }

        int clippedCount = Math.Max(minimumSemanticGroups, desired);
        IReadOnlyList<BossFragmentCell> cells = BuildCells(partitionBounds, clippedCount, seed);
        HashSet<int> visibleSlots = slots.Select(slot => slot.Index).ToHashSet();
        for (int i = 0; i < cells.Count; i++)
        {
            CreateSpineSlotPiece(visibleSlots, CellBounds(cells[i]), explosionCenter, cells, i);
        }
    }

    private bool TryCreateClippedBodyFragments(Vector2 explosionCenter)
    {
        Shader? shader = ResourceLoader.Load<Shader>(ClipShaderPath);
        if (shader == null)
        {
            return false;
        }

        int count = BossDismembermentMath.ResolvePieceCount(
            _bodyLocalBounds.Size.X,
            _bodyLocalBounds.Size.Y,
            availableParts: 9,
            detachedPart: false);
        IReadOnlyList<BossFragmentCell> cells = BuildCells(_bodyLocalBounds, count, _seed);
        int before = _pending.Count;
        for (int i = 0; i < cells.Count; i++)
        {
            Node2D? duplicate = DuplicateVisualBody();
            if (duplicate == null)
            {
                continue;
            }

            ApplyClipMaterial(duplicate, shader, cells, i);
            AddFragment(duplicate, cells[i].Vertices.Select(ToVector2).ToArray(), explosionCenter);
        }

        return _pending.Count - before >= 2;
    }

    private void CreateSpineSlotPiece(
        HashSet<int> visibleSlotIndices,
        Rect2 fallbackBounds,
        Vector2 explosionCenter,
        IReadOnlyList<BossFragmentCell>? clipCells = null,
        int clipIndex = -1)
    {
        Node2D? duplicate = DuplicateVisualBody();
        if (duplicate == null)
        {
            return;
        }

        Node2D anchor = AddCloneAtBodyPoint(duplicate, fallbackBounds.GetCenter());
        try
        {
            using var sprite = new MegaSprite(Variant.CreateFrom(duplicate));
            using MegaAnimationState? animation = sprite.TryGetAnimationState();
            animation?.SetTimeScale(0f);
            using MegaSkeleton? skeleton = sprite.GetSkeleton();
            if (skeleton == null || !skeleton.BoundObject.HasMethod("get_slots"))
            {
                throw new InvalidOperationException("The duplicated Spine skeleton has no accessible slots.");
            }

            Array<GodotObject> duplicateSlots = skeleton.BoundObject
                .Call("get_slots")
                .AsGodotArray<GodotObject>();
            for (int i = 0; i < duplicateSlots.Count; i++)
            {
                if (!visibleSlotIndices.Contains(i))
                {
                    HideSlot(duplicateSlots[i]);
                }
            }

            Rect2 visibleBounds = ValidateBounds(skeleton.GetBounds(), fallbackBounds);
            IReadOnlyList<Vector2> bodyLocalHull;
            if (clipCells != null && clipIndex >= 0)
            {
                Shader? shader = ResourceLoader.Load<Shader>(ClipShaderPath);
                if (shader == null)
                {
                    throw new InvalidOperationException("The dismemberment clip shader is unavailable.");
                }

                ApplyClipMaterial(duplicate, shader, clipCells, clipIndex);
                bodyLocalHull = clipCells[clipIndex].Vertices.Select(ToVector2).ToArray();
            }
            else
            {
                bodyLocalHull = RectCorners(visibleBounds);
            }

            RecenterClone(anchor, duplicate, BoundsOf(bodyLocalHull).GetCenter());
            AddPendingFragment(anchor, duplicate, bodyLocalHull, explosionCenter);
        }
        catch
        {
            anchor.QueueFreeSafely();
            throw;
        }
    }

    private Node2D AddCloneAtBodyPoint(Node2D duplicate, Vector2 bodyLocalPoint)
    {
        var anchor = new Node2D
        {
            Name = "BossBodyFragment",
            Position = _canvasToPresentation * (_sourceCanvasTransform * bodyLocalPoint),
            ZIndex = 2
        };
        AddChild(anchor);
        anchor.AddChild(duplicate);
        // Entering the tree can let duplicated scripts re-enable processing in _Ready.
        // Freeze the copied visual again after AddChild so fragments remain presentation-only.
        PrepareVisualClone(duplicate, isRoot: true);
        duplicate.Transform = anchor.GetGlobalTransformWithCanvas().AffineInverse()
            * _sourceCanvasTransform;
        return anchor;
    }

    private void AddFragment(
        Node2D duplicate,
        IReadOnlyList<Vector2> bodyLocalHull,
        Vector2 explosionCenter)
    {
        Vector2 center = BoundsOf(bodyLocalHull).GetCenter();
        Node2D anchor = AddCloneAtBodyPoint(duplicate, center);
        AddPendingFragment(anchor, duplicate, bodyLocalHull, explosionCenter);
    }

    private void AddPendingFragment(
        Node2D anchor,
        Node2D duplicate,
        IReadOnlyList<Vector2> bodyLocalHull,
        Vector2 explosionCenter)
    {
        Vector2[] anchorLocalHull = bodyLocalHull
            .Select(point => duplicate.Transform * point)
            .ToArray();
        float area = Math.Max(1f, PolygonArea(anchorLocalHull));
        _pending.Add(new PendingFragment(anchor, anchorLocalHull, explosionCenter, area));
    }

    private void RecenterClone(Node2D anchor, Node2D duplicate, Vector2 bodyLocalCenter)
    {
        anchor.Position = _canvasToPresentation * (_sourceCanvasTransform * bodyLocalCenter);
        duplicate.Transform = anchor.GetGlobalTransformWithCanvas().AffineInverse()
            * _sourceCanvasTransform;
    }

    private void InitializeLaunches()
    {
        float averageArea = _pending.Average(fragment => fragment.Area);
        var rng = new RandomNumberGenerator { Seed = _seed };
        foreach (PendingFragment pending in _pending)
        {
            BossFragmentLaunch launch = BossDismembermentMath.ResolveLaunch(
                new BossFragmentPoint(pending.Anchor.Position.X, pending.Anchor.Position.Y),
                new BossFragmentPoint(pending.ExplosionCenter.X, pending.ExplosionCenter.Y),
                pending.Area / averageArea,
                rng.Randf(),
                rng.Randf());
            _fragments.Add(new FragmentState(
                pending.Anchor,
                pending.LocalHull,
                new Vector2(launch.VelocityX, launch.VelocityY),
                launch.AngularVelocityDegrees));
        }

        _pending.Clear();
    }

    private SpineSource? ReadSpineSource(string? detachedBoneName)
    {
        try
        {
            using var sprite = new MegaSprite(Variant.CreateFrom(_sourceBody));
            using MegaSkeleton? skeleton = sprite.GetSkeleton();
            if (skeleton == null || !skeleton.BoundObject.HasMethod("get_slots"))
            {
                return null;
            }

            ulong? detachedBoneId = null;
            if (!string.IsNullOrWhiteSpace(detachedBoneName))
            {
                using MegaBone? detachedBone = skeleton.FindBone(detachedBoneName);
                if (detachedBone != null)
                {
                    detachedBoneId = detachedBone.BoundObject.GetInstanceId();
                }
            }

            Rect2 bounds = ValidateBounds(skeleton.GetBounds(), _bodyLocalBounds);
            Array<GodotObject> slots = skeleton.BoundObject.Call("get_slots").AsGodotArray<GodotObject>();
            var records = new List<SpineSlotRecord>(slots.Count);
            for (int i = 0; i < slots.Count; i++)
            {
                GodotObject slot = slots[i];
                if (!IsSlotVisible(slot))
                {
                    continue;
                }

                GodotObject? bone = GetSlotBone(slot);
                Vector2 point = ReadBonePoint(bone, bounds.GetCenter());
                bool detached = detachedBoneId.HasValue
                    && IsBoneDescendantOf(bone, detachedBoneId.Value);
                records.Add(new SpineSlotRecord(i, point, detached));
            }

            if (!string.IsNullOrWhiteSpace(detachedBoneName)
                && detachedBoneId == null)
            {
                Entry.Logger.Warn(
                    $"Configured boss part bone '{detachedBoneName}' was not found while splitting the body.");
            }

            return new SpineSource(records, bounds);
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn($"Spine slot splitting is unavailable; using clipped fragments: {exception.Message}");
            return null;
        }
    }

    private static IReadOnlyList<IReadOnlyList<SpineSlotRecord>> ClusterSlots(
        IReadOnlyList<SpineSlotRecord> slots,
        int desired,
        Rect2 bounds)
    {
        if (desired <= 1 || slots.Count <= 1)
        {
            return [slots];
        }

        var seeds = new List<SpineSlotRecord>(desired)
        {
            slots.OrderBy(slot => slot.Position.DistanceSquaredTo(bounds.GetCenter())).First()
        };
        while (seeds.Count < desired)
        {
            SpineSlotRecord next = slots
                .Where(candidate => !seeds.Any(seed => seed.Index == candidate.Index))
                .OrderByDescending(candidate => seeds.Min(seed => NormalizedDistanceSquared(
                    candidate.Position,
                    seed.Position,
                    bounds)))
                .ThenBy(candidate => candidate.Index)
                .First();
            seeds.Add(next);
        }

        List<SpineSlotRecord>[] groups = Enumerable.Range(0, seeds.Count)
            .Select(_ => new List<SpineSlotRecord>())
            .ToArray();
        foreach (SpineSlotRecord slot in slots)
        {
            int group = Enumerable.Range(0, seeds.Count)
                .OrderBy(index => NormalizedDistanceSquared(slot.Position, seeds[index].Position, bounds))
                .ThenBy(index => index)
                .First();
            groups[group].Add(slot);
        }

        return groups.Where(group => group.Count > 0).Cast<IReadOnlyList<SpineSlotRecord>>().ToArray();
    }

    private IReadOnlyList<BossFragmentCell> BuildCells(Rect2 bounds, int count, ulong seed) =>
        BossDismembermentMath.BuildVoronoiCells(
            new BossFragmentRect(bounds.Position.X, bounds.Position.Y, bounds.Size.X, bounds.Size.Y),
            count,
            seed);

    private static void ApplyClipMaterial(
        Node2D duplicate,
        Shader shader,
        IReadOnlyList<BossFragmentCell> cells,
        int cellIndex)
    {
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("seed_count", cells.Count);
        material.SetShaderParameter("cell_index", cellIndex);
        material.SetShaderParameter("cell_seed", ToVector2(cells[cellIndex].Seed));
        for (int i = 0; i < 9; i++)
        {
            Vector2 seed = i < cells.Count ? ToVector2(cells[i].Seed) : Vector2.Zero;
            material.SetShaderParameter($"seed_{i}", seed);
        }

        duplicate.Material = material;
    }

    private Node2D? DuplicateVisualBody()
    {
        Node.DuplicateFlags flags = Node.DuplicateFlags.Groups
            | Node.DuplicateFlags.Scripts
            | Node.DuplicateFlags.UseInstantiation;
        if (_sourceBody.Duplicate((int)flags) is not Node2D duplicate)
        {
            return null;
        }

        duplicate.Name = "FragmentVisual";
        PrepareVisualClone(duplicate, isRoot: true);
        return duplicate;
    }

    private static void PrepareVisualClone(Node node, bool isRoot)
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
            case Control control when !isRoot:
                control.Visible = false;
                break;
        }

        string name = node.Name.ToString().ToLowerInvariant();
        if (!isRoot
            && node is CanvasItem canvas
            && (name.Contains("vfx")
                || name.Contains("particle")
                || name.Contains("hitbox")
                || name.Contains("hurtbox")
                || name.Contains("intent")
                || name.Contains("shadow")))
        {
            canvas.Visible = false;
        }

        foreach (Node child in node.GetChildren())
        {
            PrepareVisualClone(child, isRoot: false);
        }
    }

    private static void HideSlot(GodotObject slot)
    {
        if (slot.HasMethod("set_attachment"))
        {
            slot.Call("set_attachment", default(Variant));
            return;
        }

        if (slot.HasMethod("set_color"))
        {
            Color color = slot.HasMethod("get_color")
                ? slot.Call("get_color").AsColor()
                : Colors.White;
            color.A = 0f;
            slot.Call("set_color", color);
        }
    }

    private static bool IsSlotVisible(GodotObject slot)
    {
        if (slot.HasMethod("get_color") && slot.Call("get_color").AsColor().A <= 0.01f)
        {
            return false;
        }

        if (!slot.HasMethod("get_attachment"))
        {
            return true;
        }

        Variant attachment = slot.Call("get_attachment");
        return attachment.VariantType != Variant.Type.Nil
            && (attachment.VariantType != Variant.Type.Object || attachment.AsGodotObject() != null);
    }

    private static GodotObject? GetSlotBone(GodotObject slot)
    {
        if (!slot.HasMethod("get_bone"))
        {
            return null;
        }

        Variant value = slot.Call("get_bone");
        return value.VariantType == Variant.Type.Object ? value.AsGodotObject() : null;
    }

    private static Vector2 ReadBonePoint(GodotObject? bone, Vector2 fallback)
    {
        if (bone == null
            || !bone.HasMethod("get_world_x")
            || !bone.HasMethod("get_world_y"))
        {
            return fallback;
        }

        return new Vector2(
            bone.Call("get_world_x").AsSingle(),
            bone.Call("get_world_y").AsSingle());
    }

    private static bool IsBoneDescendantOf(GodotObject? bone, ulong ancestorId)
    {
        GodotObject? current = bone;
        for (int depth = 0; depth < MaximumHierarchyDepth && current != null; depth++)
        {
            if (current.GetInstanceId() == ancestorId)
            {
                return true;
            }

            if (!current.HasMethod("get_parent"))
            {
                break;
            }

            Variant parent = current.Call("get_parent");
            current = parent.VariantType == Variant.Type.Object ? parent.AsGodotObject() : null;
        }

        return false;
    }

    private static Rect2 BoundsAroundSlots(
        IReadOnlyList<SpineSlotRecord> slots,
        Rect2 fallback,
        bool detachedPart)
    {
        if (slots.Count == 0)
        {
            return fallback;
        }

        Rect2 bounds = BoundsOf(slots.Select(slot => slot.Position));
        float padding = detachedPart
            ? Math.Max(42f, Math.Min(fallback.Size.X, fallback.Size.Y) * 0.14f)
            : Math.Max(28f, Math.Min(fallback.Size.X, fallback.Size.Y) * 0.08f);
        bounds = bounds.Grow(padding);
        return ValidateBounds(bounds, fallback);
    }

    private static Rect2 ValidateBounds(Rect2 candidate, Rect2 fallback) =>
        candidate.Size.X >= MinimumBoundsSize && candidate.Size.Y >= MinimumBoundsSize
            ? candidate
            : fallback;

    private static Rect2 CellBounds(BossFragmentCell cell) =>
        BoundsOf(cell.Vertices.Select(ToVector2));

    private Vector2 ToLocalPoint(Vector2 canvasPoint) => _canvasToPresentation * canvasPoint;

    private bool IsFullyOutsideScene(FragmentState fragment)
    {
        Vector2[] points = fragment.LocalHull
            .Select(point => point.Rotated(fragment.Anchor.Rotation) + fragment.Anchor.Position)
            .ToArray();
        return !_sceneBounds.Intersects(BoundsOf(points), includeBorders: true);
    }

    private void ClearFragments()
    {
        foreach (PendingFragment pending in _pending)
        {
            pending.Anchor.QueueFreeSafely();
        }

        foreach (FragmentState fragment in _fragments)
        {
            fragment.Anchor.QueueFreeSafely();
        }

        _pending.Clear();
        _fragments.Clear();
    }

    private void CompleteAndFree()
    {
        SetProcess(false);
        _completion.TrySetResult();
        this.QueueFreeSafely();
    }

    private static ulong CreateSeed(NCreature creature)
    {
        ulong combatId = unchecked((ulong)creature.Entity.CombatId.GetValueOrDefault());
        ulong modelHash = unchecked((ulong)StringComparer.Ordinal.GetHashCode(
            creature.Entity.Monster?.Id.Entry ?? creature.Name.ToString()));
        return combatId * 0x9E3779B97F4A7C15UL ^ modelHash ^ 0x4E534255525354UL;
    }

    private static Rect2 BoundsOf(IEnumerable<Vector2> points)
    {
        Vector2[] materialized = points.ToArray();
        if (materialized.Length == 0)
        {
            return default;
        }

        float minX = materialized.Min(point => point.X);
        float minY = materialized.Min(point => point.Y);
        float maxX = materialized.Max(point => point.X);
        float maxY = materialized.Max(point => point.Y);
        return new Rect2(minX, minY, maxX - minX, maxY - minY);
    }

    private static Vector2[] RectCorners(Rect2 rect) =>
    [
        rect.Position,
        rect.Position + new Vector2(rect.Size.X, 0f),
        rect.End,
        rect.Position + new Vector2(0f, rect.Size.Y)
    ];

    private static float PolygonArea(IReadOnlyList<Vector2> polygon)
    {
        double twiceArea = 0d;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 current = polygon[i];
            Vector2 next = polygon[(i + 1) % polygon.Count];
            twiceArea += current.X * next.Y - next.X * current.Y;
        }

        return (float)Math.Abs(twiceArea * 0.5d);
    }

    private static float NormalizedDistanceSquared(Vector2 first, Vector2 second, Rect2 bounds)
    {
        float dx = (first.X - second.X) / Math.Max(bounds.Size.X, 1f);
        float dy = (first.Y - second.Y) / Math.Max(bounds.Size.Y, 1f);
        return dx * dx + dy * dy;
    }

    private static Vector2 ToVector2(BossFragmentPoint point) => new(point.X, point.Y);

    private sealed record SpineSource(IReadOnlyList<SpineSlotRecord> Slots, Rect2 Bounds);

    private sealed record SpineSlotRecord(int Index, Vector2 Position, bool BelongsToDetachedPart);

    private sealed record PendingFragment(
        Node2D Anchor,
        IReadOnlyList<Vector2> LocalHull,
        Vector2 ExplosionCenter,
        float Area);

    private sealed class FragmentState(
        Node2D anchor,
        IReadOnlyList<Vector2> localHull,
        Vector2 velocity,
        float angularVelocityDegrees)
    {
        public Node2D Anchor { get; } = anchor;
        public IReadOnlyList<Vector2> LocalHull { get; } = localHull;
        public Vector2 Velocity { get; set; } = velocity;
        public float AngularVelocityDegrees { get; } = angularVelocityDegrees;
    }
}
