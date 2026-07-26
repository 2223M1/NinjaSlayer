using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace NinjaSlayer.Code.Nodes;

public partial class NCreatureTopVfxFollower : Control
{
    private const string NodeName = "CreatureTopVfxFollower";
    private static readonly string[] VisualNodeNames = ["Image1", "Image2", "Image3"];

    private NRelicFlashVfx? _vfx;
    private Creature? _target;
    private Vector2 _originAnchor;

    internal static void Attach(NRelicFlashVfx vfx, Creature target)
    {
        if (vfx.GetNodeOrNull<NCreatureTopVfxFollower>(NodeName) != null)
        {
            return;
        }

        NCreatureTopVfxFollower follower = new()
        {
            Name = NodeName,
            MouseFilter = MouseFilterEnum.Ignore,
            ProcessPriority = 1000,
            _vfx = vfx,
            _target = target
        };
        follower.SetProcess(false);
        vfx.AddChild(follower);
    }

    public override void _Ready()
    {
        if (_vfx == null || !GodotObject.IsInstanceValid(_vfx))
        {
            return;
        }

        _vfx.Connect(Node.SignalName.Ready, Callable.From(StartTracking));
    }

    public override void _Process(double delta)
    {
        UpdatePosition();
    }

    private void StartTracking()
    {
        NCreature? targetNode = GetTargetNode();
        if (targetNode == null || _vfx == null)
        {
            return;
        }

        _originAnchor = targetNode.GetTopOfHitbox();
        Callable.From(AttachVisualsAndTrack).CallDeferred();
    }

    private void AttachVisualsAndTrack()
    {
        if (_vfx == null || !GodotObject.IsInstanceValid(_vfx))
        {
            return;
        }

        Position = Vector2.Zero;
        // Offset the icon layers so the vanilla root-node rise tween remains authoritative.
        foreach (string nodeName in VisualNodeNames)
        {
            Control? visual = _vfx.GetNodeOrNull<Control>(nodeName);
            visual?.Reparent(this, true);
        }

        SetProcess(true);
        UpdatePosition();
    }

    private void UpdatePosition()
    {
        NCreature? targetNode = GetTargetNode();
        if (targetNode == null
            || _vfx == null
            || !GodotObject.IsInstanceValid(_vfx))
        {
            SetProcess(false);
            return;
        }

        Transform2D inverseRootTransform = _vfx.GetGlobalTransformWithCanvas().AffineInverse();
        Position = inverseRootTransform * targetNode.GetTopOfHitbox()
            - inverseRootTransform * _originAnchor;
    }

    private NCreature? GetTargetNode()
    {
        NCreature? targetNode = _target?.GetCreatureNode();
        return targetNode != null && GodotObject.IsInstanceValid(targetNode)
            ? targetNode
            : null;
    }
}
