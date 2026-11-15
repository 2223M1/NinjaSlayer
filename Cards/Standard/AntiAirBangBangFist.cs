using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;

namespace NinjaSlayer.Cards.Standard;

public sealed class AntiAirBangBangFist : NinjaSlayerRareCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [NinjaSlayerHoverTips.PositiveEffects];

    public AntiAirBangBangFist()
        : base(nameof(AntiAirBangBangFist), 2, CardType.Attack, TargetType.RandomEnemy) { }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(8, ValueProp.Move), new RepeatVar(1),
        new CalculationBaseVar(0), new CalculationExtraVar(1),
        new CalculatedVar("CalculatedHits").WithMultiplier((card, _) =>
            1 + card.Owner.Creature.Powers.Where(NinjaSlayerBuffs.Includes).Select(power => power.Id).Distinct().Count())
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int hits = (int)((CalculatedVar)DynamicVars["CalculatedHits"]).Calculate(cardPlay.Target);
        Action? releaseJump = Code.Nodes.NinjaSlayerAimPose.Get(Owner.Creature)?.BeginComboJump();
        try
        {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
            .FromCard(this)
#else
            .FromCard(this, cardPlay)
#endif
            .WithHitCount(hits)
            .WithHitFx(VfxCmd.flyingSlashPath)
            .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
            .TargetingRandomOpponents(CombatState!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);
        }
        finally { releaseJump?.Invoke(); }
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3);
}
