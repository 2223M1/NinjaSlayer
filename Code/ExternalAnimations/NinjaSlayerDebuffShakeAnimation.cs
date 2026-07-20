using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class NinjaSlayerDebuffShakeAnimation
{
    private sealed class ActiveShake
    {
        public required Tween Tween { get; init; }
        public required Node2D Visuals { get; init; }
        public required Vector2 Origin { get; init; }
        public required Action OnTreeExiting { get; init; }
    }

    private const float Duration = 1f;
    private const float Distance = 10f;
    private static readonly Dictionary<ulong, ActiveShake> ActiveShakes = [];

    public static void Play(NCreature creatureNode)
    {
        Node2D visuals = creatureNode.Visuals;
        ulong id = visuals.GetInstanceId();
        if (!creatureNode.IsInsideTree()
            || creatureNode.Entity.IsDead
            || StaggerAnimation.IsActive(creatureNode.Entity)
            || ActiveShakes.ContainsKey(id))
        {
            return;
        }

        Vector2 origin = visuals.Position;
        Tween tween = creatureNode.CreateTween();
        ActiveShake? active = null;
        Action onTreeExiting = () => Finish(id, active, restorePosition: false);
        active = new ActiveShake
        {
            Tween = tween,
            Visuals = visuals,
            Origin = origin,
            OnTreeExiting = onTreeExiting
        };
        ActiveShakes[id] = active;
        visuals.TreeExiting += onTreeExiting;

        tween.TweenMethod(
                Callable.From<float>(phase =>
                {
                    if (GodotObject.IsInstanceValid(visuals))
                    {
                        float offset = Distance * Mathf.Sin(phase * 4f) * Mathf.Sin(phase * 0.5f);
                        visuals.Position = origin + Vector2.Right * offset;
                    }
                }),
                0f,
                Mathf.Tau,
                Duration)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Cubic);
        tween.Finished += () => Finish(id, active, restorePosition: true);
    }

    private static void Finish(ulong id, ActiveShake? active, bool restorePosition)
    {
        if (active == null
            || !ActiveShakes.TryGetValue(id, out ActiveShake? current)
            || !ReferenceEquals(active, current))
        {
            return;
        }

        ActiveShakes.Remove(id);
        if (GodotObject.IsInstanceValid(active.Tween))
        {
            active.Tween.Kill();
        }

        if (!GodotObject.IsInstanceValid(active.Visuals))
        {
            return;
        }

        active.Visuals.TreeExiting -= active.OnTreeExiting;
        if (restorePosition)
        {
            active.Visuals.Position = active.Origin;
        }
    }
}
