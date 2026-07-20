using MegaCrit.Sts2.Core.Entities.Creatures;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class TornadoFistSpinAnimation
{
    public const string TriggerName = "TornadoFistSpin";
    public const string CueTriggerName = "TornadoFistCue";
    public const float TurnSeconds = 0.15f;

    public static async Task PlayTurn(Creature creature, float duration)
    {
        float resolvedDuration = duration > 0f ? duration : TurnSeconds;
        if (creature.GetCreatureNode() is { } creatureNode)
        {
            creatureNode.SetAnimationTrigger(CueTriggerName);
        }

        await SoarSpinAnimation.PlayExactTurn(creature, resolvedDuration);
    }
}
