using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Content;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class BlackFlameRecovery : RedesignV1UncommonCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        HoverTipFactory.FromCardWithCardHoverTips<BlackFlameRedesignV1>();

    public BlackFlameRecovery() : base(nameof(BlackFlameRecovery), "BurningCard", 2, CardType.Skill, TargetType.Self) { }
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(2), new DynamicVar("Heal", 2)];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var selected = (await CardSelectCmd.FromHand(choiceContext, Owner,
            new CardSelectorPrefs(SelectionScreenPrompt, 0, DynamicVars.Cards.IntValue), card => card != this, this)).ToArray();
        foreach (CardModel card in selected)
            await CardCmd.TransformTo<BlackFlameRedesignV1>(card);
        foreach (CardModel card in PileType.Hand.GetPile(Owner).Cards.Where(card => card.Type == CardType.Status).ToArray())
        {
            await CardCmd.Exhaust(choiceContext, card);
            if (card.Pile?.Type == PileType.Exhaust)
                await CreatureCmd.Heal(Owner.Creature, DynamicVars["Heal"].BaseValue);
        }
    }
    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1);
}
