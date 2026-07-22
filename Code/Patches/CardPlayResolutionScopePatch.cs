using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Lifecycle;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

internal sealed class CardPlayResolutionBeforePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_card_play_scope_begin";
    public static string Description => "Begin a scoped state bag before card-play listeners run.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(Hook), nameof(Hook.BeforeCardPlayed), [typeof(ICombatState), typeof(CardPlay)])];

    public static void Prefix(CardPlay cardPlay) => CardPlayResolutionScope.BeginPlay(cardPlay);
}

internal sealed class CardPlayResolutionAfterPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_card_play_scope_complete";
    public static string Description => "Release per-play state after all post-card listeners finish.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(Hook), nameof(Hook.AfterCardPlayed),
            [typeof(ICombatState), typeof(PlayerChoiceContext), typeof(CardPlay)])
    ];

    public static void Postfix(CardPlay cardPlay, ref Task __result) =>
        __result = CardPlayResolutionScope.CompletePlayAfter(__result, cardPlay);
}

internal sealed class CardResolutionCleanupPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_card_resolution_scope_cleanup";
    public static string Description => "Release all card-play state when OnPlayWrapper completes or throws.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(CardModel), nameof(CardModel.OnPlayWrapper),
            [typeof(PlayerChoiceContext), typeof(Creature), typeof(bool), typeof(ResourceInfo), typeof(bool)])
    ];

    public static void Prefix(CardModel __instance, out CardPlayResolutionScope.CardResolution __state) =>
        __state = CardPlayResolutionScope.BeginCard(__instance);

    public static void Postfix(ref Task __result, CardPlayResolutionScope.CardResolution __state) =>
        __result = CardPlayResolutionScope.CompleteCardAfter(__result, __state);
}
