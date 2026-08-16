using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class ShurikenStockRedesignV1 : NinjaSlayerRedesignCardTemplate
{
    private static readonly NinjaSlayerCardSpec Spec = new(
        nameof(ShurikenStockRedesignV1), 1, CardType.Skill, CardRarity.Common, TargetType.Self, true);

    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(7, ValueProp.Move), new DynamicVar("Stock", 2)];

    public ShurikenStockRedesignV1() : base(Spec, nameof(ShurikenStock)) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await PowerCmd.Apply<ShurikenStockPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars["Stock"].BaseValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade() { DynamicVars.Block.UpgradeValueBy(1); DynamicVars["Stock"].UpgradeValueBy(1); }
}
