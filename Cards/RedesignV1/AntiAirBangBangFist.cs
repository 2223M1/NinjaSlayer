using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Code.ExternalAnimations;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class AntiAirBangBangFist : RedesignV1RareCard
{
    public AntiAirBangBangFist()
        : base(nameof(AntiAirBangBangFist), "Chop", 2, CardType.Attack, TargetType.AnyEnemy) { }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(8, ValueProp.Move), new RepeatVar(1),
        new CalculationBaseVar(0), new CalculationExtraVar(1),
        new CalculatedVar("CalculatedHits").WithMultiplier((card, _) =>
            1 + card.Owner.Creature.Powers.Where(CountsAsBuff).Select(power => power.Id).Distinct().Count())
    ];

    private static bool CountsAsBuff(PowerModel power) =>
        power.Amount > 0 && power.TypeForCurrentAmount == PowerType.Buff
        && power is StrengthPower or DexterityPower or VigorPower or KaratePower or FocusPower
            or ArtifactPower or BufferPower or IntangiblePower or ThornsPower or PlatingPower
            or RegenPower or EvasionPower;

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int hits = (int)((CalculatedVar)DynamicVars["CalculatedHits"]).Calculate(cardPlay.Target);
        return DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
            .FromCard(this)
#else
            .FromCard(this, cardPlay)
#endif
            .WithHitCount(hits)
            .WithDefectStrikeHitFx()
            .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3);
}
