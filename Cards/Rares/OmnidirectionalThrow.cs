using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public sealed class OmnidirectionalThrow : NinjaSlayerCardTemplate
{
    private static readonly NinjaSlayerCardSpec CardSpec = new(nameof(OmnidirectionalThrow), 1, CardType.Skill, CardRarity.Rare, TargetType.Self, true, "ShurikenSpread");


    // ponytail: reuse shuriken art until this card gets dedicated art.

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new ShurikenVar(3)
    ];

    public OmnidirectionalThrow() : base(CardSpec) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await NinjaSlayerActions.AddGeneratedShuriken(choiceContext, Owner, DynamicVars.Shuriken().IntValue, PileType.Hand);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Shuriken().UpgradeValueBy(1);
    }
}
