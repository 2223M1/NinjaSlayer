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
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Nodes.Screens.InspectScreens;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Transition;

namespace NinjaSlayer.Code.Compatibility;

internal static class GameCompatibility
{
    public const string SupportedGameVersion = "0.109.x";
    public static readonly IReadOnlyList<CompatibilityCapability> Capabilities =
    [
        new("finisher-command", SupportedGameVersion, "Disable enhanced finisher interception"),
        new("prepared-draw", SupportedGameVersion, "Disable Prepared draw filtering"),
        new("nancy-room-load", SupportedGameVersion, "Keep the loaded room without replacement"),
        new("karate-health-text", SupportedGameVersion, "Keep the original HP label"),
        new("typography", SupportedGameVersion, "Keep the original title font"),
        new("transition", SupportedGameVersion, "Use the original transition"),
        new("transition-loading", SupportedGameVersion, "Use the original asset finalization and GC"),
        new("tornado-cadence", SupportedGameVersion, "Keep original command pacing")
    ];

    internal static class Finisher
    {
        private static readonly FieldInfo? DamagePerHit = AccessTools.Field(typeof(AttackCommand), "_damagePerHit");
        private static readonly FieldInfo? CalculatedDamage = AccessTools.Field(typeof(AttackCommand), "_calculatedDamageVar");
        private static readonly FieldInfo? HitCount = AccessTools.Field(typeof(AttackCommand), "_hitCount");
        private static readonly FieldInfo? SingleTarget = AccessTools.Field(typeof(AttackCommand), "_singleTarget");

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
    }

    internal readonly record struct AttackCommandState(
        decimal DamagePerHit,
        CalculatedDamageVar? CalculatedDamage,
        int HitCount,
        Creature? SingleTarget);

    internal static class Prepared
    {
        private static readonly MethodInfo? DrawInternal = AccessTools.Method(
            typeof(CardPileCmd),
            "DrawInternal",
            [typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool)]);
        private static readonly MethodInfo? ShuffleFtueCheck = AccessTools.Method(typeof(CardPileCmd), "ShuffleFtueCheck");
        private static readonly FieldInfo? Grid = AccessTools.Field(typeof(NCardPileScreen), "_grid");

        public static bool CanInstall(out string missingMember)
        {
            if (DrawInternal == null)
            {
                missingMember = "CardPileCmd.DrawInternal(PlayerChoiceContext, decimal, Player, bool)";
                return false;
            }

            if (ShuffleFtueCheck == null)
            {
                missingMember = "CardPileCmd.ShuffleFtueCheck()";
                return false;
            }

            missingMember = string.Empty;
            return true;
        }

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

    internal static class Nancy
    {
        private static readonly FieldInfo? Rooms = AccessTools.Field(typeof(ActModel), "_rooms");

        public static bool TryGetRooms(ActModel act, out RoomSet? rooms)
        {
            rooms = Rooms?.GetValue(act) as RoomSet;
            return rooms != null;
        }
    }

    internal static class KarateHealthBar
    {
        private static readonly FieldInfo? Creature = AccessTools.Field(typeof(NHealthBar), "_creature");
        private static readonly FieldInfo? HpLabel = AccessTools.Field(typeof(NHealthBar), "_hpLabel");

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

        public static bool CanFinalize(out string missingMember)
        {
            if (Finalizing == null)
            {
                missingMember = $"{typeof(AssetLoadingSession).FullName}._finalizing";
                return false;
            }

            if (AddToCache == null)
            {
                missingMember = $"{typeof(AssetLoadingSession).FullName}.AddToCache";
                return false;
            }

            missingMember = string.Empty;
            return true;
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

        public static bool CanInstall(out string missingMember)
        {
            if (CustomWait == null)
            {
                missingMember = $"{typeof(Cmd).FullName}.{nameof(Cmd.CustomScaledWait)}";
                return false;
            }

            if (ScopedWait == null)
            {
                missingMember = $"{typeof(TornadoFistFinisherCadenceContext).FullName}.WaitUnlessActive";
                return false;
            }

            return TryResolveStateMachines(out _, out missingMember);
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

internal sealed record CompatibilityCapability(
    string Name,
    string SupportedVersion,
    string DegradedBehavior);
