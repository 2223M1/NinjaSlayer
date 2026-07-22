using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Afflictions;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public sealed class ReadyBlade : NinjaSlayerCardTemplate
{
    private static readonly NinjaSlayerCardSpec CardSpec = new(nameof(ReadyBlade), 1, CardType.Skill, CardRarity.Uncommon, TargetType.Self, true);


    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new CardsVar(1),
        new ShurikenVar(3)
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        .. HoverTipFactory.FromAffliction<PreparedAffliction>()
    ];

    public ReadyBlade() : base(CardSpec) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await NinjaSlayerActions.AddGeneratedShuriken(
            choiceContext,
            Owner,
            DynamicVars.Shuriken().IntValue,
            PileType.Draw,
            position: CardPilePosition.Top,
            prepare: true);
        await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.BaseValue, Owner);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Shuriken().UpgradeValueBy(1);
    }
}
