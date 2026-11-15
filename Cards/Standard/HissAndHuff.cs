using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace NinjaSlayer.Cards.Standard;

public sealed class HissAndHuff : NinjaSlayerUncommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override bool IsPlayable => PileType.Hand.GetPile(Owner).Cards.OfType<Chado>().Any();
    protected override bool ShouldGlowGoldInternal => CombatState != null && IsPlayable;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<RegenPower>(4)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromCard<Chado>(), HoverTipFactory.FromPower<RegenPower>()];

    public HissAndHuff()
        : base(nameof(HissAndHuff), 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        CardModel? tea = (await CardSelectCmd.FromHand(
            choiceContext, Owner,
            new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 1),
            card => card is Chado, this)).FirstOrDefault();
        if (tea == null)
        {
            return;
        }

        await CardCmd.Exhaust(choiceContext, tea);
        await PowerCmd.Apply<RegenPower>(
            choiceContext, Owner.Creature, DynamicVars[nameof(RegenPower)].BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade() => DynamicVars[nameof(RegenPower)].UpgradeValueBy(1);
}
