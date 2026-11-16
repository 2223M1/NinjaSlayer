using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Code.Nodes;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

public sealed class NarakuUnleashedRelic : NinjaSlayerRelicTemplate
{
    private bool _attackPriorityEnded;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => [new NarakuLifeVar(20)];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<NarakuLifePower>(),
            .. HoverTipFactory.FromCardWithCardHoverTips<BlackFlame>()];

    public override async Task BeforeCombatStart()
    {
        _attackPriorityEnded = false;
        Flash();
        NarakuVisualOverlay.Sync(Owner.Creature);
        NinjaSlayerCombatVfx.PlayBurnStatusFeedback([Owner.Creature]);
        await PowerCmd.Apply<NarakuLifePower>(new ThrowingPlayerChoiceContext(),
            Owner.Creature, DynamicVars.NarakuLife().BaseValue, Owner.Creature, null);
    }

    public override bool ShouldPlay(CardModel card, AutoPlayType autoPlayType) =>
        _attackPriorityEnded || card.Owner != Owner || autoPlayType != AutoPlayType.None
        || card.Type == CardType.Attack || card is Enthralled
        || !PileType.Hand.GetPile(Owner).Cards.Any(held => held.Type == CardType.Attack);

    public override Task AfterShuffle(PlayerChoiceContext choiceContext, Player shuffler)
    {
        // Match Stratagem: the initial combat shuffle does not invoke AfterShuffle.
        if (shuffler == Owner) _attackPriorityEnded = true;
        return Task.CompletedTask;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player != Owner) return;
        Flash();
        var card = Owner.Creature.CombatState!.CreateCard<BlackFlame>(Owner);
        await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, Owner);
    }
}
