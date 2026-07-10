using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public sealed class ShurikenCleave : NinjaSlayerCardTemplate
{
    private const int energyCost = 2;
    private const CardType type = CardType.Attack;
    private const CardRarity rarity = CardRarity.Uncommon;
    private const TargetType targetType = TargetType.AllEnemies;
    private const bool shouldShowInCardLibrary = true;

    // ponytail: reuse barrage art until this card gets dedicated art.
    public override CardAssetProfile AssetProfile => NinjaSlayerCardAssets.Named("ShurikenBarrage");

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new CalculationBaseVar(10),
        new ExtraDamageVar(4),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(ShurikenInHandCount)
    ];

    public ShurikenCleave() : base(energyCost, type, rarity, targetType, shouldShowInCardLibrary) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerCombatAudioSet.For(Owner.Creature).FastAttack);
        await JumpAnimation.Play(Owner.Creature);
        await DamageCmd.Attack(DynamicVars.CalculatedDamage)
            .FromCard(this, cardPlay)
            .WithDefectStrikeHitFx()
            .TargetingAllOpponents(CombatState ?? throw new InvalidOperationException("Shuriken Cleave requires combat."))
            .Execute(choiceContext);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.CalculationBase.UpgradeValueBy(2);
        DynamicVars.ExtraDamage.UpgradeValueBy(2);
    }

    private static decimal ShurikenInHandCount(CardModel card, Creature? _)
    {
        return PileType.Hand.GetPile(card.Owner).Cards.Count(c => c.Tags.Contains(NinjaSlayerCardTags.Shuriken));
    }
}
