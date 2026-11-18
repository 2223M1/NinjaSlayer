#if NINJASLAYER_CHANNEL_PREVIEW
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.FoolArrows;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace NinjaSlayer.Code.ExternalAnimations;

// The native VFX must stay in its own tree: its exit removes the rear half
// and unregisters the slot. Only lend the two draw transforms to the stab.
internal sealed class DarkStrikeImpaledArrow : IDisposable
{
    private static readonly AccessTools.FieldRef<NImpaledArrowVfx, ArrowSlot> Slot =
        AccessTools.FieldRefAccess<NImpaledArrowVfx, ArrowSlot>("_arrowSlot");
    private static readonly AccessTools.FieldRef<NImpaledArrowVfx, Node2D> Rear =
        AccessTools.FieldRefAccess<NImpaledArrowVfx, Node2D>("_backArrowNode");
    private readonly ArrowSlot _slot;
    private readonly Piece[] _pieces;
    private bool _released;

    internal static DarkStrikeImpaledArrow[] Capture(Node visuals, Sprite2D source) =>
        visuals.GetChildren().OfType<NImpaledArrowVfx>()
            .Where(arrow => !arrow.IsQueuedForDeletion() && Slot(arrow).Status == ArrowStatus.Away)
            .Select(arrow => new DarkStrikeImpaledArrow(arrow, source)).ToArray();

    private DarkStrikeImpaledArrow(NImpaledArrowVfx arrow, Sprite2D source)
    {
        _slot = Slot(arrow);
        _pieces = [new(arrow, source, _slot, 1), new(Rear(arrow), source, _slot, -1)];
        _slot.ArrowStatusUpdated += OnSlotChanged;
    }

    internal void ShowOn(Sprite2D body, int bodyZ)
    {
        if (_released) return;
        foreach (var piece in _pieces) piece.Follow(body, bodyZ);
    }

    private void OnSlotChanged()
    {
        if (_slot.Status != ArrowStatus.Away) Dispose();
    }

    public void Dispose()
    {
        if (_released) return;
        _released = true;
        _slot.ArrowStatusUpdated -= OnSlotChanged;
        foreach (var piece in _pieces) piece.Dispose();
    }

    private sealed class Piece(Node2D original, Sprite2D source, ArrowSlot slot, int depth) : IDisposable
    {
        private readonly Transform2D _home = original.Transform;
        private readonly Transform2D _onBody = source.GlobalTransform.AffineInverse() * original.GlobalTransform;
        private readonly bool _relative = original.ZAsRelative;
        private readonly int _z = original.ZIndex;
        private readonly RemoteTransform2D _follow = new();

        internal void Follow(Sprite2D body, int bodyZ)
        {
            if (!GodotObject.IsInstanceValid(original) || !original.IsInsideTree()) return;
            if (_follow.GetParent() != body)
            {
                _follow.RemotePath = "";
                if (_follow.GetParent() == null) body.AddChild(_follow);
                else _follow.Reparent(body, keepGlobalTransform: false);
                _follow.Transform = _onBody;
                _follow.RemotePath = _follow.GetPathTo(original);
                original.GlobalTransform = _follow.GlobalTransform;
            }
            original.ZAsRelative = false;
            original.ZIndex = Math.Clamp(bodyZ + depth, -4095, 4095);
        }

        public void Dispose()
        {
            if (GodotObject.IsInstanceValid(_follow))
            {
                _follow.RemotePath = "";
                _follow.QueueFree();
            }
            if (!GodotObject.IsInstanceValid(original)) return;
            if (slot.Status == ArrowStatus.Away) original.Transform = _home;
            original.ZAsRelative = _relative;
            original.ZIndex = _z;
        }
    }
}
#endif
