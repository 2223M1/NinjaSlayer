using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public sealed class PerfectChop : NinjaSlayerCardTemplate
{
    public override CardAssetProfile AssetProfile => NinjaSlayerCardAssets.Named("Chop");

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(16, ValueProp.Move),
        new CalculationBaseVar(2),
        new CalculationExtraVar(2),
        new CalculatedKarateVar().WithMultiplier(CountChopCards)
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromPower<KaratePower>()
    ];

    public PerfectChop() : base(2, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy, true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .WithHeavyBluntHitFx()
            .WithAttackerAnim("SlowAttack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);

        await PowerCmd.Apply<KaratePower>(
            choiceContext,
            cardPlay.Target,
            DynamicVars.CalculatedKarate().Calculate(cardPlay.Target),
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.CalculationBase.UpgradeValueBy(3);
    }

    private static decimal CountChopCards(CardModel card, Creature? _) =>
        card.Owner.PlayerCombatState?.AllCards.Count(c =>
            c.GetType().Name.Contains("Chop", StringComparison.OrdinalIgnoreCase)) ?? 0;
}
