using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Enchantments;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class BlackFlameDamagePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_black_flame_damage_results";
    public static string Description => "Track actual damage receivers for Black Flame enchanted attacks.";
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
                typeof(CardModel),
                typeof(CardPlay)
            ])
    ];

    public static void Postfix(
        CardModel? cardSource,
        CardPlay? cardPlay,
        ref Task<IEnumerable<DamageResult>> __result)
    {
        if (cardPlay == null || cardSource != cardPlay.Card || cardSource.Enchantment is not BlackFlameEnchantment)
        {
            return;
        }

        __result = RecordResults(__result, cardPlay);
    }

    private static async Task<IEnumerable<DamageResult>> RecordResults(
        Task<IEnumerable<DamageResult>> damageTask,
        CardPlay cardPlay)
    {
        List<DamageResult> results = (await damageTask).ToList();
        BlackFlameHitTracker.Record(cardPlay, results);
        return results;
    }
}
