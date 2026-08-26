using System.Diagnostics.CodeAnalysis;
using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed record BossSemanticPartDefinition(
    ulong PrimaryBoneId,
    string PrimaryBoneName,
    IReadOnlyList<ulong> AncestorBoneIds,
    IReadOnlyList<int> SlotIndices,
    Rect2 SourceBounds,
    int DrawOrder,
    bool BelongsToDetachedPart);

internal sealed record BossAtlasSemanticPart(
    BossSemanticPartDefinition Definition,
    Rect2 AtlasUvRect);

internal sealed record BossCapturedFragmentDescriptor(
    int FragmentIndex,
    BossFragmentCell Cell,
    IReadOnlyList<BossFragmentPoint> AllSeeds,
    BossSemanticPartDefinition Part,
    Rect2 AtlasUvRect,
    float BodyAreaRatio,
    bool IsLocalSplit);

internal sealed record BossFragmentPartition(
    IReadOnlyList<BossCapturedFragmentDescriptor> Fragments,
    BossFragmentRect SourceBounds,
    int SemanticPartCount,
    int MergedPartCount,
    int SplitFragmentCount);

internal sealed class BossSemanticPartBuilder
{
    private const float TinyAreaRatio = 0.015f;
    private const float TinySpanRatio = 0.04f;
    private const float BoundsPadding = 2f;

    private readonly Node2D _template;
    private readonly Rect2 _bodyBounds;
    private readonly List<PartDraft> _parts;
    private int _measurementIndex;
    private IReadOnlyList<BossSemanticPartDefinition>? _completedParts;

    private BossSemanticPartBuilder(
        Node2D template,
        Rect2 bodyBounds,
        List<PartDraft> parts)
    {
        _template = template;
        _bodyBounds = bodyBounds;
        _parts = parts;
    }

    public bool IsMeasured => _measurementIndex >= _parts.Count;
    public int MergedPartCount { get; private set; }

    public static bool TryCreate(
        Node2D template,
        Rect2 bodyBounds,
        string? detachedBoneName,
        [NotNullWhen(true)] out BossSemanticPartBuilder? builder,
        out string failureReason)
    {
        builder = null;
        failureReason = string.Empty;
        try
        {
            List<SpineSlotSample> slots = ReadVisibleSlots(
                template,
                detachedBoneName);
            List<PartDraft> parts = slots
                .GroupBy(slot => slot.BoneId)
                .Select(group =>
                {
                    SpineSlotSample primary = group
                        .OrderBy(slot => slot.DrawOrder)
                        .First();
                    return new PartDraft(
                        primary.BoneId,
                        primary.BoneName,
                        primary.AncestorBoneIds,
                        group.Select(slot => slot.SetupIndex).Distinct().Order().ToList(),
                        default,
                        group.Min(slot => slot.DrawOrder),
                        group.Any(slot => slot.BelongsToDetachedPart));
                })
                .OrderBy(part => part.DrawOrder)
                .ToList();
            if (parts.Count < 2)
            {
                failureReason = "the Spine death pose contains fewer than two visible bone parts";
                return false;
            }

            builder = new BossSemanticPartBuilder(template, bodyBounds, parts);
            return true;
        }
        catch (Exception exception)
        {
            failureReason = $"Spine topology extraction failed: {exception.Message}";
            Entry.Logger.Warn($"Boss semantic part extraction failed: {exception}");
            return false;
        }
    }

    public int MeasureNext(int maximumParts)
    {
        if (maximumParts <= 0 || IsMeasured)
        {
            return 0;
        }

        int measured = 0;
        MegaSkeleton skeleton = GetSkeleton(_template);
        using IDisposable? skeletonLease = skeleton as IDisposable;
        Godot.Collections.Array<GodotObject> slots = GetSlots(skeleton);
        try
        {
            while (_measurementIndex < _parts.Count && measured < maximumParts)
            {
                PartDraft part = _parts[_measurementIndex++];
                Rect2 measuredBounds = MeasureIsolatedBounds(
                    skeleton,
                    slots,
                    part.SlotIndices);
                part.SourceBounds = measuredBounds.Intersects(
                    _bodyBounds,
                    includeBorders: true)
                        ? measuredBounds
                        : default;
                measured++;
            }
        }
        finally
        {
            DisposeObjects(slots);
        }

        return measured;
    }

