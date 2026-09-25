using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Combat;
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
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner.Creature != Owner || cardPlay.Card.Type != CardType.Attack)
            return;
        int flames = Amount;
        for (int index = 0; index < flames; index++)
        {
            if (CombatManager.Instance.IsOverOrEnding || !Owner.IsAlive) break;
            var targets = Owner.CombatState!.HittableEnemies;
            if (targets.Count == 0) break;
            Flash();
            NinjaSlayerCombatVfx.PlayBurnStatusFeedback(targets);
            await BlackFlameRedesignV1.DamageEnemies(choiceContext, Owner.Player!,
                RedesignV1Rules.BlackFlameDamage, null, targets);
        }
    }
}
