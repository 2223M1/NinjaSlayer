using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class CombatPresentationPacing
    {
        public static MethodInfo? CustomWait { get; } = AccessTools.Method(
            typeof(Cmd),
            nameof(Cmd.CustomScaledWait),
            [typeof(float), typeof(float), typeof(bool), typeof(CancellationToken)]);

        public static IReadOnlyList<CapabilityProbe> GetProbes()
        {
            bool stateMachinesMatch = TryResolveStateMachines(out _, out string reason);
            return
            [
                RequiredMember("combat-pacing.custom-wait", CustomWait, "Cmd.CustomScaledWait"),
                CapabilityProbe.Required(
                    "combat-pacing.state-machines",
                    stateMachinesMatch,
                    stateMachinesMatch ? "validated" : reason)
            ];
        }

        public static bool TryResolveStateMachines(
            out RuntimePatchTarget[] targets,
            out string reason)
        {
            var signatures = new (string Id, Type Type, string Name, Type[] Parameters)[]
            {
                ("creature-damage", typeof(CreatureCmd), nameof(CreatureCmd.Damage),
                    Damage.CommandParameterTypes),
                ("power-apply", typeof(PowerCmd), nameof(PowerCmd.Apply),
                [
                    typeof(PlayerChoiceContext), typeof(PowerModel), typeof(Creature),
                    typeof(decimal), typeof(Creature), typeof(CardModel), typeof(bool)
                ]),
                ("power-modify-amount", typeof(PowerCmd), nameof(PowerCmd.ModifyAmount),
                [
                    typeof(PlayerChoiceContext), typeof(PowerModel), typeof(decimal),
                    typeof(Creature), typeof(CardModel), typeof(bool)
                ])
            };

            GameHostContractProfile? profile = null;
            var resolved = new List<RuntimePatchTarget>(signatures.Length);
            foreach ((string id, Type type, string name, Type[] parameters) in signatures)
            {
                MethodInfo? method = AccessTools.Method(type, name, parameters);
                Type? stateMachine = method?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
                MethodInfo? moveNext = stateMachine == null
                    ? null
                    : AccessTools.Method(stateMachine, nameof(IAsyncStateMachine.MoveNext), Type.EmptyTypes);
                if (!MethodBodyFingerprintCapture.TryCapture(
                        moveNext,
                        out MethodBodyFingerprint fingerprint,
                        out reason))
                {
                    targets = [];
                    return false;
                }

                if (profile == null
                    && !GameHostContractProfile.TryResolve(fingerprint, out profile))
                {
                    targets = [];
                    reason = $"Unsupported combat-pacing host ({fingerprint}).";
                    return false;
                }

                MethodBodyContract contract = id switch
                {
                    "creature-damage" => profile!.CombatPresentationPacing.CreatureDamage,
                    "power-apply" => profile!.CombatPresentationPacing.PowerApply,
                    _ => profile!.CombatPresentationPacing.PowerModifyAmount
                };
                if (!StableMethodBodyContract.Matches(fingerprint, profile!, contract))
                {
                    targets = [];
                    reason = $"combat-pacing {id} host contract does not match ({fingerprint}).";
                    return false;
                }

                resolved.Add(new RuntimePatchTarget(id, moveNext!));
            }

            targets = resolved.ToArray();
            reason = string.Empty;
            return true;
        }
    }
}
