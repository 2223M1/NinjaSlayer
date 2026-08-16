using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Lifecycle;
using STS2RitsuLib.Patching.Models;
using STS2RitsuLib.Utils.HarmonyIl;

namespace NinjaSlayer.Code.Patches;

internal sealed class RapidCardResolutionScopePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_rapid_card_scope";
    public static string Description => "Mark Ninja Slayer OnPlayWrapper presentation as non-blocking.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(CardModel), nameof(CardModel.OnPlayWrapper),
            [typeof(PlayerChoiceContext), typeof(Creature), typeof(bool), typeof(ResourceInfo), typeof(bool)])
    ];

    public static void Prefix(CardModel __instance, out RapidCardPresentationContext.ScopeLease __state)
    {
        __state = RapidCardPresentationContext.Begin(__instance);
        if (RapidCardPresentationContext.IsActive)
        {
            NinjaSlayerRapidAnimationCoordinator.EnsureLifecycle(__instance.Owner.Creature);
        }
    }

    public static void Postfix(
        CardModel __instance,
        RapidCardPresentationContext.ScopeLease __state,
        ref Task __result)
    {
        if (RapidCardPresentationContext.IsActive)
        {
            __result = SettleAfterCardResolution(__result, __instance);
        }

        __state.RestoreCallerContext();
    }

    private static async Task SettleAfterCardResolution(Task task, CardModel card)
    {
        try
        {
            await task;
            NinjaSlayerRapidAnimationCoordinator.CardGameplaySettled(card.Owner.Creature);
        }
        catch
        {
            NinjaSlayerRapidAnimationCoordinator.CancelAndRestore(card.Owner.Creature);
            throw;
        }
    }
}

internal sealed class RapidPowerCardFlyPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_rapid_power_card_fly";
    public static string Description => "Continue the original power-card fly VFX without blocking gameplay.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() => [new(typeof(CardModel), "PlayPowerCardFlyVfx", Type.EmptyTypes)];

    public static void Prefix(CardModel __instance) =>
        RapidCardPresentationContext.PreparePowerFly(__instance);

    public static void Postfix(ref Task __result)
    {
        if (!RapidCardPresentationContext.IsActive)
        {
            return;
        }

        TaskHelper.RunSafely(__result);
        __result = Task.CompletedTask;
    }
}

internal sealed class RapidMultiCardPlayPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_rapid_multi_card_play";
    public static string Description => "Continue repeated-card presentation without blocking gameplay.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() => [new(typeof(NCard), nameof(NCard.AnimMultiCardPlay), Type.EmptyTypes)];

    public static void Postfix(ref Task __result)
    {
        if (!RapidCardPresentationContext.IsActive)
        {
            return;
        }

        TaskHelper.RunSafely(__result);
        __result = Task.CompletedTask;
    }
}

internal static class RapidCardResolutionStateMachinePatch
{
    private const string PatchIdPrefix = "ninjaslayer_rapid_card_state_machine";

    public static DynamicPatchInfo[] CreateDynamicPatches()
    {
        if (!GameCompatibility.RapidCardResolution.TryResolveStateMachines(
                out GameCompatibility.RuntimePatchTarget[] targets,
                out string reason))
        {
            throw new MissingMethodException(reason);
        }

        return targets
            .Where(target => target.IdSuffix is "on-play-wrapper" or "add-during-manual-play")
            .Select(target => new DynamicPatchInfo(
                $"{PatchIdPrefix}_{target.IdSuffix}",
                target.Method,
                transpiler: new HarmonyMethod(
                    typeof(RapidCardResolutionStateMachinePatch),
                    target.IdSuffix == "on-play-wrapper"
                        ? nameof(TranspileOnPlayWrapper)
                        : nameof(TranspileAddDuringManualPlay)),
                isCritical: true,
                description: $"Detach Ninja Slayer card presentation in {target.IdSuffix}."))
            .ToArray();
    }

    public static IEnumerable<CodeInstruction> TranspileOnPlayWrapper(
        IEnumerable<CodeInstruction> instructions)
    {
        var rewriter = HarmonyIlRewriter.From(instructions);
        Redirect(
            rewriter,
            "rapid-card presentation waits",
            GameCompatibility.RapidCardResolution.CustomWait,
            AccessTools.Method(typeof(RapidCardPresentationContext), nameof(RapidCardPresentationContext.WaitUnlessActive)),
            expectedSites: 2);
        Redirect(
            rewriter,
            "rapid-card removals",
            GameCompatibility.RapidCardResolution.RemoveFromCombat,
            AccessTools.Method(typeof(RapidCardPresentationContext), nameof(RapidCardPresentationContext.RemoveFromCombat)));
        Redirect(
            rewriter,
            "rapid-card exhausts",
            GameCompatibility.RapidCardResolution.Exhaust,
            AccessTools.Method(typeof(RapidCardPresentationContext), nameof(RapidCardPresentationContext.Exhaust)));
        return rewriter.Instructions();
    }

    public static IEnumerable<CodeInstruction> TranspileAddDuringManualPlay(
        IEnumerable<CodeInstruction> instructions)
    {
        var rewriter = HarmonyIlRewriter.From(instructions);
        Redirect(
            rewriter,
            "rapid-card manual play tween",
            GameCompatibility.RapidCardResolution.AwaitTween,
            AccessTools.Method(typeof(RapidCardPresentationContext), nameof(RapidCardPresentationContext.AwaitTweenUnlessActive)));
        return rewriter.Instructions();
    }

    private static void Redirect(
        HarmonyIlRewriter rewriter,
        string operation,
        MethodInfo? original,
        MethodInfo? replacement,
        int expectedSites = 1)
    {
        if (original == null || replacement == null)
        {
            throw new MissingMethodException(operation);
        }

        HarmonyIlRewriteReport report = HarmonyAsyncIl.RedirectAwaitedCalls(
            rewriter,
            operation,
            original,
            replacement,
            code => code.Any(HarmonyIl.IsCall(replacement)));
        rewriter.InstructionsChecked(report, expectedSites);
    }
}
