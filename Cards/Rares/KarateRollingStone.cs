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

public sealed class KarateRollingStone : NinjaSlayerCardTemplate
{
    private static readonly NinjaSlayerCardSpec CardSpec = new(nameof(KarateRollingStone), 2, CardType.Power, CardRarity.Rare, TargetType.Self, true, "KataDrill");


    // ponytail: reuse karate art until this card gets dedicated art.

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new KarateVar(4)
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromPower<KaratePower>()
    ];

    public KarateRollingStone() : base(CardSpec) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<KarateRollingStonePower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.Karate().BaseValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Karate().UpgradeValueBy(1);
    }
}
