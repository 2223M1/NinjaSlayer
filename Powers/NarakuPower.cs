using System.Collections.Generic;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Nodes;
using STS2RitsuLib.Interop.AutoRegistration;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class NarakuPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new HpLossVar(4)
    ];

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        NarakuVisualOverlay.Sync(Owner);
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.PangbaiScaryEvent);
        NinjaSlayerCombatVfx.PlayBurnStatusFeedback([Owner]);
        return Task.CompletedTask;
    }

    public override Task AfterRemoved(Creature oldOwner)
    {
        NarakuVisualOverlay.Sync(oldOwner);
        return Task.CompletedTask;
    }

    public override async Task BeforeCardPlayed(CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner == Owner.Player && cardPlay.Card.Type == CardType.Attack)
        {
            await Content.NinjaSlayerActions.AddGeneratedCard<BurningCard>(Owner.Player, PileType.Discard);
        }
    }

    public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
    {
        if (dealer != Owner || !props.IsPoweredAttack())
        {
            return;
        }

        IReadOnlyList<Creature> enemies = CombatState.HittableEnemies;
        if (enemies.Count == 0)
        {
            return;
        }

        NinjaSlayerCombatVfx.PlayBurnStatusFeedback(enemies);

        await CreatureCmd.Damage(
            choiceContext,
            enemies,
            DynamicVars.HpLoss.BaseValue,
            ValueProp.Unblockable | ValueProp.Unpowered,
            Owner,
            cardSource,
            null);
    }
}
