using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.InspectScreens;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Transition;

namespace NinjaSlayer.Code.Compatibility;

internal static class GameCompatibility
{
    public const string SupportedGameVersion = "0.109.x";

    internal static class Finisher
    {
        private static readonly FieldInfo? DamagePerHit = AccessTools.Field(typeof(AttackCommand), "_damagePerHit");
        private static readonly FieldInfo? CalculatedDamage = AccessTools.Field(typeof(AttackCommand), "_calculatedDamageVar");
        private static readonly FieldInfo? HitCount = AccessTools.Field(typeof(AttackCommand), "_hitCount");
        private static readonly FieldInfo? SingleTarget = AccessTools.Field(typeof(AttackCommand), "_singleTarget");

        public static IReadOnlyList<CapabilityProbe> GetProbes()
        {
            bool lethalTargetAvailable = CanProtectLethalDamage(out string lethalReason);
            return
            [
                RequiredMember("AttackCommand.damage-per-hit", DamagePerHit, "AttackCommand._damagePerHit"),
                RequiredMember("AttackCommand.calculated-damage", CalculatedDamage, "AttackCommand._calculatedDamageVar"),
                RequiredMember("AttackCommand.hit-count", HitCount, "AttackCommand._hitCount"),
                RequiredMember("AttackCommand.single-target", SingleTarget, "AttackCommand._singleTarget"),
                CapabilityProbe.Required(
                    "Creature.lethal-damage-contract",
                    lethalTargetAvailable,
                    lethalTargetAvailable ? "validated" : lethalReason)
            ];
        }

