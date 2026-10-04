using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Content;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.Standard;

public sealed class GatherKi : NinjaSlayerUncommonCard
{
    protected override bool ShouldGlowGoldInternal => CombatState != null && IsPlayable;

    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<NinjaSlayer.Powers.KaratePower>(), .. HoverTipFactory.FromCardWithCardHoverTips<Chado>()];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain];
    public GatherKi() : base(nameof(GatherKi), 1, CardType.Skill, TargetType.Self) { }
    protected override bool IsPlayable => PileType.Hand.GetPile(Owner).Cards.OfType<Chado>().Any();
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var selected = (await CardSelectCmd.FromHand(choiceContext, Owner,
            new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 1),
            card => card is Chado, this)).FirstOrDefault();
        if (selected is null) return;
        decimal karate = selected.DynamicVars.Energy.BaseValue * 2;
        await CardCmd.Exhaust(choiceContext, selected);
        if (selected.Pile?.Type == PileType.Exhaust)
            await PowerCmd.Apply<KaratePower>(choiceContext, Owner.Creature, karate, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}
