using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public sealed class TeaDrinkingSword : NinjaSlayerCardTemplate
{
    private static readonly NinjaSlayerCardSpec CardSpec = new(nameof(TeaDrinkingSword), 2, CardType.Power, CardRarity.Rare, TargetType.Self, true, "ShurikenThrow");


    // ponytail: reuse shuriken-throw art until this card gets dedicated art.

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DynamicVar("ShurikenThreshold", 5)
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromCard<ChadoCard>(),
        HoverTipFactory.FromCard<ShurikenCard>()
    ];

    public TeaDrinkingSword() : base(CardSpec) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<TeaDrinkingSwordPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars["ShurikenThreshold"].IntValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars["ShurikenThreshold"].UpgradeValueBy(-1);
    }
}
