using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public sealed class TeaShield : NinjaSlayerCardTemplate
{
    private static readonly NinjaSlayerCardSpec CardSpec = new(nameof(TeaShield), 1, CardType.Skill, CardRarity.Rare, TargetType.Self, true, "BlockCard");


    public override bool GainsBlock => true;

    // ponytail: reuse tea art until this card gets dedicated art.

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new CalculationBaseVar(7),
        new CalculationExtraVar(2),
        new CalculatedBlockVar(ValueProp.Move).WithMultiplier(NinjaSlayerActions.ChadoInExhaustPileMultiplier)
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromCard<ChadoCard>()
    ];

    public TeaShield() : base(CardSpec) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await CreatureCmd.GainBlock(
            Owner.Creature,
            DynamicVars.CalculatedBlock.Calculate(Owner.Creature),
            DynamicVars.CalculatedBlock.Props,
            cardPlay);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.CalculationExtra.UpgradeValueBy(1);
    }
}
