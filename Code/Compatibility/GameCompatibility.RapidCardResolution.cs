using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class RapidCardResolution
    {
        private static readonly MethodInfo? OnPlayWrapper = AccessTools.Method(
            typeof(CardModel),
            nameof(CardModel.OnPlayWrapper),
            [typeof(PlayerChoiceContext), typeof(Creature), typeof(bool), typeof(ResourceInfo), typeof(bool)]);
        private static readonly MethodInfo? AddDuringManualPlay = AccessTools.Method(
            typeof(CardPileCmd),
            nameof(CardPileCmd.AddDuringManualCardPlay),
            [typeof(CardModel)]);
        public static MethodInfo? PowerFly { get; } = AccessTools.Method(
            typeof(CardModel),
            "PlayPowerCardFlyVfx",
            Type.EmptyTypes);
        public static MethodInfo? MultiPlay { get; } = AccessTools.Method(
            typeof(NCard),
            nameof(NCard.AnimMultiCardPlay),
            Type.EmptyTypes);
        public static MethodInfo? CustomWait { get; } = AccessTools.Method(
            typeof(Cmd),
            nameof(Cmd.CustomScaledWait),
            [typeof(float), typeof(float), typeof(bool), typeof(CancellationToken)]);
        public static MethodInfo? AwaitTween { get; } = AccessTools.Method(
            typeof(TweenHelper),
            nameof(TweenHelper.AwaitFinished),
            [typeof(Godot.Tween), typeof(Godot.Node)]);
        public static MethodInfo? AddCard { get; } = AccessTools.Method(
            typeof(CardPileCmd),
            nameof(CardPileCmd.Add),
            [typeof(CardModel), typeof(PileType), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)]);
        public static MethodInfo? RemoveFromCombat { get; } = AccessTools.Method(
            typeof(CardPileCmd),
            nameof(CardPileCmd.RemoveFromCombat),
            [typeof(CardModel), typeof(bool)]);
        public static MethodInfo? Exhaust { get; } = AccessTools.Method(
            typeof(CardCmd),
            nameof(CardCmd.Exhaust),
            [typeof(PlayerChoiceContext), typeof(CardModel), typeof(bool), typeof(bool)]);

        public static IReadOnlyList<CapabilityProbe> GetProbes()
        {
            bool stateMachinesMatch = TryResolveStateMachines(out _, out string reason);
            return
            [
                RequiredMember("rapid-card.custom-wait", CustomWait, "Cmd.CustomScaledWait"),
                RequiredMember("rapid-card.await-tween", AwaitTween, "TweenHelper.AwaitFinished"),
                RequiredMember("rapid-card.add", AddCard, "CardPileCmd.Add"),
                RequiredMember("rapid-card.remove", RemoveFromCombat, "CardPileCmd.RemoveFromCombat"),
                RequiredMember("rapid-card.exhaust", Exhaust, "CardCmd.Exhaust"),
                RequiredMember("rapid-card.power-fly", PowerFly, "CardModel.PlayPowerCardFlyVfx"),
                RequiredMember("rapid-card.multi-play", MultiPlay, "NCard.AnimMultiCardPlay"),
                CapabilityProbe.Required(
                    "rapid-card.state-machines",
                    stateMachinesMatch,
                    stateMachinesMatch ? "validated" : reason)
            ];
        }

        public static bool TryResolveStateMachines(
            out RuntimePatchTarget[] targets,
            out string reason)
        {
            if (!TryResolveProfile(OnPlayWrapper, out GameHostContractProfile? profile, out reason))
            {
                targets = [];
                return false;
            }

            var methods = new (string Id, MethodInfo? Method, MethodBodyContract Contract)[]
            {
                ("on-play-wrapper", OnPlayWrapper, profile!.RapidCardResolution.OnPlayWrapper),
                ("add-during-manual-play", AddDuringManualPlay, profile.RapidCardResolution.AddDuringManualPlay),
                ("power-fly", PowerFly, profile.RapidCardResolution.PowerFly),
                ("multi-play", MultiPlay, profile.RapidCardResolution.MultiPlay)
            };
            var resolved = new List<RuntimePatchTarget>(methods.Length);
            foreach ((string id, MethodInfo? method, MethodBodyContract contract) in methods)
            {
                if (method == null)
                {
                    targets = [];
                    reason = $"rapid-card {id} method is unavailable.";
                    return false;
                }

                if (!TryResolveMoveNext(method, out MethodInfo? moveNext) || moveNext == null)
                {
                    targets = [];
                    reason = $"rapid-card {id} async state machine is unavailable.";
                    return false;
                }

                if (!MethodBodyFingerprintCapture.TryCapture(
                        moveNext,
                        out MethodBodyFingerprint fingerprint,
                        out reason))
                {
                    targets = [];
                    return false;
                }

                if (!StableMethodBodyContract.Matches(fingerprint, profile, contract))
                {
                    targets = [];
                    reason = $"rapid-card {id} host contract does not match ({fingerprint}).";
                    return false;
                }

                resolved.Add(new RuntimePatchTarget(id, moveNext));
            }

            targets = resolved.ToArray();
            reason = string.Empty;
            return true;
        }

        private static bool TryResolveProfile(
            MethodInfo? method,
            out GameHostContractProfile? profile,
            out string reason)
        {
            if (method == null)
            {
                profile = null;
                reason = "rapid-card OnPlayWrapper is unavailable.";
                return false;
            }

            if (!MethodBodyFingerprintCapture.TryCaptureAsyncMoveNext(
                    method,
                    out MethodBodyFingerprint fingerprint,
                    out reason))
            {
                profile = null;
                return false;
            }

            if (!GameHostContractProfile.TryResolve(fingerprint, out GameHostContractProfile resolved))
            {
                profile = null;
                reason = $"Unsupported rapid-card host ({fingerprint}).";
                return false;
            }

            profile = resolved;
            return true;
        }

        private static bool TryResolveMoveNext(MethodInfo method, out MethodInfo? moveNext)
        {
            Type? stateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
            moveNext = stateMachine == null
                ? null
                : AccessTools.Method(stateMachine, nameof(IAsyncStateMachine.MoveNext), Type.EmptyTypes);
            return moveNext != null;
        }
    }
}
