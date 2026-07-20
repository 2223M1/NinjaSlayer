using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Combat;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class TornadoFistFinisherCadencePatch : IPatchMethod
{
    private static readonly MethodInfo CustomWaitMethod = AccessTools.Method(
        typeof(Cmd),
        nameof(Cmd.CustomScaledWait),
        [typeof(float), typeof(float), typeof(bool), typeof(CancellationToken)]);

    private static readonly MethodInfo ScopedWaitMethod = AccessTools.Method(
        typeof(TornadoFistFinisherCadenceContext),
        nameof(TornadoFistFinisherCadenceContext.WaitUnlessActive));

    public static string PatchId => "ninjaslayer_tornado_fist_finisher_cadence";
    public static string Description => "Remove only generic damage and power pacing waits from Tornado Fist finishers.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(GetAsyncStateMachineType(
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
            ]), nameof(IAsyncStateMachine.MoveNext)),
        new(GetAsyncStateMachineType(
            typeof(PowerCmd),
            nameof(PowerCmd.Apply),
            [
                typeof(PlayerChoiceContext),
                typeof(PowerModel),
                typeof(Creature),
                typeof(decimal),
                typeof(Creature),
                typeof(CardModel),
                typeof(bool)
            ]), nameof(IAsyncStateMachine.MoveNext)),
        new(GetAsyncStateMachineType(
            typeof(PowerCmd),
            nameof(PowerCmd.ModifyAmount),
            [
                typeof(PlayerChoiceContext),
                typeof(PowerModel),
                typeof(decimal),
                typeof(Creature),
                typeof(CardModel),
                typeof(bool)
            ]), nameof(IAsyncStateMachine.MoveNext))
    ];

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> rewritten = instructions.ToList();
        int replacements = 0;
        foreach (CodeInstruction instruction in rewritten)
        {
            if (!instruction.Calls(CustomWaitMethod))
            {
                continue;
            }

            instruction.operand = ScopedWaitMethod;
            replacements++;
        }

        if (replacements != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one pacing wait in the patched async method, found {replacements}.");
        }

        return rewritten;
    }

    private static Type GetAsyncStateMachineType(Type declaringType, string methodName, Type[] parameterTypes)
    {
        MethodInfo method = AccessTools.Method(declaringType, methodName, parameterTypes)
            ?? throw new MissingMethodException(declaringType.FullName, methodName);
        return method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            ?? throw new InvalidOperationException($"{declaringType.FullName}.{methodName} is not async.");
    }
}
