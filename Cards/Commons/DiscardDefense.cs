using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public sealed class DiscardDefense : NinjaSlayerCardTemplate
{
    public override CardAssetProfile AssetProfile => NinjaSlayerCardAssets.Named("BlockCard");
    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new BlockVar(6, ValueProp.Move),
        new PowerVar<DiscardDefensePower>(3)
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromPower<DiscardDefensePower>()
    ];

    public DiscardDefense() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self, true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await PowerCmd.Apply<DiscardDefensePower>(
            choiceContext,
            Owner.Creature,
            DynamicVars["DiscardDefensePower"].BaseValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars["DiscardDefensePower"].UpgradeValueBy(2);
    }
}
