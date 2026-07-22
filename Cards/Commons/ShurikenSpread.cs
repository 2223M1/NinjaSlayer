using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public sealed class ShurikenSpread : NinjaSlayerCardTemplate
{
    private static readonly NinjaSlayerCardSpec CardSpec = new(nameof(ShurikenSpread), 1, CardType.Skill, CardRarity.Common, TargetType.Self, true);


    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new ShurikenVar(2)
    ];

    public ShurikenSpread() : base(CardSpec) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await NinjaSlayerActions.AddGeneratedShuriken(choiceContext, Owner, DynamicVars.Shuriken().IntValue, PileType.Hand, IsUpgraded);
    }

    protected override void OnUpgrade() { }
}
