using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.Code.ExternalAnimations;

// The presentation session owns time/pause/cancellation; this owns only the body pose.
internal sealed class GreetingBow : IDisposable
{
    internal const float Duration = 1f;
    internal static float Angle(float elapsed) => Mathf.DegToRad(18f) *
        (elapsed < .2f ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp(elapsed / .2f, 0f, 1f))
        : elapsed < .7f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp((elapsed - .7f) / .3f, 0f, 1f)));

    private readonly Creature _creature;
    private readonly NinjaSlayerAimPose? _aim;
    private readonly NinjaSlayerAimPose.VisualMotion? _motion;
    private readonly Node2D? _anchor;
    private readonly Transform2D _baseline;
    private readonly Vector2 _pivot;
    private readonly float _facing;
    private bool _disposed;
    private readonly long? _generation;

    internal GreetingBow(Creature creature)
    {
        _creature = creature;
        NinjaSlayerRapidAnimationCoordinator.CancelAndRestore(creature);
        NCreature node = creature.GetCreatureNode()
            ?? throw new InvalidOperationException("Greeting requires a creature node.");
        _aim = NinjaSlayerAimPose.Get(creature);
        if (_aim != null)
        {
            _motion = _aim.BeginVisualMotion(NinjaSlayerAimPose.MotionKind.Offset, Duration)
                ?? throw new InvalidOperationException("Greeting cannot acquire the player pose.");
            _motion.Paused = true;
            _facing = _motion.Facing;
        }
        else
        {
            _anchor = NinjaSlayerVisualRig.GetAirborneAnchor(node.Visuals)
                ?? throw new InvalidOperationException("Greeting requires the monster body anchor.");
            _baseline = _anchor.Transform;
            _pivot = _anchor.GetParent<CanvasItem>().GetGlobalTransform().AffineInverse()
                * node.Visuals.VfxSpawnPosition.GlobalPosition;
            bool left = creature.Monster is ForestSawatariMonster
                ? !((Sprite2D)node.Body).FlipH : node.Body.Transform.Determinant() < 0f;
            _facing = left ? 1f : -1f;
        }
        _generation = NinjaSlayerRapidAnimationCoordinator.RegisterReturnTail(creature, null, Dispose);
    }

    internal void Apply(float elapsed)
    {
        if (_disposed) return;
        if (_creature.IsDead || _creature.CombatState == null) { Dispose(); return; }
        float angle = Angle(elapsed) * _facing;
        if (_motion != null)
        {
            _motion.Radians = angle;
            if (GodotObject.IsInstanceValid(_aim)) _aim!.SyncNow();
        }
        else if (GodotObject.IsInstanceValid(_anchor))
        {
            Transform2D rotation = new(angle, Vector2.Zero);
            rotation.Origin = _pivot - rotation.BasisXform(_pivot);
            _anchor!.Transform = rotation * _baseline;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _motion?.Dispose();
        if (GodotObject.IsInstanceValid(_aim)) _aim!.SyncNow();
        if (GodotObject.IsInstanceValid(_anchor)) _anchor!.Transform = _baseline;
        if (_generation is { } generation)
            NinjaSlayerRapidAnimationCoordinator.CompleteVisualTail(_creature, generation);
    }
}
