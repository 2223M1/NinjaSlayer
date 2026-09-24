using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Nodes;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

public sealed class NarakuWithinRelic : NinjaSlayerRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        HoverTipFactory.FromCardWithCardHoverTips<BlackFlameRedesignV1>();

    public override Task BeforeCombatStart()
    {
        Flash();
        NarakuVisualOverlay.Sync(Owner.Creature);
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.PangbaiScaryEvent);
        NinjaSlayerCombatVfx.PlayBurnStatusFeedback([Owner.Creature]);
        return Task.CompletedTask;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player != Owner) return;
        Flash();
        var card = Owner.Creature.CombatState!.CreateCard<BlackFlameRedesignV1>(Owner);
        await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, Owner);
    }
}