    public IReadOnlyList<BossSemanticPartDefinition> Complete()
    {
        if (_completedParts != null)
        {
            return _completedParts;
        }

        if (!IsMeasured)
        {
            throw new InvalidOperationException(
                "Semantic Spine bounds were requested before measurement completed.");
        }

        _parts.RemoveAll(part => !IsValidBounds(part.SourceBounds));
        if (_parts.Count < 2)
        {
            throw new InvalidOperationException(
                "Fewer than two visible Spine bone parts produced valid bounds.");
        }

        MergeTinyParts();
        while (_parts.Count > BossDismembermentMath.MaximumPieces)
        {
            PartDraft source = _parts
                .OrderBy(Area)
                .ThenBy(part => part.DrawOrder)
                .First();
            PartDraft? target = FindRelatedMergeTarget(source, _parts);
            if (target == null)
            {
                throw new InvalidOperationException(
                    "The Spine contains more than sixteen unrelated visible bone branches.");
            }

            MergeInto(target, source);
            _parts.Remove(source);
            MergedPartCount++;
        }

        _completedParts = _parts
            .OrderBy(part => part.DrawOrder)
            .Select(ToDefinition)
            .ToArray();
        return _completedParts;
    }

    private void MergeTinyParts()
    {
        float bodyArea = Math.Max(1f, _bodyBounds.Size.X * _bodyBounds.Size.Y);
        bool merged;
        do
        {
            merged = false;
            PartDraft[] candidates = _parts
                .Where(part => IsTiny(part, bodyArea, _bodyBounds))
                .OrderBy(Area)
                .ThenBy(part => part.DrawOrder)
                .ToArray();
            foreach (PartDraft source in candidates)
            {
                if (!_parts.Contains(source) || _parts.Count <= 2)
                {
                    continue;
                }

                PartDraft? target = FindRelatedMergeTarget(source, _parts);
                if (target == null)
                {
                    continue;
                }

                MergeInto(target, source);
                _parts.Remove(source);
                MergedPartCount++;
                merged = true;
            }
        }
        while (merged);
    }

    private static Rect2 MeasureIsolatedBounds(
        MegaSkeleton skeleton,
        Godot.Collections.Array<GodotObject> slots,
        IReadOnlyList<int> visibleSlotIndices)
    {
        var visible = new HashSet<int>(visibleSlotIndices);
        var attachments = new Variant[slots.Count];
        var canRestore = new bool[slots.Count];
        try
        {
            for (int index = 0; index < slots.Count; index++)
            {
                GodotObject slot = slots[index];
                if (!slot.HasMethod("get_attachment")
                    || !slot.HasMethod("set_attachment"))
                {
                    continue;
                }

                attachments[index] = slot.Call("get_attachment");
                canRestore[index] = true;
                if (!visible.Contains(index))
                {
                    slot.Call("set_attachment", default(Variant));
                }
            }

            Rect2 bounds = skeleton.GetBounds();
            return IsValidBounds(bounds) ? bounds.Grow(BoundsPadding) : default;
        }
        finally
        {
            for (int index = 0; index < slots.Count; index++)
            {
                if (canRestore[index]
                    && GodotObject.IsInstanceValid(slots[index])
                    && slots[index].HasMethod("set_attachment"))
                {
                    slots[index].Call("set_attachment", attachments[index]);
                }
            }
        }
    }

