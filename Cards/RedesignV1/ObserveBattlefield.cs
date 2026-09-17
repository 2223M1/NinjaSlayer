using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class ObserveBattlefield : RedesignV1UncommonCard
{
    protected override bool ShouldGlowGoldInternal => CombatState != null && IsPlayable;

    public ObserveBattlefield() : base(nameof(ObserveBattlefield), nameof(ObserveBattlefield), 0, CardType.Skill, TargetType.AnyEnemy) { }
    public override bool GainsBlock => true;
    protected override bool IsPlayable => PileType.Hand.GetPile(Owner).Cards.OfType<ChadoEnergyRedesignV1>().Any();
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(6, ValueProp.Move), new PowerVar<WeakPower>(2)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<WeakPower>(), .. HoverTipFactory.FromCardWithCardHoverTips<ChadoEnergyRedesignV1>()];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var selected = (await CardSelectCmd.FromHand(choiceContext, Owner,
            new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 1),
            card => card is ChadoEnergyRedesignV1, this)).FirstOrDefault();
        if (selected is null) return;
        await CardCmd.Exhaust(choiceContext, selected);
        if (selected.Pile?.Type != PileType.Exhaust) return;
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await PowerCmd.Apply<WeakPower>(choiceContext, cardPlay.Target!, DynamicVars[nameof(WeakPower)].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(1);
        DynamicVars[nameof(WeakPower)].UpgradeValueBy(1);
    }
}
