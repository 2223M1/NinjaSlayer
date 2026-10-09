using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.Standard;

public sealed class NinjaTaunt : NinjaSlayerCommonCard
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new BlockVar(15, ValueProp.Move), new KarateVar(4)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<KaratePower>()];

    public NinjaTaunt()
        : base(nameof(NinjaTaunt), 1, CardType.Skill, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await PowerCmd.Apply<KaratePower>(
            choiceContext,
            cardPlay.Target!,
            DynamicVars.Karate().BaseValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3);
}
