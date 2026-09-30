using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Cards.Standard;

[RegisterCard(typeof(NinjaSlayerCardPool))]
public sealed class StraightPunch : NinjaSlayerStandaloneCardTemplate
{
    private static readonly NinjaSlayerCardSpec Spec = new(
        nameof(StraightPunch),
        2,
        CardType.Attack,
        CardRarity.Basic,
        TargetType.AnyEnemy,
        true);

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(8, ValueProp.Move), new KarateVar(4)];

    public StraightPunch() : base(Spec) { }

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
        await PowerCmd.Apply<KaratePower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.Karate().BaseValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(3);
        DynamicVars.Karate().UpgradeValueBy(1);
    }
}
