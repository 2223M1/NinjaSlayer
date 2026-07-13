using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

/// <summary>
/// X-cost combo lunge: first hit moves out, middle hits hold, EndCombo returns.
/// Uses slow-attack easing curves without triggering the SlowAttack anim route.
/// </summary>
public static class XAttackComboMovement
{
    private const float ReturnDuration = 0.25f;

    private static readonly Dictionary<Creature, Vector2> ComboBasePositions = new();

    public static void BeginCombo(Creature creature)
    {
        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null)
        {
            return;
        }

        ComboBasePositions[creature] = creatureNode.Position;
    }

    public static async Task PlayHitMovement(Creature creature, float waitTime)
    {
        if (XAttackComboContext.CurrentHitIndex != 0)
        {
            return;
        }

        if (!ComboBasePositions.TryGetValue(creature, out Vector2 basePos))
        {
            return;
        }

        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null)
        {
            return;
        }

        float direction = creature.IsPlayer ? 1f : -1f;
        if (waitTime <= 0f)
        {
            creatureNode.Position = new Vector2(
                basePos.X + NinjaSlayerCombatVisuals.AttackLungeDistance * direction,
                basePos.Y);
            return;
        }

        var tween = creatureNode.CreateTween();
        tween.TweenMethod(
            Callable.From<float>(progress =>
            {
                float easedT = Mathf.Pow(progress, 10f);
                float xOffset = Mathf.Lerp(0f, NinjaSlayerCombatVisuals.AttackLungeDistance, easedT);
                creatureNode.Position = new Vector2(basePos.X + xOffset * direction, basePos.Y);
            }),
            0f,
            1f,
            waitTime
        ).SetTrans(Tween.TransitionType.Linear);

        await creatureNode.ToSignal(tween, Tween.SignalName.Finished);
    }

    public static async Task EndCombo(Creature creature)
    {
        if (!ComboBasePositions.Remove(creature, out Vector2 basePos))
        {
            return;
        }

        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null)
        {
            return;
        }

        float direction = creature.IsPlayer ? 1f : -1f;
        var tween = creatureNode.CreateTween();
        tween.TweenMethod(
            Callable.From<float>(progress =>
            {
                float fadeT = 1f - progress;
                float easedT = fadeT * fadeT * (3f - 2f * fadeT);
                float xOffset = Mathf.Lerp(0f, NinjaSlayerCombatVisuals.AttackLungeDistance, easedT);
                creatureNode.Position = new Vector2(basePos.X + xOffset * direction, basePos.Y);
            }),
            0f,
            1f,
            ReturnDuration
        ).SetTrans(Tween.TransitionType.Linear);

        await creatureNode.ToSignal(tween, Tween.SignalName.Finished);
        creatureNode.Position = basePos;
    }
}
