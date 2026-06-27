using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

/// <summary>
/// Base for NinjaSlayer X-cost attacks with spin combo animation, SFX, and lunge movement.
/// Subclasses implement per-hit logic in <see cref="ExecuteXHit"/>.
/// </summary>
[RegisterCard(typeof(NinjaSlayerCardPool), Inherit = true)]
public abstract class NinjaSlayerXAttackCard : ModCardTemplate
{
    protected NinjaSlayerXAttackCard(
        int energyCost,
        CardType type,
        CardRarity rarity,
        TargetType targetType,
        bool shouldShowInCardLibrary)
        : base(energyCost, type, rarity, targetType, shouldShowInCardLibrary)
    {
    }

    protected override bool HasEnergyCostX => true;

    protected virtual string AttackerAnimTrigger => "XAttack";

    protected virtual int ResolveXHitCount()
    {
        return Math.Max(0, ResolveEnergyXValue());
    }

    protected sealed override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int hits = ResolveXHitCount();
        if (hits == 0)
        {
            return;
        }

        await NinjaSlayerXAttackSequence.Run(
            Owner.Creature,
            hits,
            Owner.Character.AttackAnimDelay,
            hitIndex => RunXHit(choiceContext, cardPlay, hitIndex, hits));
    }

    private async Task RunXHit(PlayerChoiceContext choiceContext, CardPlay cardPlay, int hitIndex, int totalHits)
    {
        await OnBeforeXHit(choiceContext, cardPlay, hitIndex, totalHits);
        await ExecuteXHit(choiceContext, cardPlay, hitIndex, totalHits);
    }

    protected virtual Task OnBeforeXHit(
        PlayerChoiceContext choiceContext,
        CardPlay cardPlay,
        int hitIndex,
        int totalHits) => Task.CompletedTask;

    protected abstract Task ExecuteXHit(
        PlayerChoiceContext choiceContext,
        CardPlay cardPlay,
        int hitIndex,
        int totalHits);
}
