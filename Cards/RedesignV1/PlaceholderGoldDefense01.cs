using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class PlaceholderGoldDefense01 : RedesignV1RareCard
{
    public PlaceholderGoldDefense01() : base(nameof(PlaceholderGoldDefense01), nameof(PlaceholderGoldDefense01), 1, CardType.Skill, TargetType.Self) { }
    public override bool GainsBlock => true;
    private bool ExhaustedTeaThisTurn => CombatState != null && CombatManager.Instance.History.Entries
        .OfType<CardExhaustedEntry>().Any(e => e.HappenedThisTurn(CombatState)
            && e.Card.Owner == Owner && e.Card is ChadoEnergyRedesignV1);
    protected override bool ShouldGlowGoldInternal => ExhaustedTeaThisTurn;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(5, ValueProp.Move), new KarateVar(3)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<KaratePower>(), .. HoverTipFactory.FromCardWithCardHoverTips<ChadoEnergyRedesignV1>()];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        bool gainKarate = ExhaustedTeaThisTurn;
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        if (gainKarate)
            await PowerCmd.Apply<KaratePower>(choiceContext, Owner.Creature, DynamicVars.Karate().BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(2);
        DynamicVars.Karate().UpgradeValueBy(1);
    }
}
