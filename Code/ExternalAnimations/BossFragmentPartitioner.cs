using Godot;
using Godot.Collections;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed record BossFragmentPartition(
    IReadOnlyList<BossFragmentCell> Cells,
    IReadOnlySet<int> DetachedCellIndices);

internal static class BossFragmentPartitioner
{
    private const int MaximumHierarchyDepth = 128;

    public static BossFragmentPartition Build(
        Node2D? capturedVisual,
        Rect2 bodyLocalBounds,
        bool canSplitSpine,
        ulong seed,
        string? detachedBoneName,
        bool hasDetachedBurst)
    {
        BossFragmentRect bounds = new(
            bodyLocalBounds.Position.X,
            bodyLocalBounds.Position.Y,
            bodyLocalBounds.Size.X,
            bodyLocalBounds.Size.Y);
        SpineSource? source = canSplitSpine
            ? ReadSpineSource(capturedVisual, bodyLocalBounds, detachedBoneName)
            : null;
        if (source != null && source.Slots.Count >= 2)
        {
            SpineSlotRecord[] bodySlots = source.Slots
                .Where(slot => !slot.BelongsToDetachedPart)
                .ToArray();
            SpineSlotRecord[] detachedSlots = source.Slots
                .Where(slot => slot.BelongsToDetachedPart)
                .ToArray();
            BossFragmentAllocation allocation = BossDismembermentMath.AllocateSpinePieces(
                bodySlots.Length,
                hasDetachedBurst ? detachedSlots.Length : 0);
            var seeds = new List<BossFragmentPoint>(BossDismembermentMath.MaximumPieces);
            AddClusterSeeds(seeds, bodySlots, allocation.BodyPieces, source.Bounds);
            int detachedSeedStart = seeds.Count;
            if (hasDetachedBurst)
            {
                AddClusterSeeds(seeds, detachedSlots, allocation.DetachedPieces, source.Bounds);
            }

            EnsureDistinctSeeds(seeds);
            IReadOnlyList<BossFragmentCell> semanticCells =
                BossDismembermentMath.BuildVoronoiCells(bounds, seeds);
            if (semanticCells.Count >= 2)
            {
                return new BossFragmentPartition(
                    semanticCells,
                    ResolveDetachedCells(semanticCells, seeds, detachedSeedStart));
            }
        }

        int count = BossDismembermentMath.ResolvePieceCount(
            bodyLocalBounds.Size.X,
            bodyLocalBounds.Size.Y,
            BossDismembermentMath.MaximumPieces,
            detachedPart: false);
        return new BossFragmentPartition(
            BossDismembermentMath.BuildVoronoiCells(bounds, count, seed),
            new HashSet<int>());
    }

    private static IReadOnlySet<int> ResolveDetachedCells(
        IReadOnlyList<BossFragmentCell> cells,
        IReadOnlyList<BossFragmentPoint> seeds,
        int detachedSeedStart)
    {
        var detached = new HashSet<int>();
        if (detachedSeedStart >= seeds.Count)
        {
            return detached;
        }

        for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
        {
            BossFragmentPoint cellSeed = cells[cellIndex].Seed;
            int nearestSeed = Enumerable.Range(0, seeds.Count)
                .OrderBy(index => DistanceSquared(cellSeed, seeds[index]))
                .ThenBy(index => index)
                .First();
            if (nearestSeed >= detachedSeedStart)
            {
                detached.Add(cellIndex);
            }
        }

        return detached;
    }

    private static SpineSource? ReadSpineSource(
        Node2D? visual,
        Rect2 bodyLocalBounds,
        string? detachedBoneName)
    {
        if (visual == null)
        {
            return null;
        }

        try
        {
            var sprite = new MegaSprite(Variant.CreateFrom(visual));
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

            Rect2 bounds = ValidateBounds(skeleton.GetBounds(), bodyLocalBounds);
            Array<GodotObject> slots = skeleton.BoundObject.Call("get_slots").AsGodotArray<GodotObject>();
            var records = new List<SpineSlotRecord>(slots.Count);
            for (int index = 0; index < slots.Count; index++)
            {
                GodotObject slot = slots[index];
                if (!IsSlotVisible(slot))
                {
                    continue;
                }

                GodotObject? bone = GetSlotBone(slot);
                records.Add(new SpineSlotRecord(
                    ReadBonePoint(bone, bounds.GetCenter()),
                    detachedBoneId.HasValue && IsBoneDescendantOf(bone, detachedBoneId.Value)));
            }

            return new SpineSource(records, bounds);
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn(
                $"Spine partition seeds are unavailable; using spatial cells: {exception.Message}");
            return null;
        }
    }

