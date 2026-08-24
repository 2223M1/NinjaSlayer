using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using STS2RitsuLib.Patching.Models;
using STS2RitsuLib.Utils.HarmonyIl;

namespace NinjaSlayer.Code.Patches;

internal static class CombatPresentationPacingPatch
{
    private const string PatchIdPrefix = "ninjaslayer_combat_presentation_pacing";
    private static readonly MethodInfo CustomWait = RequireMethod(
        typeof(Cmd),
        nameof(Cmd.CustomScaledWait),
        [typeof(float), typeof(float), typeof(bool), typeof(CancellationToken)]);

    public static DynamicPatchInfo[] CreateDynamicPatches()
    {
        var targets = new (string IdSuffix, MethodInfo Method)[]
        {
            (
                "creature-damage",
                ResolveAsyncMoveNext(
                    typeof(CreatureCmd),
                    nameof(CreatureCmd.Damage),
                    GameCompatibility.Damage.CommandParameterTypes)),
            (
                "power-apply",
                ResolveAsyncMoveNext(
                    typeof(PowerCmd),
                    nameof(PowerCmd.Apply),
                    [
                        typeof(PlayerChoiceContext), typeof(PowerModel), typeof(Creature),
                        typeof(decimal), typeof(Creature), typeof(CardModel), typeof(bool)
                    ])),
            (
                "power-modify-amount",
                ResolveAsyncMoveNext(
                    typeof(PowerCmd),
                    nameof(PowerCmd.ModifyAmount),
                    [
                        typeof(PlayerChoiceContext), typeof(PowerModel), typeof(decimal),
                        typeof(Creature), typeof(CardModel), typeof(bool)
                    ]))
        };

        var transpiler = new HarmonyMethod(
            typeof(CombatPresentationPacingPatch),
            nameof(Transpiler));
        return targets.Select(target => new DynamicPatchInfo(
                $"{PatchIdPrefix}_{target.IdSuffix}",
                target.Method,
                transpiler: transpiler,
                isCritical: true,
                description: $"Apply scoped combat presentation pacing to {target.IdSuffix}."))
            .ToArray();
    }

    public static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase original)
    {
        bool isDamage = original.DeclaringType?.DeclaringType == typeof(MegaCrit.Sts2.Core.Commands.CreatureCmd);
        MethodInfo scopedWait = AccessTools.Method(
                typeof(CombatPresentationPacingScope),
                isDamage
                    ? nameof(CombatPresentationPacingScope.WaitForDamageRecovery)
                    : nameof(CombatPresentationPacingScope.WaitForPowerRecovery))
            ?? throw new MissingMethodException(typeof(CombatPresentationPacingScope).FullName);
        var rewriter = HarmonyIlRewriter.From(instructions);
        HarmonyIlRewriteReport report = HarmonyAsyncIl.RedirectAwaitedCalls(
            rewriter,
            "NinjaSlayer scoped combat presentation pacing",
            CustomWait,
            scopedWait,
            code => code.Any(HarmonyIl.IsCall(scopedWait)));
        return rewriter.InstructionsChecked(report);
    }

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
}
