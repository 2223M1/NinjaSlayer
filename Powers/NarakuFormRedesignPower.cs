using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class NarakuFormRedesignPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named("NarakuPower");
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
    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner.Creature != Owner || cardPlay.Card.Type != CardType.Attack)
            return Task.CompletedTask;
        Flash();
        return BlackFlameRedesignV1.DamageEnemies(choiceContext, Owner.Player!,
            RedesignV1Rules.BlackFlameDamage * Amount, null);
    }
}
