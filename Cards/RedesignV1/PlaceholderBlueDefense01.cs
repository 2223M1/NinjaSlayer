using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class PlaceholderBlueDefense01 : RedesignV1UncommonCard
{
    public PlaceholderBlueDefense01() : base(nameof(PlaceholderBlueDefense01), nameof(PlaceholderBlueDefense01), 1, CardType.Skill, TargetType.Self) { }
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(5, ValueProp.Move), new PowerVar<ThornsPower>(3)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<ThornsPower>()];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await PowerCmd.Apply<ThornsPower>(choiceContext, Owner.Creature, DynamicVars[nameof(ThornsPower)].BaseValue, Owner.Creature, this);
        CaltropsDurationPower duration = (await PowerCmd.Apply<CaltropsDurationPower>(
            choiceContext, Owner.Creature, 3, Owner.Creature, this))!;
        duration.ThornsAmount = DynamicVars[nameof(ThornsPower)].IntValue;
    }
    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(3);
        DynamicVars[nameof(ThornsPower)].UpgradeValueBy(1);
    }
}
