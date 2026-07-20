using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class TornadoFistSpinAnimation
{
    public const string TriggerName = "TornadoFistSpin";
    public const string CueTriggerName = "TornadoFistCue";
    public const float TurnSeconds = 0.15f;

    private static readonly Dictionary<Creature, SpinState> ActiveSpins = [];
    private static bool _loggedMissingRig;

    public static async Task PlayTurn(Creature creature, float duration)
    {
        float resolvedDuration = duration > 0f ? duration : TurnSeconds;
        NCreature? creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        Node2D? anchor = NinjaSlayerVisualRig.GetAirborneAnchor(creatureNode?.Visuals);
        Node2D? focus = NinjaSlayerVisualRig.GetCinematicFocus(creatureNode?.Visuals);
        if (creatureNode == null || anchor == null || focus == null)
        {
            LogMissingRigOnce();
            await Cmd.Wait(resolvedDuration);
            return;
        }

        StopAndRestore(creature);
        creatureNode.SetAnimationTrigger(CueTriggerName);

        var state = new SpinState(anchor, focus);
        ActiveSpins[creature] = state;
        Tween tween = creatureNode.CreateTween();
        state.Tween = tween;
        tween.TweenMethod(
                Callable.From<float>(angle => state.ApplyAngle(angle)),
                0f,
                Mathf.Tau,
                resolvedDuration)
            .SetTrans(Tween.TransitionType.Linear);

        try
        {
            await creatureNode.ToSignal(tween, Tween.SignalName.Finished);
        }
        finally
        {
            if (ActiveSpins.TryGetValue(creature, out SpinState? active)
                && ReferenceEquals(active, state))
            {
                ActiveSpins.Remove(creature);
                state.Restore();
            }
        }
    }

    public static void StopAndRestore(Creature creature)
    {
        if (ActiveSpins.Remove(creature, out SpinState? state))
        {
            state.StopAndRestore();
        }
    }

    private static void LogMissingRigOnce()
    {
        if (_loggedMissingRig)
        {
            return;
        }

        _loggedMissingRig = true;
        Entry.Logger.Warn("Tornado Fist spin skipped because AirborneAnchor or CinematicFocus was unavailable.");
    }

    private sealed class SpinState
    {
        private readonly Node2D _anchor;
        private readonly Vector2 _position;
        private readonly float _rotation;
        private readonly Vector2 _fixedFocusPosition;
        private readonly Vector2 _focusLocalPosition;

        public SpinState(Node2D anchor, Node2D focus)
        {
            _anchor = anchor;
            _position = anchor.Position;
            _rotation = anchor.Rotation;
            _focusLocalPosition = focus.Position;
            _fixedFocusPosition = _position + anchor.Transform.BasisXform(_focusLocalPosition);
        }

        public Tween? Tween { get; set; }

        public void ApplyAngle(float angle)
        {
            if (!GodotObject.IsInstanceValid(_anchor))
            {
                return;
            }

            _anchor.Rotation = _rotation + angle;
            Vector2 rotatedFocusOffset = _anchor.Transform.BasisXform(_focusLocalPosition);
            _anchor.Position = _fixedFocusPosition - rotatedFocusOffset;
        }

        public void StopAndRestore()
        {
            if (Tween is { } tween && GodotObject.IsInstanceValid(tween) && tween.IsValid())
            {
                tween.Kill();
            }

            Restore();
        }

        public void Restore()
        {
            if (!GodotObject.IsInstanceValid(_anchor))
            {
                return;
            }

            _anchor.Position = _position;
            _anchor.Rotation = _rotation;
        }
    }
}
