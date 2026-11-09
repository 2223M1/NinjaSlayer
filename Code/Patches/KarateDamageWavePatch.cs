using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class KarateDamageWavePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_karate_damage_wave";
    public static string Description => "Apply attacker-owned Karate once per batch damage wave.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(CreatureCmd),
            nameof(CreatureCmd.Damage),
            [
                typeof(PlayerChoiceContext),
                typeof(IEnumerable<Creature>),
                typeof(decimal),
                typeof(ValueProp),
                typeof(Creature),
                typeof(CardModel)
#if !NINJASLAYER_LEGACY_DAMAGE_API
                , typeof(CardPlay)
#endif
            ])
    ];

    public static void Postfix(
        PlayerChoiceContext choiceContext,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        bool __runOriginal,
        ref Task<IEnumerable<DamageResult>> __result)
    {
        if (!__runOriginal
            || dealer == null
            || !props.IsPoweredAttack())
        {
            return;
        }

        if (!dealer.HasPower<KaratePower>() && !dealer.HasPower<OneMindOneBodyPower>())
        {
            return;
        }

        __result = ApplyWaveEffects(
            __result,
            choiceContext,
            props,
            dealer,
            cardSource);
    }

    private static async Task<IEnumerable<DamageResult>> ApplyWaveEffects(
        Task<IEnumerable<DamageResult>> damageTask,
        PlayerChoiceContext choiceContext,
        ValueProp props,
        Creature dealer,
        CardModel? cardSource)
    {
        List<DamageResult> results = (await damageTask).ToList();
        List<Creature> targets = results
            .Where(result => KarateWaveRules.IsEligibleHit(
                result.TotalDamage,
                result.Receiver.Side != dealer.Side))
            .Select(result => result.Receiver)
            .Distinct()
            .ToList();
        KarateWaveResolution wave = KarateWaveRules.Resolve(dealer.GetPowerAmount<KaratePower>(),
            props.IsPoweredAttack(), targets.Count,
            dealer.GetPowerAmount<NarakuLifePower>(), dealer.GetPowerAmount<OneMindOneBodyPower>());
        if (wave.Triggered)
        {
            await TriggerKarateWave(
                choiceContext,
                dealer,
                targets.Where(target => !target.IsDead).ToList(),
                wave.BonusDamagePerTarget,
                cardSource);
        }

        return results;
    }
    private static async Task TriggerKarateWave(
        PlayerChoiceContext choiceContext,
        Creature dealer,
        List<Creature> targets,
        int extraDamage,
        CardModel? cardSource)
    {
        if (extraDamage <= 0)
        {
            return;
        }

        using var _ = ScreenShakeSuppressionContext.Suppress();
        var combatState = dealer.CombatState!;
        if (targets.Count > 0)
        {
            var damage = await CreatureCmd.Damage(choiceContext, targets, extraDamage, ValueProp.Unpowered, dealer);
            Telemetry.NinjaSlayerCombatTelemetry.AttributeDamage(damage, "karate");
        }

        PowerModel? consumed = dealer.HasPower<OneMindOneBodyPower>()
            && dealer.GetPower<NarakuLifePower>() is { Amount: > 0 } life
                ? life : dealer.GetPower<KaratePower>();
        if (consumed is { Amount: > 0 })
        {
            int amountBeforeConsumption = consumed.Amount;
            await PowerCmd.ModifyAmount(choiceContext, consumed, -1, dealer, cardSource);
            if (CombatManager.Instance.IsEnding && consumed.Amount == amountBeforeConsumption)
            {
                // The last enemy can die during the bonus; the triggering wave still spends one resource.
                consumed.SetAmount(amountBeforeConsumption - 1, silent: true);
                if (consumed.Amount == 0) consumed.RemoveInternal();
                CombatHealthBar.Refresh(dealer);
                KaratePower.RefreshAttackDisplay(dealer);
            }
        }
        foreach (Creature creature in combatState.Creatures.ToArray())
        {
            if (!creature.IsDead && creature.GetPower<GuardStancePower>() is { } guard)
                await guard.AfterKarate();
        }
    }
}
