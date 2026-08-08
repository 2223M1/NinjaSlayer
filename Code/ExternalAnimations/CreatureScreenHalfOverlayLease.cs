using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed partial class CreatureScreenHalfOverlayLease : IDisposable
{
    private const int MaximumCanvasZIndex = 4095;
    private const float MaskMargin = 64f;

    private readonly NCombatRoom _room;
    private readonly float _splitCanvasX;
    private readonly ScreenHalfMask _mask;
    private int _disposed;

    private CreatureScreenHalfOverlayLease(
        NCombatRoom room,
        float splitCanvasX,
        ScreenHalfMask mask)
    {
        _room = room;
        _splitCanvasX = splitCanvasX;
        _mask = mask;
    }

    internal static CreatureScreenHalfOverlayLease? TryAcquire(
        NCombatRoom room,
        Creature target,
        int zIndex)
    {
        ScreenHalfMask? mask = null;
        try
        {
            NCreature? targetNode = room.GetCreatureNode(target);
            Node2D? sourceBody = targetNode?.Visuals.GetCurrentBody();
            if (targetNode == null
                || sourceBody == null
                || !GodotObject.IsInstanceValid(sourceBody)
                || sourceBody.Duplicate((int)Node.DuplicateFlags.Groups) is not Node2D duplicate)
            {
                return null;
            }

            PrepareVisualClone(duplicate);
            mask = new ScreenHalfMask
            {
                Name = "DarkNinjaTargetLeftHalf",
                ZAsRelative = false,
                ZIndex = Math.Clamp(zIndex, -MaximumCanvasZIndex, MaximumCanvasZIndex),
                ClipChildren = CanvasItem.ClipChildrenMode.Only
            };
            room.SceneContainer.AddChildSafely(mask);
            duplicate.Name = "FrozenTargetBody";
            duplicate.TopLevel = false;
            duplicate.ZAsRelative = true;
            duplicate.ZIndex = 0;
            mask.AddChildSafely(duplicate);
            FreezeSpineAnimations(duplicate);
            duplicate.Transform = mask.GetGlobalTransformWithCanvas().AffineInverse()
                * sourceBody.GetGlobalTransformWithCanvas();

            Marker2D marker = targetNode.Visuals.VfxSpawnPosition;
            float splitCanvasX = GodotObject.IsInstanceValid(marker)
                ? marker.GetGlobalTransformWithCanvas().Origin.X
                : targetNode.Visuals.Bounds.GetGlobalRect().GetCenter().X;
            var lease = new CreatureScreenHalfOverlayLease(room, splitCanvasX, mask);
            lease.SyncMask();
            return lease;
        }
        catch (Exception exception)
        {
            if (mask != null && GodotObject.IsInstanceValid(mask))
            {
                mask.QueueFreeSafely();
            }

            Entry.Logger.Warn($"Dark Strike target layering fell back to normal rendering: {exception.Message}");
            return null;
        }
    }

    internal void SyncMask()
    {
        if (Volatile.Read(ref _disposed) != 0
            || !GodotObject.IsInstanceValid(_room)
            || !GodotObject.IsInstanceValid(_mask)
            || !_mask.IsInsideTree())
        {
            return;
        }

        Vector2 viewportSize = _room.GetViewport().GetVisibleRect().Size;
        Transform2D canvasToMask = _mask.GetGlobalTransformWithCanvas().AffineInverse();
        Vector2 topLeft = canvasToMask * new Vector2(-MaskMargin, -MaskMargin);
        Vector2 bottomRight = canvasToMask * new Vector2(
            _splitCanvasX,
            viewportSize.Y + MaskMargin);
        _mask.SetMaskRect(new Rect2(
            new Vector2(Math.Min(topLeft.X, bottomRight.X), Math.Min(topLeft.Y, bottomRight.Y)),
            new Vector2(Math.Abs(bottomRight.X - topLeft.X), Math.Abs(bottomRight.Y - topLeft.Y))));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0
            || !GodotObject.IsInstanceValid(_mask))
        {
            return;
        }

        _mask.QueueFreeSafely();
    }

    internal static int ResolveEffectiveZ(CanvasItem item)
    {
        int z = item.ZIndex;
        CanvasItem current = item;
        while (current.ZAsRelative && current.GetParent() is CanvasItem parent)
        {
            z = Math.Clamp(z + parent.ZIndex, -MaximumCanvasZIndex, MaximumCanvasZIndex);
            current = parent;
        }

        return z;
    }

    private static void PrepareVisualClone(Node node)
    {
        node.SetProcess(false);
        node.SetPhysicsProcess(false);
        node.SetProcessInput(false);
        node.SetProcessUnhandledInput(false);
        node.SetProcessUnhandledKeyInput(false);
        if (node is Control control)
        {
            control.Visible = false;
        }

        foreach (Node child in node.GetChildren())
        {
            PrepareVisualClone(child);
        }
    }

    private static void FreezeSpineAnimations(Node visual)
    {
        if (visual.GetClass() == "SpineSprite")
        {
            var sprite = new MegaSprite(Variant.CreateFrom(visual));
            MegaAnimationState? animation = sprite.TryGetAnimationState();
            using IDisposable animationLease = GameCompatibility.NativeHandles.Lease(animation);
            animation?.SetTimeScale(0f);
        }

        foreach (Node child in visual.GetChildren())
        {
            FreezeSpineAnimations(child);
        }
    }

    private sealed partial class ScreenHalfMask : Node2D
    {
        private Rect2 _maskRect;

        internal void SetMaskRect(Rect2 maskRect)
        {
            _maskRect = maskRect;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_maskRect.Size.X > 0f && _maskRect.Size.Y > 0f)
            {
                DrawRect(_maskRect, Colors.White);
            }
        }
    }
}
