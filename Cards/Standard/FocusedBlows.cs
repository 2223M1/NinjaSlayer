using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards.Standard;

public sealed class FocusedBlows : NinjaSlayerCommonCard
{
    public FocusedBlows() : base(nameof(FocusedBlows), 1, CardType.Attack, TargetType.AnyEnemy) { }
    public override CardAssetProfile AssetProfile => NinjaSlayerCardAssets.Named("RightUppercut");
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<ShurikenAimPower>()];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(6, ValueProp.Move), new PowerVar<ShurikenAimPower>(3)
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
            .FromCard(this)
#else
            .FromCard(this, cardPlay)
#endif
            .WithDefectStrikeHitFx()
            .WithAttackerAnim("SlowAttack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);
        await PowerCmd.Apply<ShurikenAimPower>(choiceContext, cardPlay.Target!,
            DynamicVars[nameof(ShurikenAimPower)].BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(3);
        DynamicVars[nameof(ShurikenAimPower)].UpgradeValueBy(1);
    }
}
