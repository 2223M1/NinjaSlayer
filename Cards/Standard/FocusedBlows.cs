using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards.Standard;

public sealed class FocusedBlows : NinjaSlayerCommonCard
{
    public FocusedBlows() : base(nameof(FocusedBlows), 1, CardType.Attack, TargetType.AnyEnemy) { }
    public override CardAssetProfile AssetProfile => NinjaSlayerCardAssets.Named("RightUppercut");
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromOrb<ShurikenOrb>()];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new CalculationBaseVar(6), new ExtraDamageVar(3),
        new CalculatedDamageVar(ValueProp.Move)
            .WithMultiplier(static (card, _) => ShurikenOrb.Find(card.Owner)?.StackCount ?? 0)
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.CalculatedDamage)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
            .FromCard(this)
#else
            .FromCard(this, cardPlay)
#endif
            .WithDefectStrikeHitFx()
            .WithAttackerAnim("SlowAttack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);
    }

    protected override void OnUpgrade() => DynamicVars.ExtraDamage.UpgradeValueBy(1);
}
