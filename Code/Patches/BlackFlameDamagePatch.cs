using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Combat;
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

#pragma warning disable CA1707 // Harmony reserves double-underscore parameter names.
#if NINJASLAYER_LEGACY_DAMAGE_API
    public static void Postfix(
        CardModel? cardSource,
        ref Task<IEnumerable<DamageResult>> __result) =>
        TrackResults(cardSource, suppliedCardPlay: null, ref __result);
#else
    public static void Postfix(
        CardModel? cardSource,
        CardPlay? cardPlay,
        ref Task<IEnumerable<DamageResult>> __result) =>
        TrackResults(cardSource, cardPlay, ref __result);
#endif
#pragma warning restore CA1707

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
