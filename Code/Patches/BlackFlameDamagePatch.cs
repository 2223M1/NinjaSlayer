using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.Lifecycle;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed partial class BlackFlameDamagePatch : IPatchMethod
{
    private const string BlackFlameEnchantmentId = "NINJA_SLAYER_ENCHANTMENT_BLACK_FLAME_ENCHANTMENT";

    public static string PatchId => "ninjaslayer_black_flame_damage_results";
    public static string Description => "Track actual damage receivers for Black Flame enchanted attacks.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(CreatureCmd),
            nameof(CreatureCmd.Damage),
            GameCompatibility.Damage.CommandParameterTypes)
    ];

    private static void TrackResults(
        CardModel? cardSource,
        CardPlay? suppliedCardPlay,
        ref Task<IEnumerable<DamageResult>> resultTask)
    {
        if (cardSource?.Enchantment?.Id.Entry != BlackFlameEnchantmentId)
        {
            return;
        }

        CardPlay? cardPlay = suppliedCardPlay;
        if (cardPlay is null
            && !CardPlayResolutionScope.TryResolveCurrentPlay(cardSource, out cardPlay))
        {
            return;
        }
        if (!ReferenceEquals(cardSource, cardPlay.Card))
        {
            return;
        }

        resultTask = RecordResults(resultTask, cardPlay);
    }

    private static async Task<IEnumerable<DamageResult>> RecordResults(
        Task<IEnumerable<DamageResult>> damageTask,
        CardPlay cardPlay)
    {
        IEnumerable<DamageResult> results = await damageTask;
        List<DamageResult> snapshot = results.ToList();
        BlackFlameHitTracker.Record(cardPlay, snapshot);
        return snapshot;
    }
}
