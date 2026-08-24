using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.RedesignV1;
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
        ref Task<IEnumerable<DamageResult>> __result)
    {
        KaratePower? karate = dealer?.GetPower<KaratePower>();
        if (dealer == null || karate == null || karate.Amount <= 0 || !props.IsPoweredAttack())
        {
            return;
        }

        int stacks = karate.Amount;
        __result = ApplyKarate(__result, choiceContext, props, dealer, cardSource, karate, stacks);
    }

    private static async Task<IEnumerable<DamageResult>> ApplyKarate(
        Task<IEnumerable<DamageResult>> damageTask,
        PlayerChoiceContext choiceContext,
        ValueProp props,
        Creature dealer,
        CardModel? cardSource,
        KaratePower karate,
        int stacks)
    {
        List<DamageResult> results = (await damageTask).ToList();
        List<Creature> targets = results
            .Where(result => KarateWaveRules.IsEligibleHit(
                result.TotalDamage,
                result.Receiver.Side != dealer.Side,
                !result.Receiver.IsDead))
            .Select(result => result.Receiver)
            .Distinct()
            .ToList();
        KarateWaveResolution wave = KarateWaveRules.Resolve(stacks, props.IsPoweredAttack(), targets.Count);
        if (wave.Triggered)
        {
            int multiplier = cardSource is ClankDrinkTeaRedesignV1 assassination
                ? assassination.KarateMultiplier
                : 1;
            await NinjaSlayerActions.TriggerKarateWave(
                choiceContext,
                dealer,
                targets,
                karate,
                wave.BonusDamagePerTarget * multiplier,
                cardSource);
        }

        return results;
    }
}
