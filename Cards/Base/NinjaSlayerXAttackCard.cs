using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

/// <summary>
/// Base for NinjaSlayer X-cost attacks with spin combo animation, SFX, and lunge movement.
/// Subclasses implement per-hit logic in <see cref="ExecuteXHit"/>.
/// </summary>
public abstract class NinjaSlayerXAttackCard : NinjaSlayerCardTemplate
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

    protected virtual float XAttackHitDelay => Owner.Character.AttackAnimDelay;

    protected virtual float XAttackAudioHitDuration => Owner.Character.AttackAnimDelay;

    protected virtual int ResolveXHitCount()
    {
        return Math.Max(0, ResolveEnergyXValue());
    }

    public int GetPreviewHitCount()
    {
        int xValue = EnergyCost.GetAmountToSpend();
        if (Pile != null && CombatState is { } combatState)
        {
            xValue = Hook.ModifyXValue(combatState, this, xValue);
        }

        return Math.Max(0, ModifyPreviewHitCount(xValue));
    }

    protected virtual int ModifyPreviewHitCount(int xValue) => xValue;

    protected sealed override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int hits = ResolveXHitCount();
        if (hits == 0)
        {
            return;
        }

        FinisherAttackSpec finisherSpec = FinisherAttackSpec.FromCard(
            this,
            cardPlay,
            hitCountOverride: hits);
        await NinjaSlayerFinisherCinematic.ExecuteSequenceWithFinisher(
            choiceContext,
            finisherSpec,
            () => NinjaSlayerXAttackSequence.Run(
                Owner.Creature,
                hits,
                XAttackHitDelay,
                XAttackAudioHitDuration,
                hitIndex => RunXHit(choiceContext, cardPlay, hitIndex, hits)));
    }

    private async Task<bool> RunXHit(PlayerChoiceContext choiceContext, CardPlay cardPlay, int hitIndex, int totalHits)
    {
        await OnBeforeXHit(choiceContext, cardPlay, hitIndex, totalHits);
        return await ExecuteXHit(choiceContext, cardPlay, hitIndex, totalHits);
    }

    protected virtual Task OnBeforeXHit(
        PlayerChoiceContext choiceContext,
        CardPlay cardPlay,
        int hitIndex,
        int totalHits) => Task.CompletedTask;

    protected abstract Task<bool> ExecuteXHit(
        PlayerChoiceContext choiceContext,
        CardPlay cardPlay,
        int hitIndex,
        int totalHits);
}