    private static void AddClusterSeeds(
        ICollection<BossFragmentPoint> destination,
        IReadOnlyList<SpineSlotRecord> slots,
        int desired,
        Rect2 bounds)
    {
        IReadOnlyList<IReadOnlyList<SpineSlotRecord>> groups = ClusterSlots(slots, desired, bounds);
        foreach (IReadOnlyList<SpineSlotRecord> group in groups)
        {
            if (group.Count == 0)
            {
                continue;
            }

            destination.Add(new BossFragmentPoint(
                group.Average(slot => slot.Position.X),
                group.Average(slot => slot.Position.Y)));
        }
    }

    private static IReadOnlyList<IReadOnlyList<SpineSlotRecord>> ClusterSlots(
        IReadOnlyList<SpineSlotRecord> slots,
        int desired,
        Rect2 bounds)
    {
        desired = Math.Min(Math.Max(0, desired), slots.Count);
        if (desired <= 0 || slots.Count == 0)
        {
            return [];
        }

        if (desired == 1 || slots.Count == 1)
        {
            return [slots];
        }

        int firstSeed = Enumerable.Range(0, slots.Count)
            .OrderBy(index => slots[index].Position.DistanceSquaredTo(bounds.GetCenter()))
            .ThenBy(index => index)
            .First();
        var seedIndices = new List<int>(desired) { firstSeed };
        var selected = new HashSet<int> { firstSeed };
        while (seedIndices.Count < desired)
        {
            int next = Enumerable.Range(0, slots.Count)
                .Where(index => !selected.Contains(index))
                .OrderByDescending(index => seedIndices.Min(seedIndex => NormalizedDistanceSquared(
                    slots[index].Position,
                    slots[seedIndex].Position,
                    bounds)))
                .ThenBy(index => index)
                .First();
            seedIndices.Add(next);
            selected.Add(next);
        }

        List<SpineSlotRecord>[] groups = Enumerable.Range(0, seedIndices.Count)
            .Select(_ => new List<SpineSlotRecord>())
            .ToArray();
        for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++)
        {
            int group = Enumerable.Range(0, seedIndices.Count)
                .OrderBy(index => NormalizedDistanceSquared(
                    slots[slotIndex].Position,
                    slots[seedIndices[index]].Position,
                    bounds))
                .ThenBy(index => index)
                .First();
            groups[group].Add(slots[slotIndex]);
        }

        return groups
            .Where(group => group.Count > 0)
            .Cast<IReadOnlyList<SpineSlotRecord>>()
            .ToArray();
    }

    private static void EnsureDistinctSeeds(IList<BossFragmentPoint> seeds)
    {
        for (int index = 0; index < seeds.Count; index++)
        {
            BossFragmentPoint seed = seeds[index];
            for (int attempt = 0; attempt < seeds.Count; attempt++)
            {
                bool overlaps = false;
                for (int previous = 0; previous < index; previous++)
                {
                    float dx = seed.X - seeds[previous].X;
                    float dy = seed.Y - seeds[previous].Y;
                    if (dx * dx + dy * dy >= 1f)
                    {
                        continue;
                    }

                    overlaps = true;
                    break;
                }

                if (!overlaps)
                {
                    break;
                }

                float angle = (index + attempt * 0.61803398875f) * 2.39996323f;
                float radius = 2f + attempt;
                seed = new BossFragmentPoint(
                    seed.X + MathF.Cos(angle) * radius,
                    seed.Y + MathF.Sin(angle) * radius);
            }

            seeds[index] = seed;
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
        if (bone == null || !bone.HasMethod("get_world_x") || !bone.HasMethod("get_world_y"))
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

    private static Rect2 ValidateBounds(Rect2 candidate, Rect2 fallback) =>
        candidate.Size.X >= 12f && candidate.Size.Y >= 12f ? candidate : fallback;

    private static float NormalizedDistanceSquared(Vector2 first, Vector2 second, Rect2 bounds)
    {
        float dx = (first.X - second.X) / Math.Max(bounds.Size.X, 1f);
        float dy = (first.Y - second.Y) / Math.Max(bounds.Size.Y, 1f);
        return dx * dx + dy * dy;
    }

    private static float DistanceSquared(BossFragmentPoint first, BossFragmentPoint second)
    {
        float dx = first.X - second.X;
        float dy = first.Y - second.Y;
        return dx * dx + dy * dy;
    }

    private sealed record SpineSource(IReadOnlyList<SpineSlotRecord> Slots, Rect2 Bounds);
    private sealed record SpineSlotRecord(Vector2 Position, bool BelongsToDetachedPart);
}
