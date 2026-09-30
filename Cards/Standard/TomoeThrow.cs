using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.ExternalAnimations;

namespace NinjaSlayer.Cards.Standard;

public sealed class TomoeThrow : NinjaSlayerUncommonCard
{
    protected override bool ShouldGlowGoldInternal => CombatState != null && IsPlayable;

    public TomoeThrow() : base(nameof(TomoeThrow), 0, CardType.Skill, TargetType.AnyEnemy) { }
    public override bool GainsBlock => true;
    protected override bool IsPlayable => PileType.Hand.GetPile(Owner).Cards.OfType<Chado>().Any();
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(6, ValueProp.Move), new PowerVar<WeakPower>(2)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<WeakPower>(), .. HoverTipFactory.FromCardWithCardHoverTips<Chado>()];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var selected = (await CardSelectCmd.FromHand(choiceContext, Owner,
            new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 1),
            card => card is Chado, this)).FirstOrDefault();
        if (selected is null) return;
        await CardCmd.Exhaust(choiceContext, selected);
        if (selected.Pile?.Type != PileType.Exhaust) return;
        await TomoeThrowAnimation.Play(Owner.Creature, cardPlay.Target!, async () =>
        {
            await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
            await PowerCmd.Apply<WeakPower>(choiceContext, cardPlay.Target!, DynamicVars[nameof(WeakPower)].BaseValue, Owner.Creature, this);
        });
    }
    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(1);
        DynamicVars[nameof(WeakPower)].UpgradeValueBy(1);
    }
}
