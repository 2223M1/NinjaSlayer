using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class Slaughter : RedesignV1UncommonCard
{
    public Slaughter() : base(nameof(Slaughter), "Chop", 3, CardType.Attack, TargetType.AnyEnemy) { }
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(30, ValueProp.Move), new KarateVar(5)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<KaratePower>()];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var attack = DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
            .FromCard(this)
#else
            .FromCard(this, cardPlay)
#endif
            .WithHeavyBluntHitFx()
            .WithAttackerAnim("SlowAttack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target!);
        await attack.ExecuteWithFinisher(choiceContext, this, cardPlay);
        if (attack.Results.SelectMany(hit => hit).Any(result => result.WasTargetKilled))
            await PowerCmd.Apply<KaratePower>(choiceContext, Owner.Creature, DynamicVars.Karate().BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(8);
        DynamicVars.Karate().UpgradeValueBy(1);
    }
}
