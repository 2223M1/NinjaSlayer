using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Nodes;
using STS2RitsuLib.Interop.AutoRegistration;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

[RegisterPower]
public sealed class NarakuPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;

    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.For(GetType());

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        NarakuVisualOverlay.Sync(Owner);
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.PangbaiScaryEvent);
        return Task.CompletedTask;
    }

    public override Task AfterRemoved(Creature oldOwner)
    {
        NarakuVisualOverlay.Sync(oldOwner);
        return Task.CompletedTask;
    }

    public override async Task BeforeCardPlayed(CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner == Owner.Player)
        {
            await Content.NinjaSlayerActions.AddGeneratedCard<BurningCard>(Owner.Player, PileType.Discard);
        }
    }

    public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
    {
        if (dealer != Owner || cardSource?.Type != CardType.Attack || props.HasFlag(ValueProp.Unblockable) || result.BlockedDamage <= 0)
        {
            return;
        }

        await CreatureCmd.Damage(choiceContext, target, result.BlockedDamage, ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, dealer, cardSource);
    }
}
