using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

[RegisterCard(typeof(TokenCardPool))]
public sealed class GiantShurikenCard : NinjaSlayerStandaloneCardTemplate
{
    private static readonly NinjaSlayerCardSpec CardSpec = new(
        nameof(GiantShurikenCard),
        0,
        CardType.Attack,
        CardRarity.Token,
        targetType,
        false,
        Tags: [NinjaSlayerCardTags.Shuriken]);

    private const TargetType targetType = TargetType.AnyEnemy;

    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;

    public override TargetType TargetType => Owner?.Creature.HasPower<HellTornadoPower>() == true
        ? TargetType.AllEnemies
        : targetType;


    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(16, ValueProp.Move),
        new CardsVar(1)
    ];

    public GiantShurikenCard() : base(CardSpec) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await ShurikenCombat.BuildAttackCommand(this, cardPlay, DynamicVars.Damage, CombatState)
            .Execute(choiceContext);
        await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.BaseValue, Owner);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(4);
    }
}
