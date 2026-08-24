using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
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
    private static readonly MethodInfo CustomWait = RequireMethod(
        typeof(Cmd),
        nameof(Cmd.CustomScaledWait),
        [typeof(float), typeof(float), typeof(bool), typeof(CancellationToken)]);
    private static readonly MethodInfo AwaitTween = RequireMethod(
        typeof(TweenHelper),
        nameof(TweenHelper.AwaitFinished),
        [typeof(Godot.Tween), typeof(Godot.Node)]);
    private static readonly MethodInfo RemoveFromCombat = RequireMethod(
        typeof(CardPileCmd),
        nameof(CardPileCmd.RemoveFromCombat),
        [typeof(CardModel), typeof(bool)]);
    private static readonly MethodInfo Exhaust = RequireMethod(
        typeof(CardCmd),
        nameof(CardCmd.Exhaust),
        [typeof(PlayerChoiceContext), typeof(CardModel), typeof(bool), typeof(bool)]);

    public static DynamicPatchInfo[] CreateDynamicPatches()
    {
        MethodInfo onPlayWrapper = ResolveAsyncMoveNext(
            typeof(CardModel),
            nameof(CardModel.OnPlayWrapper),
            [typeof(PlayerChoiceContext), typeof(Creature), typeof(bool), typeof(ResourceInfo), typeof(bool)]);
        MethodInfo addDuringManualPlay = ResolveAsyncMoveNext(
            typeof(CardPileCmd),
            nameof(CardPileCmd.AddDuringManualCardPlay),
            [typeof(CardModel)]);

        return
        [
            CreateDynamicPatch(
                "on-play-wrapper",
                onPlayWrapper,
                nameof(TranspileOnPlayWrapper)),
            CreateDynamicPatch(
                "add-during-manual-play",
                addDuringManualPlay,
                nameof(TranspileAddDuringManualPlay))
        ];
    }

    public static IEnumerable<CodeInstruction> TranspileOnPlayWrapper(
        IEnumerable<CodeInstruction> instructions)
    {
        var rewriter = HarmonyIlRewriter.From(instructions);
        Redirect(
            rewriter,
            "rapid-card presentation waits",
            CustomWait,
            RequireMethod(
                typeof(RapidCardPresentationContext),
                nameof(RapidCardPresentationContext.WaitUnlessActive),
                [typeof(float), typeof(float), typeof(bool), typeof(CancellationToken)]),
            expectedSites: 2);
        Redirect(
            rewriter,
            "rapid-card removals",
            RemoveFromCombat,
            RequireMethod(
                typeof(RapidCardPresentationContext),
                nameof(RapidCardPresentationContext.RemoveFromCombat),
                [typeof(CardModel), typeof(bool)]));
        Redirect(
            rewriter,
            "rapid-card exhausts",
            Exhaust,
            RequireMethod(
                typeof(RapidCardPresentationContext),
                nameof(RapidCardPresentationContext.Exhaust),
                [typeof(PlayerChoiceContext), typeof(CardModel), typeof(bool), typeof(bool)]));
        return rewriter.Instructions();
    }

    public static IEnumerable<CodeInstruction> TranspileAddDuringManualPlay(
        IEnumerable<CodeInstruction> instructions)
    {
        var rewriter = HarmonyIlRewriter.From(instructions);
        Redirect(
            rewriter,
            "rapid-card manual play tween",
            AwaitTween,
            RequireMethod(
                typeof(RapidCardPresentationContext),
                nameof(RapidCardPresentationContext.AwaitTweenUnlessActive),
                [typeof(Godot.Tween), typeof(Godot.Node)]));
        return rewriter.Instructions();
    }

    private static DynamicPatchInfo CreateDynamicPatch(
        string idSuffix,
        MethodInfo target,
        string transpiler) =>
        new(
            $"{PatchIdPrefix}_{idSuffix}",
            target,
            transpiler: new HarmonyMethod(typeof(RapidCardResolutionStateMachinePatch), transpiler),
            isCritical: true,
            description: $"Detach Ninja Slayer card presentation in {idSuffix}.");

    private static MethodInfo ResolveAsyncMoveNext(
        Type declaringType,
        string methodName,
        Type[] parameterTypes)
    {
        MethodInfo method = RequireMethod(declaringType, methodName, parameterTypes);
        Type stateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            ?? throw new MissingMethodException(
                $"{declaringType.FullName}.{methodName} has no async state machine.");
        return RequireMethod(stateMachine, nameof(IAsyncStateMachine.MoveNext), Type.EmptyTypes);
    }

    private static MethodInfo RequireMethod(
        Type declaringType,
        string methodName,
        Type[] parameterTypes) =>
        AccessTools.Method(declaringType, methodName, parameterTypes)
        ?? throw new MissingMethodException(declaringType.FullName, methodName);

    private static void Redirect(
        HarmonyIlRewriter rewriter,
        string operation,
        MethodInfo original,
        MethodInfo replacement,
        int expectedSites = 1)
    {
        HarmonyIlRewriteReport report = HarmonyAsyncIl.RedirectAwaitedCalls(
            rewriter,
            operation,
            original,
            replacement,
            code => code.Any(HarmonyIl.IsCall(replacement)));
        rewriter.InstructionsChecked(report, expectedSites);
    }
}