    private static List<SpineSlotSample> ReadVisibleSlots(
        Node2D template,
        string? detachedBoneName)
    {
        MegaSkeleton skeleton = GetSkeleton(template);
        using IDisposable? skeletonLease = skeleton as IDisposable;
        Godot.Collections.Array<GodotObject> slots = GetSlots(skeleton);
        try
        {
            Dictionary<string, BoneTopology> bones = ReadBoneTopology(
                skeleton,
                slots);
            Dictionary<string, int> drawOrder = ReadDrawOrder(skeleton);
            var result = new List<SpineSlotSample>(slots.Count);
            for (int setupIndex = 0; setupIndex < slots.Count; setupIndex++)
            {
                GodotObject slot = slots[setupIndex];
                if (!IsSlotVisible(slot))
                {
                    continue;
                }

                GodotObject? bone = CallObject(slot, "get_bone");
                GodotObject? attachment = CallObject(slot, "get_attachment");
                GodotObject? slotData = CallObject(slot, "get_data");
                try
                {
                    string boneName = ReadBoneName(bone);
                    string slotName = ReadString(slotData, "get_slot_name", "get_name");
                    string attachmentName = ReadString(attachment, "get_name");
                    if (IsShadow(slotName, attachmentName, boneName)
                        || string.IsNullOrWhiteSpace(boneName)
                        || !bones.TryGetValue(boneName, out BoneTopology? topology))
                    {
                        continue;
                    }

                    bool detached = !string.IsNullOrWhiteSpace(detachedBoneName)
                        && (string.Equals(
                                topology.Name,
                                detachedBoneName,
                                StringComparison.OrdinalIgnoreCase)
                            || topology.AncestorNames.Contains(
                                detachedBoneName,
                                StringComparer.OrdinalIgnoreCase));
                    int order = !string.IsNullOrWhiteSpace(slotName)
                        && drawOrder.TryGetValue(slotName, out int resolvedOrder)
                            ? resolvedOrder
                            : setupIndex;
                    result.Add(new SpineSlotSample(
                        setupIndex,
                        order,
                        topology.Id,
                        topology.Name,
                        topology.AncestorIds,
                        detached));
                }
                finally
                {
                    slotData?.Dispose();
                    attachment?.Dispose();
                    bone?.Dispose();
                }
            }

            return result;
        }
        finally
        {
            DisposeObjects(slots);
        }
    }

