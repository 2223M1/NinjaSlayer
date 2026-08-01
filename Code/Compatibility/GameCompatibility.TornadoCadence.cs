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

        public static bool TryResolveStateMachines(
            out RuntimePatchTarget[] targets,
            out string missingMember)
        {
            var signatures = new (string IdSuffix, Type DeclaringType, string Name, Type[] Parameters)[]
            {
                ("creature-damage", typeof(CreatureCmd), nameof(CreatureCmd.Damage),
                [
                    typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>), typeof(decimal),
                    typeof(ValueProp), typeof(Creature), typeof(CardModel)
#if !NINJASLAYER_LEGACY_DAMAGE_API
                    , typeof(CardPlay)
#endif
                ]),
                ("power-apply", typeof(PowerCmd), nameof(PowerCmd.Apply),
                [
                    typeof(PlayerChoiceContext), typeof(PowerModel), typeof(Creature), typeof(decimal),
                    typeof(Creature), typeof(CardModel), typeof(bool)
                ]),
                ("power-modify-amount", typeof(PowerCmd), nameof(PowerCmd.ModifyAmount),
                [
                    typeof(PlayerChoiceContext), typeof(PowerModel), typeof(decimal), typeof(Creature),
                    typeof(CardModel), typeof(bool)
                ])
            };
            var resolved = new List<RuntimePatchTarget>(signatures.Length);
            foreach ((string idSuffix, Type declaringType, string methodName, Type[] parameterTypes) in signatures)
            {
                MethodInfo? method = AccessTools.Method(declaringType, methodName, parameterTypes);
                Type? stateMachine = method?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
                MethodInfo? moveNext = stateMachine is null
                    ? null
                    : AccessTools.Method(stateMachine, "MoveNext", Type.EmptyTypes);
                if (moveNext == null)
                {
                    targets = [];
                    missingMember = $"{declaringType.FullName}.{methodName} async state machine";
                    return false;
                }

                resolved.Add(new RuntimePatchTarget(idSuffix, moveNext));
            }

            targets = resolved.ToArray();
            missingMember = string.Empty;
            return true;
        }
    }
}