        public static bool CanProtectLethalDamage(out string reason)
        {
            if (!FinisherLethalTargetContract.TryValidate(
                    out MethodInfo? lethalDamage,
                    out _,
                    out reason)
                || lethalDamage == null)
            {
                return false;
            }

            HarmonyLib.Patches? patchInfo = Harmony.GetPatchInfo(lethalDamage);
            if (patchInfo == null)
            {
                reason = string.Empty;
                return true;
            }

            HarmonyLib.Patch? unsafeTranspiler = patchInfo.Transpilers.FirstOrDefault(patch => !IsNinjaSlayerPatch(patch));
            if (unsafeTranspiler != null)
            {
                reason = $"foreign transpiler {DescribePatch(unsafeTranspiler)} targets Creature.LoseHpInternal.";
                return false;
            }

            HarmonyLib.Patch? skippingPrefix = patchInfo.Prefixes.FirstOrDefault(patch =>
                !IsNinjaSlayerPatch(patch) && patch.PatchMethod.ReturnType == typeof(bool));
            if (skippingPrefix != null)
            {
                reason = $"foreign bool Prefix {DescribePatch(skippingPrefix)} can skip Creature.LoseHpInternal.";
                return false;
            }

            HarmonyLib.Patch? resultReplacement = patchInfo.Prefixes
                .Concat(patchInfo.Postfixes)
                .Concat(patchInfo.Finalizers)
                .FirstOrDefault(patch =>
                    !IsNinjaSlayerPatch(patch)
                    && patch.PatchMethod.GetParameters().Any(parameter =>
                        parameter.Name == "__result"
                        && parameter.ParameterType.IsByRef
                        && parameter.ParameterType.GetElementType() == typeof(DamageResult)));
            if (resultReplacement != null)
            {
                reason = $"foreign result-replacement Patch {DescribePatch(resultReplacement)} targets Creature.LoseHpInternal.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool TryReadAttackCommand(AttackCommand command, out AttackCommandState state)
        {
            if (DamagePerHit == null || CalculatedDamage == null || HitCount == null || SingleTarget == null)
            {
                state = default;
                return false;
            }

            state = new AttackCommandState(
                (decimal)(DamagePerHit.GetValue(command) ?? 0m),
                CalculatedDamage.GetValue(command) as CalculatedDamageVar,
                (int)(HitCount.GetValue(command) ?? 1),
                SingleTarget.GetValue(command) as Creature);
            return true;
        }

        private static bool IsNinjaSlayerPatch(HarmonyLib.Patch patch) =>
            patch.PatchMethod.DeclaringType?.Assembly == typeof(GameCompatibility).Assembly;

        private static string DescribePatch(HarmonyLib.Patch patch) =>
            $"owner={patch.owner}, method={patch.PatchMethod.DeclaringType?.FullName}.{patch.PatchMethod.Name}, "
            + $"priority={patch.priority}, before=[{string.Join(',', patch.before)}], after=[{string.Join(',', patch.after)}]";
    }

    internal readonly record struct AttackCommandState(
        decimal DamagePerHit,
        CalculatedDamageVar? CalculatedDamage,
        int HitCount,
        Creature? SingleTarget);

    internal static class Prepared
    {
        private static readonly MethodInfo? ShuffleFtueCheck = AccessTools.Method(typeof(CardPileCmd), "ShuffleFtueCheck");
        private static readonly MethodInfo? AfterCardChangedPiles = AccessTools.Method(
            typeof(Hook),
            nameof(Hook.AfterCardChangedPiles),
            [typeof(IRunState), typeof(MegaCrit.Sts2.Core.Combat.ICombatState), typeof(CardModel), typeof(PileType), typeof(AbstractModel)]);
        private static readonly MethodInfo? InitializeSavedRun = AccessTools.Method(
            typeof(RunManager),
            "InitializeSavedRun",
            [typeof(SerializableRun)]);
        private static readonly FieldInfo? Grid = AccessTools.Field(typeof(NCardPileScreen), "_grid");

        public static IReadOnlyList<CapabilityProbe> GetGameplayProbes()
        {
            bool drawContractMatches = PreparedDrawTargetContract.TryValidate(
                out _,
                out PreparedDrawTargetFingerprint fingerprint,
                out string reason);
            bool queueContractMatches = PreparedQueueCompatibility.TryValidate(
                out PreparedQueueFingerprint queueFingerprint,
                out string queueReason);
            return
            [
                CapabilityProbe.Required(
                    "CardPileCmd.draw-wrapper-and-internal-contract",
                    drawContractMatches,
                    drawContractMatches ? fingerprint.ToString() : reason),
                CapabilityProbe.Required(
                    "CardPile.prepared-queue-contract",
                    queueContractMatches,
                    queueContractMatches ? queueFingerprint.ToString() : queueReason),
                RequiredMember("CardPileCmd.shuffle-ftue", ShuffleFtueCheck, "CardPileCmd.ShuffleFtueCheck()")
            ];
        }

        public static IReadOnlyList<CapabilityProbe> GetSafetyProbes() =>
        [
            RequiredMember(
                "Hook.after-card-changed-piles",
                AfterCardChangedPiles,
                "Hook.AfterCardChangedPiles(IRunState, ICombatState, CardModel, PileType, AbstractModel)"),
            CapabilityProbe.Optional(
                "RunManager.initialize-saved-run",
                InitializeSavedRun != null,
                InitializeSavedRun != null ? "available" : "RunManager.InitializeSavedRun(SerializableRun) is unavailable")
        ];

        public static IReadOnlyList<CapabilityProbe> GetUiProbes() =>
        [
            RequiredMember("NCardPileScreen.grid", Grid, "NCardPileScreen._grid")
        ];

        public static async Task ShowShuffleFtue()
        {
            if (ShuffleFtueCheck?.Invoke(null, null) is Task task)
            {
                await task;
            }
        }

        public static bool TryGetGrid(NCardPileScreen screen, out NCardGrid? grid)
        {
            grid = Grid?.GetValue(screen) as NCardGrid;
            return grid != null;
        }
    }

    internal static class KarateHealthBar
    {
        private static readonly FieldInfo? Creature = AccessTools.Field(typeof(NHealthBar), "_creature");
        private static readonly FieldInfo? HpLabel = AccessTools.Field(typeof(NHealthBar), "_hpLabel");

        public static IReadOnlyList<CapabilityProbe> GetProbes() =>
        [
            CapabilityProbe.Optional(
                "NHealthBar.creature",
                Creature != null,
                Creature != null ? "available" : "NHealthBar._creature is unavailable"),
            CapabilityProbe.Optional(
                "NHealthBar.hp-label",
                HpLabel != null,
                HpLabel != null ? "available" : "NHealthBar._hpLabel is unavailable")
        ];

        public static bool TryGetState(NHealthBar healthBar, out Creature? creature, out MegaLabel? hpLabel)
        {
            creature = Creature?.GetValue(healthBar) as Creature;
            hpLabel = HpLabel?.GetValue(healthBar) as MegaLabel;
            return creature != null && hpLabel != null;
        }
    }

    internal static class Typography
    {
        private static readonly FieldInfo? Relics = AccessTools.Field(typeof(NInspectRelicScreen), "_relics");
        private static readonly FieldInfo? Index = AccessTools.Field(typeof(NInspectRelicScreen), "_index");

        public static IReadOnlyList<CapabilityProbe> GetProbes() =>
        [
            CapabilityProbe.Optional(
                "NInspectRelicScreen.relics",
                Relics != null,
                Relics != null ? "available" : "NInspectRelicScreen._relics is unavailable"),
            CapabilityProbe.Optional(
                "NInspectRelicScreen.index",
                Index != null,
                Index != null ? "available" : "NInspectRelicScreen._index is unavailable")
        ];

        public static bool TryGetSelectedRelic(NInspectRelicScreen screen, out RelicModel? relic)
        {
            relic = null;
            if (Relics?.GetValue(screen) is not IReadOnlyList<RelicModel> relics
                || Index?.GetValue(screen) is not int index
                || index < 0
                || index >= relics.Count)
            {
                return false;
            }

            relic = relics[index];
            return true;
        }
    }

    internal static class Transition
    {
        private static readonly PropertyInfo? InTransition =
            AccessTools.Property(typeof(NTransition), nameof(NTransition.InTransition));
        private static readonly FieldInfo? Tween = AccessTools.Field(typeof(NTransition), "_tween");

        public static IReadOnlyList<CapabilityProbe> GetProbes() =>
        [
            RequiredMember("NTransition.in-transition", InTransition, "NTransition.InTransition"),
            RequiredMember("NTransition.tween", Tween, "NTransition._tween")
        ];

        public static void SetInTransition(NTransition transition, bool value) =>
            InTransition?.SetValue(transition, value);

        public static void KillTween(NTransition transition)
        {
            if (Tween?.GetValue(transition) is Tween tween)
            {
                tween.Kill();
                Tween.SetValue(transition, null);
            }
        }
    }

    internal static class AssetLoading
    {
        private static readonly FieldInfo? Finalizing = AccessTools.Field(typeof(AssetLoadingSession), "_finalizing");
        private static readonly MethodInfo? AddToCache = AccessTools.Method(typeof(AssetLoadingSession), "AddToCache");
        public static MethodInfo? GcCollect { get; } = AccessTools.Method(typeof(GC), nameof(GC.Collect), Type.EmptyTypes);
        public static MethodInfo? SafeCollect { get; } = AccessTools.Method(
            typeof(NinjaSlayerTransitionLoadSmoothing),
            nameof(NinjaSlayerTransitionLoadSmoothing.CollectWhenSafe));

        public static IReadOnlyList<CapabilityProbe> GetProbes()
        {
            bool stateMachinesAvailable = TryResolvePreloadStateMachines(out _, out string stateMachineReason);
            return
            [
                RequiredMember("AssetLoadingSession.finalizing", Finalizing, "AssetLoadingSession._finalizing"),
                RequiredMember("AssetLoadingSession.add-to-cache", AddToCache, "AssetLoadingSession.AddToCache"),
                RequiredMember("GC.collect", GcCollect, "System.GC.Collect()"),
                RequiredMember(
                    "TransitionLoadSmoothing.safe-collect",
                    SafeCollect,
                    "NinjaSlayerTransitionLoadSmoothing.CollectWhenSafe"),
                CapabilityProbe.Required(
                    "PreloadManager.state-machines",
                    stateMachinesAvailable,
                    stateMachinesAvailable ? "validated" : stateMachineReason)
            ];
        }

        public static bool TryGetFinalizing(AssetLoadingSession session, out Queue<string>? finalizing)
        {
            finalizing = Finalizing?.GetValue(session) as Queue<string>;
            return finalizing != null;
        }

        public static void Cache(AssetLoadingSession session, Resource? resource, string path) =>
            AddToCache?.Invoke(session, [resource, path]);

        public static bool TryResolvePreloadStateMachines(out Type[] stateMachines, out string missingMember)
        {
            var signatures = new (string Name, Type[] Parameters)[]
            {
                (nameof(PreloadManager.LoadRunAssets), [typeof(IEnumerable<CharacterModel>)]),
                (nameof(PreloadManager.LoadActAssets), [typeof(ActModel)]),
                ("LoadRoomAssets", [typeof(string), typeof(IEnumerable<string>)])
            };
            var resolved = new List<Type>(signatures.Length);
            foreach ((string methodName, Type[] parameterTypes) in signatures)
            {
                MethodInfo? method = AccessTools.Method(typeof(PreloadManager), methodName, parameterTypes);
                Type? stateMachine = method?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
                if (stateMachine == null)
                {
                    stateMachines = [];
                    missingMember = $"{typeof(PreloadManager).FullName}.{methodName} async state machine";
                    return false;
                }

                resolved.Add(stateMachine);
            }

            stateMachines = resolved.ToArray();
            missingMember = string.Empty;
            return true;
        }
    }

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

    internal static class ReporterPass
    {
        private static readonly MethodInfo? SetEventFinished =
            AccessTools.Method(typeof(EventModel), "SetEventFinished", [typeof(LocString)]);

        public static IReadOnlyList<CapabilityProbe> GetProbes() =>
        [
            RequiredMember("EventModel.set-event-finished", SetEventFinished,
                "EventModel.SetEventFinished(LocString)")
        ];

        public static bool TryFinish(EventModel eventModel, LocString result)
        {
            if (SetEventFinished == null)
            {
                return false;
            }

            SetEventFinished.Invoke(eventModel, [result]);
            return true;
        }
    }

    internal static class Feedback
    {
        private static readonly MethodInfo? SendButtonSelected =
            AccessTools.Method(typeof(NSendFeedbackScreen), "SendButtonSelected", [typeof(NButton)]);

        public static IReadOnlyList<CapabilityProbe> GetProbes() =>
        [
            RequiredMember("NSendFeedbackScreen.send-button-selected", SendButtonSelected,
                "NSendFeedbackScreen.SendButtonSelected(NButton)")
        ];

        public static bool TrySelectSendButton(NSendFeedbackScreen screen, NButton button)
        {
            if (SendButtonSelected == null)
            {
                return false;
            }

            SendButtonSelected.Invoke(screen, [button]);
            return true;
        }
    }

    private static CapabilityProbe RequiredMember(string name, MemberInfo? member, string memberDescription) =>
        CapabilityProbe.Required(
            name,
            member != null,
            member != null ? "available" : $"{memberDescription} is unavailable");
}