    private static Dictionary<string, BoneTopology> ReadBoneTopology(
        MegaSkeleton skeleton,
        Godot.Collections.Array<GodotObject> slots)
    {
        var raw = new List<RawBoneTopology>();
        if (skeleton.BoundObject.HasMethod("get_bones"))
        {
            Godot.Collections.Array<GodotObject> bones = skeleton.BoundObject
                .Call("get_bones")
                .AsGodotArray<GodotObject>();
            try
            {
                for (int index = 0; index < bones.Count; index++)
                {
                    AddRawBone(raw, bones[index], index);
                }
            }
            finally
            {
                DisposeObjects(bones);
            }
        }

        if (raw.Count == 0)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < slots.Count; index++)
            {
                GodotObject? bone = CallObject(slots[index], "get_bone");
                try
                {
                    AddBoneAndParents(raw, names, bone);
                }
                finally
                {
                    bone?.Dispose();
                }
            }
        }

        var rawByName = raw
            .Where(bone => !string.IsNullOrWhiteSpace(bone.Name))
            .GroupBy(bone => bone.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var result = new Dictionary<string, BoneTopology>(StringComparer.Ordinal);
        foreach (RawBoneTopology bone in rawByName.Values.OrderBy(bone => bone.Index))
        {
            var ancestorIds = new List<ulong>();
            var ancestorNames = new List<string>();
            string? parentName = bone.ParentName;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (!string.IsNullOrWhiteSpace(parentName)
                   && visited.Add(parentName)
                   && rawByName.TryGetValue(parentName, out RawBoneTopology? parent))
            {
                ancestorIds.Add(ToBoneId(parent.Index));
                ancestorNames.Add(parent.Name);
                parentName = parent.ParentName;
            }

            result[bone.Name] = new BoneTopology(
                ToBoneId(bone.Index),
                bone.Name,
                ancestorIds,
                ancestorNames);
        }

        return result;
    }

    private static void AddRawBone(
        ICollection<RawBoneTopology> bones,
        GodotObject bone,
        int index)
    {
        string name = ReadBoneName(bone);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        GodotObject? parent = CallParentObject(bone);
        try
        {
            bones.Add(new RawBoneTopology(index, name, ReadBoneName(parent)));
        }
        finally
        {
            parent?.Dispose();
        }
    }

    private static void AddBoneAndParents(
        ICollection<RawBoneTopology> bones,
        HashSet<string> names,
        GodotObject? bone)
    {
        GodotObject? current = bone;
        bool ownsCurrent = false;
        try
        {
            while (current != null)
            {
                string name = ReadBoneName(current);
                GodotObject? parent = CallParentObject(current);
                string parentName = ReadBoneName(parent);
                if (!string.IsNullOrWhiteSpace(name) && names.Add(name))
                {
                    bones.Add(new RawBoneTopology(bones.Count, name, parentName));
                }

                if (ownsCurrent)
                {
                    current.Dispose();
                }

                current = parent;
                ownsCurrent = true;
            }
        }
        finally
        {
            if (ownsCurrent)
            {
                current?.Dispose();
            }
        }
    }

    private static Dictionary<string, int> ReadDrawOrder(MegaSkeleton skeleton)
    {
        if (!skeleton.BoundObject.HasMethod("get_draw_order"))
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        Godot.Collections.Array<GodotObject> slots = skeleton.BoundObject
            .Call("get_draw_order")
            .AsGodotArray<GodotObject>();
        try
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < slots.Count; index++)
            {
                GodotObject? data = CallObject(slots[index], "get_data");
                try
                {
                    string name = ReadString(data, "get_slot_name", "get_name");
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        result.TryAdd(name, index);
                    }
                }
                finally
                {
                    data?.Dispose();
                }
            }

            return result;
        }
        finally
        {
            DisposeObjects(slots);
        }
    }

    private static MegaSkeleton GetSkeleton(Node2D visual)
    {
        var sprite = new MegaSprite(Variant.CreateFrom(visual));
        return sprite.GetSkeleton()
            ?? throw new InvalidOperationException("The duplicated Spine skeleton is not ready.");
    }

    private static Godot.Collections.Array<GodotObject> GetSlots(MegaSkeleton skeleton)
    {
        if (!skeleton.BoundObject.HasMethod("get_slots"))
        {
            throw new MissingMethodException("SpineSkeleton.get_slots is unavailable.");
        }

        Godot.Collections.Array<GodotObject> slots = skeleton.BoundObject
            .Call("get_slots")
            .AsGodotArray<GodotObject>();
        if (slots.Count == 0)
        {
            DisposeObjects(slots);
            throw new InvalidOperationException("The Spine skeleton has no accessible slots.");
        }

        return slots;
    }

    private static bool IsSlotVisible(GodotObject slot)
    {
        if (slot.HasMethod("get_color")
            && slot.Call("get_color").AsColor().A <= 0.01f)
        {
            return false;
        }

        GodotObject? attachment = CallObject(slot, "get_attachment");
        try
        {
            return attachment != null;
        }
        finally
        {
            attachment?.Dispose();
        }
    }

    private static bool IsShadow(params string[] names) => names.Any(name =>
        name.Contains("floor_shadow", StringComparison.OrdinalIgnoreCase)
        || name.Contains("ground_shadow", StringComparison.OrdinalIgnoreCase)
        || name.Contains("shadow", StringComparison.OrdinalIgnoreCase));

    private static string ReadBoneName(GodotObject? bone)
    {
        if (bone == null)
        {
            return string.Empty;
        }

        string directName = ReadString(bone, "get_bone_name", "get_name");
        if (!string.IsNullOrWhiteSpace(directName))
        {
            return directName;
        }

        GodotObject? data = CallObject(bone, "get_data");
        try
        {
            return ReadString(data, "get_bone_name", "get_name");
        }
        finally
        {
            data?.Dispose();
        }
    }

    private static string ReadString(GodotObject? owner, params string[] methods)
    {
        if (owner == null)
        {
            return string.Empty;
        }

        foreach (string method in methods)
        {
            if (!owner.HasMethod(method))
            {
                continue;
            }

            Variant value = owner.Call(method);
            string text = value.AsString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private static GodotObject? CallObject(GodotObject owner, string method)
    {
        if (!owner.HasMethod(method))
        {
            return null;
        }

        Variant value = owner.Call(method);
        return value.VariantType == Variant.Type.Object ? value.AsGodotObject() : null;
    }

    private static GodotObject? CallParentObject(GodotObject owner)
    {
        GodotObject? parent = CallObject(owner, "get_parent");
        if (parent != null)
        {
            return parent;
        }

        GodotObject? data = CallObject(owner, "get_data");
        try
        {
            return data == null ? null : CallObject(data, "get_parent");
        }
        finally
        {
            data?.Dispose();
        }
    }

    private static ulong ToBoneId(int boneIndex) => checked((ulong)boneIndex + 1UL);

    private static void DisposeObjects(Godot.Collections.Array<GodotObject> objects)
    {
        foreach (GodotObject item in objects)
        {
            item.Dispose();
        }
    }

    private static bool IsTiny(PartDraft part, float bodyArea, Rect2 bodyBounds)
    {
        float areaRatio = Area(part) / bodyArea;
        float spanRatio = Math.Max(
            part.SourceBounds.Size.X / Math.Max(1f, bodyBounds.Size.X),
            part.SourceBounds.Size.Y / Math.Max(1f, bodyBounds.Size.Y));
        return areaRatio < TinyAreaRatio || spanRatio < TinySpanRatio;
    }

    private static PartDraft? FindRelatedMergeTarget(
        PartDraft source,
        IReadOnlyList<PartDraft> parts)
    {
        foreach (ulong ancestorId in source.AncestorBoneIds)
        {
            PartDraft? ancestor = parts.FirstOrDefault(part =>
                !ReferenceEquals(part, source)
                && part.PrimaryBoneId == ancestorId);
            if (ancestor != null)
            {
                return ancestor;
            }
        }

        return parts
            .Where(part => !ReferenceEquals(part, source))
            .Select(part => new
            {
                Part = part,
                CommonDepth = CommonAncestorDepth(source, part)
            })
            .Where(candidate => candidate.CommonDepth >= 0)
            .OrderByDescending(candidate => candidate.CommonDepth)
            .ThenBy(candidate => Math.Abs(candidate.Part.DrawOrder - source.DrawOrder))
            .ThenBy(candidate => Area(candidate.Part))
            .Select(candidate => candidate.Part)
            .FirstOrDefault();
    }

    private static int CommonAncestorDepth(PartDraft first, PartDraft second)
    {
        if (first.AncestorBoneIds.Contains(second.PrimaryBoneId))
        {
            return int.MaxValue;
        }

        if (second.AncestorBoneIds.Contains(first.PrimaryBoneId))
        {
            return int.MaxValue - 1;
        }

        for (int firstIndex = 0; firstIndex < first.AncestorBoneIds.Count; firstIndex++)
        {
            for (int secondIndex = 0;
                 secondIndex < second.AncestorBoneIds.Count;
                 secondIndex++)
            {
                if (second.AncestorBoneIds[secondIndex]
                    == first.AncestorBoneIds[firstIndex])
                {
                    return 10_000 - firstIndex - secondIndex;
                }
            }
        }

        return -1;
    }

    private static void MergeInto(PartDraft target, PartDraft source)
    {
        target.SlotIndices.AddRange(source.SlotIndices);
        target.SlotIndices.Sort();
        target.SlotIndices = target.SlotIndices.Distinct().ToList();
        target.SourceBounds = target.SourceBounds.Merge(source.SourceBounds);
        target.DrawOrder = Math.Min(target.DrawOrder, source.DrawOrder);
        target.BelongsToDetachedPart |= source.BelongsToDetachedPart;
    }

    private static BossSemanticPartDefinition ToDefinition(PartDraft part) => new(
        part.PrimaryBoneId,
        part.PrimaryBoneName,
        part.AncestorBoneIds.ToArray(),
        part.SlotIndices.ToArray(),
        part.SourceBounds,
        part.DrawOrder,
        part.BelongsToDetachedPart);

    private static float Area(PartDraft part) =>
        Math.Max(0f, part.SourceBounds.Size.X * part.SourceBounds.Size.Y);

    private static bool IsValidBounds(Rect2 bounds) =>
        float.IsFinite(bounds.Position.X)
        && float.IsFinite(bounds.Position.Y)
        && float.IsFinite(bounds.Size.X)
        && float.IsFinite(bounds.Size.Y)
        && bounds.Size.X > 1f
        && bounds.Size.Y > 1f;

    private sealed class PartDraft(
        ulong primaryBoneId,
        string primaryBoneName,
        IReadOnlyList<ulong> ancestorBoneIds,
        List<int> slotIndices,
        Rect2 sourceBounds,
        int drawOrder,
        bool belongsToDetachedPart)
    {
        public ulong PrimaryBoneId { get; } = primaryBoneId;
        public string PrimaryBoneName { get; } = primaryBoneName;
        public IReadOnlyList<ulong> AncestorBoneIds { get; } = ancestorBoneIds;
        public List<int> SlotIndices { get; set; } = slotIndices;
        public Rect2 SourceBounds { get; set; } = sourceBounds;
        public int DrawOrder { get; set; } = drawOrder;
        public bool BelongsToDetachedPart { get; set; } = belongsToDetachedPart;
    }

    private sealed record SpineSlotSample(
        int SetupIndex,
        int DrawOrder,
        ulong BoneId,
        string BoneName,
        IReadOnlyList<ulong> AncestorBoneIds,
        bool BelongsToDetachedPart);

    private sealed record RawBoneTopology(int Index, string Name, string ParentName);

    private sealed record BoneTopology(
        ulong Id,
        string Name,
        IReadOnlyList<ulong> AncestorIds,
        IReadOnlyList<string> AncestorNames);
}

internal static class BossFragmentPartitioner
{
    private const float OversizedAreaRatio = 0.22f;
    private const float OversizedSpanRatio = 0.45f;

    public static BossFragmentPartition BuildSemanticPartition(
        IReadOnlyList<BossAtlasSemanticPart> atlasParts,
        ulong seed,
        int mergedPartCount)
    {
        if (atlasParts.Count < 2)
        {
            throw new InvalidOperationException(
                "A semantic boss partition requires at least two atlas parts.");
        }

        Rect2 semanticSourceBounds = atlasParts
            .Select(part => part.Definition.SourceBounds)
            .Aggregate((current, next) => current.Merge(next));
        float bodyArea = Math.Max(
            1f,
            semanticSourceBounds.Size.X * semanticSourceBounds.Size.Y);
        int availableFragments = BossDismembermentMath.MaximumPieces - atlasParts.Count;
        var splitCounts = atlasParts.ToDictionary(part => part, _ => 1);
        foreach (BossAtlasSemanticPart part in atlasParts
                     .OrderByDescending(part => ResolveOversizedScore(
                          part.Definition.SourceBounds,
                          semanticSourceBounds)))
        {
            int desired = ResolveLocalSplitCount(
                part.Definition.SourceBounds,
                semanticSourceBounds);
            int additional = Math.Min(Math.Max(0, desired - 1), availableFragments);
            splitCounts[part] += additional;
            availableFragments -= additional;
        }

        var descriptors = new List<BossCapturedFragmentDescriptor>(
            BossDismembermentMath.MaximumPieces);
        int splitFragmentCount = 0;
        foreach (BossAtlasSemanticPart atlasPart in atlasParts
                     .OrderBy(part => part.Definition.DrawOrder))
        {
            BossSemanticPartDefinition part = atlasPart.Definition;
            int splitCount = splitCounts[atlasPart];
            IReadOnlyList<BossFragmentCell> cells = splitCount == 1
                ? [BuildRectangleCell(part.SourceBounds)]
                : BossDismembermentMath.BuildVoronoiCells(
                    ToFragmentRect(part.SourceBounds),
                    splitCount,
                    seed ^ part.PrimaryBoneId);
            BossFragmentPoint[] seeds = splitCount == 1
                ? []
                : cells.Select(cell => cell.Seed).ToArray();
            splitFragmentCount += Math.Max(0, cells.Count - 1);
            foreach (BossFragmentCell cell in cells)
            {
                descriptors.Add(new BossCapturedFragmentDescriptor(
                    descriptors.Count,
                    cell,
                    seeds,
                    part,
                    atlasPart.AtlasUvRect,
                    Math.Clamp(cell.Area / bodyArea, 0.0001f, 1f),
                    splitCount > 1));
            }
        }

        return new BossFragmentPartition(
            descriptors,
            ToFragmentRect(semanticSourceBounds),
            atlasParts.Count,
            mergedPartCount,
            splitFragmentCount);
    }

    public static bool TryMergeSmallestRelatedPart(
        IReadOnlyList<BossSemanticPartDefinition> source,
        out IReadOnlyList<BossSemanticPartDefinition> merged)
    {
        merged = source;
        if (source.Count <= 2)
        {
            return false;
        }

        BossSemanticPartDefinition smallest = source
            .OrderBy(part => part.SourceBounds.Size.X * part.SourceBounds.Size.Y)
            .ThenBy(part => part.DrawOrder)
            .First();
        BossSemanticPartDefinition? target = FindRelatedTarget(smallest, source);
        if (target == null)
        {
            return false;
        }

        var replacement = target with
        {
            SlotIndices = target.SlotIndices
                .Concat(smallest.SlotIndices)
                .Distinct()
                .Order()
                .ToArray(),
            SourceBounds = target.SourceBounds.Merge(smallest.SourceBounds),
            DrawOrder = Math.Min(target.DrawOrder, smallest.DrawOrder),
            BelongsToDetachedPart = target.BelongsToDetachedPart
                || smallest.BelongsToDetachedPart
        };
        merged = source
            .Where(part => !ReferenceEquals(part, smallest)
                && !ReferenceEquals(part, target))
            .Append(replacement)
            .OrderBy(part => part.DrawOrder)
            .ToArray();
        return true;
    }

    internal static int ResolveLocalSplitCount(Rect2 partBounds, Rect2 bodyBounds)
    {
        float areaRatio = partBounds.Size.X * partBounds.Size.Y
            / Math.Max(1f, bodyBounds.Size.X * bodyBounds.Size.Y);
        float spanRatio = Math.Max(
            partBounds.Size.X / Math.Max(1f, bodyBounds.Size.X),
            partBounds.Size.Y / Math.Max(1f, bodyBounds.Size.Y));
        if (areaRatio <= OversizedAreaRatio && spanRatio <= OversizedSpanRatio)
        {
            return 1;
        }

        return Math.Clamp(
            Math.Max(
                Mathf.CeilToInt(areaRatio / 0.18f),
                Mathf.CeilToInt(spanRatio / 0.38f)),
            2,
            4);
    }

    private static float ResolveOversizedScore(Rect2 partBounds, Rect2 bodyBounds)
    {
        float areaRatio = partBounds.Size.X * partBounds.Size.Y
            / Math.Max(1f, bodyBounds.Size.X * bodyBounds.Size.Y);
        float spanRatio = Math.Max(
            partBounds.Size.X / Math.Max(1f, bodyBounds.Size.X),
            partBounds.Size.Y / Math.Max(1f, bodyBounds.Size.Y));
        return Math.Max(
            areaRatio / OversizedAreaRatio,
            spanRatio / OversizedSpanRatio);
    }

    private static BossSemanticPartDefinition? FindRelatedTarget(
        BossSemanticPartDefinition source,
        IReadOnlyList<BossSemanticPartDefinition> parts)
    {
        foreach (ulong ancestorId in source.AncestorBoneIds)
        {
            BossSemanticPartDefinition? ancestor = parts.FirstOrDefault(part =>
                !ReferenceEquals(part, source)
                && part.PrimaryBoneId == ancestorId);
            if (ancestor != null)
            {
                return ancestor;
            }
        }

        return parts
            .Where(part => !ReferenceEquals(part, source))
            .Where(part => part.AncestorBoneIds.Intersect(source.AncestorBoneIds).Any()
                || part.AncestorBoneIds.Contains(source.PrimaryBoneId)
                || source.AncestorBoneIds.Contains(part.PrimaryBoneId))
            .OrderBy(part => Math.Abs(part.DrawOrder - source.DrawOrder))
            .ThenBy(part => part.SourceBounds.Size.X * part.SourceBounds.Size.Y)
            .FirstOrDefault();
    }

    private static BossFragmentCell BuildRectangleCell(Rect2 bounds)
    {
        BossFragmentPoint[] vertices =
        [
            new(bounds.Position.X, bounds.Position.Y),
            new(bounds.End.X, bounds.Position.Y),
            new(bounds.End.X, bounds.End.Y),
            new(bounds.Position.X, bounds.End.Y)
        ];
        Vector2 center = bounds.GetCenter();
        return new BossFragmentCell(
            new BossFragmentPoint(center.X, center.Y),
            vertices);
    }

    private static BossFragmentRect ToFragmentRect(Rect2 bounds) => new(
        bounds.Position.X,
        bounds.Position.Y,
        bounds.Size.X,
        bounds.Size.Y);
}
