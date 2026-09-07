using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class PlaceholderBlueDefense02 : RedesignV1UncommonCard
{
    public PlaceholderBlueDefense02() : base(nameof(PlaceholderBlueDefense02), "BlockCard", 1, CardType.Skill, TargetType.Self) { }
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(11, ValueProp.Move)];
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(4);
}
