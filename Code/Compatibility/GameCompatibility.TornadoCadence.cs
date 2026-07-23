using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class TornadoCadence
    {
        public static MethodInfo? CustomWait { get; } = AccessTools.Method(
            typeof(Cmd),
            nameof(Cmd.CustomScaledWait),
            [typeof(float), typeof(float), typeof(bool), typeof(CancellationToken)]);
        public static MethodInfo? ScopedWait { get; } = AccessTools.Method(
            typeof(TornadoFistFinisherCadenceContext),
            nameof(TornadoFistFinisherCadenceContext.WaitUnlessActive));

        public static IReadOnlyList<CapabilityProbe> GetProbes()
        {
            bool stateMachinesAvailable = TryResolveStateMachines(out _, out string stateMachineReason);
            return
            [
                RequiredMember("Cmd.custom-scaled-wait", CustomWait, "Cmd.CustomScaledWait"),
                RequiredMember(
                    "TornadoCadence.scoped-wait",
                    ScopedWait,
                    "TornadoFistFinisherCadenceContext.WaitUnlessActive"),
                CapabilityProbe.Required(
                    "TornadoCadence.state-machines",
                    stateMachinesAvailable,
                    stateMachinesAvailable ? "validated" : stateMachineReason)
            ];
        }

        public static bool TryResolveStateMachines(out Type[] stateMachines, out string missingMember)
        {
            var signatures = new (Type DeclaringType, string Name, Type[] Parameters)[]
            {
                (typeof(CreatureCmd), nameof(CreatureCmd.Damage),
                [
                    typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>), typeof(decimal),
                    typeof(ValueProp), typeof(Creature), typeof(CardModel), typeof(CardPlay)
                ]),
                (typeof(PowerCmd), nameof(PowerCmd.Apply),
                [
                    typeof(PlayerChoiceContext), typeof(PowerModel), typeof(Creature), typeof(decimal),
                    typeof(Creature), typeof(CardModel), typeof(bool)
                ]),
                (typeof(PowerCmd), nameof(PowerCmd.ModifyAmount),
                [
                    typeof(PlayerChoiceContext), typeof(PowerModel), typeof(decimal), typeof(Creature),
                    typeof(CardModel), typeof(bool)
                ])
            };
            var resolved = new List<Type>(signatures.Length);
            foreach ((Type declaringType, string methodName, Type[] parameterTypes) in signatures)
            {
                MethodInfo? method = AccessTools.Method(declaringType, methodName, parameterTypes);
                Type? stateMachine = method?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
                if (stateMachine == null)
                {
                    stateMachines = [];
                    missingMember = $"{declaringType.FullName}.{methodName} async state machine";
                    return false;
                }

                resolved.Add(stateMachine);
            }

            stateMachines = resolved.ToArray();
            missingMember = string.Empty;
            return true;
        }
    }
}
