using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Odds;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;
using NinjaSlayer.Events;
using NinjaSlayer.Ancients;
using NinjaSlayer.Monsters;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class SawatariEventOptionReadyPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_sawatari_event_option_ready";
    public static string Description => "Focus Sawatari choices after the native button entrance finishes.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NEventOptionButton), nameof(NEventOptionButton.EnableButton), [])
    ];

    public static void Postfix(NEventOptionButton __instance)
    {
        if (__instance.Event is TheMovingJungleEvent && __instance.IsVisibleInTree()
            && NEventRoom.Instance?.Layout?.OptionButtons.FirstOrDefault() == __instance)
            ActiveScreenContext.Instance.Update();
    }
}

internal static class SawatariEventRoute
{
    private static readonly FieldInfo ActRooms =
        AccessTools.Field(typeof(ActModel), "_rooms")
        ?? throw new MissingFieldException(typeof(ActModel).FullName, "_rooms");
    private static readonly ConditionalWeakTable<ActModel, State> States = new();

    public static void Reset(ActModel act) => States.Remove(act);

    public static void Schedule(ActModel act, EncounterModel encounter)
    {
        State state = States.GetOrCreateValue(act);
        state.Encounter = encounter;
        state.Pending = true;
    }

    public static bool TryActivate(ActModel act)
    {
        State state = States.GetOrCreateValue(act);
        if (!state.Pending || state.Encounter == null)
        {
            return false;
        }

        state.Pending = false;
        state.Active = true;
        state.SuppressNextEventVisit = true;
        return true;
    }

    public static EncounterModel ResolveEncounter(ActModel act)
    {
        if (States.TryGetValue(act, out State? state) && state.Active && state.Encounter != null)
        {
            return state.Encounter;
        }

        RoomSet rooms = GetRoomSet(act);
        if (rooms.normalEncounters.Count == 0 || rooms.normalEncountersVisited <= 0)
        {
            throw new InvalidOperationException("Sawatari event has no routed normal encounter.");
        }

        int index = (rooms.normalEncountersVisited - 1) % rooms.normalEncounters.Count;
        return rooms.normalEncounters[index]
            ?? throw new InvalidOperationException("Sawatari event has no routed normal encounter.");
    }

    public static void SetAncient(ActModel act, AncientEventModel ancient) =>
        GetRoomSet(act).Ancient = ancient;

    public static bool ConsumeSuppressedEventVisit(ActModel act)
    {
        if (!States.TryGetValue(act, out State? state) || !state.SuppressNextEventVisit)
        {
            return false;
        }

        state.SuppressNextEventVisit = false;
        return true;
    }

    internal static RoomSet GetRoomSet(ActModel act) =>
        ActRooms.GetValue(act) switch
        {
            RoomSet rooms => rooms,
            null => throw new InvalidOperationException($"Act {act.Id} has no room set."),
            _ => throw new InvalidOperationException(
                $"Act {act.Id} has a room set with an unexpected runtime type.")
        };

    private sealed class State
    {
        public EncounterModel? Encounter;
        public bool Pending;
        public bool Active;
        public bool SuppressNextEventVisit;
    }
}

public sealed class SawatariActRoomGenerationPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_sawatari_remove_from_event_queue";
    public static string Description => "Reserve Sawatari for strong-monster unknown results.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(ActModel),
            nameof(ActModel.GenerateRooms),
            [typeof(Rng), typeof(UnlockState), typeof(bool)
#if NINJASLAYER_CHANNEL_PREVIEW
                , typeof(bool)
#endif
            ])
    ];

    public static void Postfix(ActModel __instance)
    {
        SawatariEventRoute.Reset(__instance);
        __instance.RemoveEventFromSet(ModelDb.Event<TheMovingJungleEvent>());
    }
}

public sealed class SawatariUnknownRoomRollPatch : IPatchMethod
{
    [ThreadStatic]
    private static RollFrame? _currentRoll;

    public static string PatchId => "ninjaslayer_sawatari_unknown_monster_route";
    public static string Description => "Replace only an eligible strong-monster unknown result with Sawatari.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(UnknownMapPointOdds),
            nameof(UnknownMapPointOdds.Roll),
            [typeof(IEnumerable<RoomType>), typeof(IRunState)])
    ];

    public static void Prefix(
        UnknownMapPointOdds __instance,
        IRunState runState,
        out RollFrame __state)
    {
        EncounterModel nextEncounter = runState.Act.PullNextEncounter(RoomType.Monster);
        bool tutorialUnknown = runState.UnlockState.NumberOfRuns == 0
            && runState.MapPointHistory
                .SelectMany(history => history)
                .Count(entry => entry.MapPointType == MapPointType.Unknown) < 3;
        __state = new RollFrame(
            _currentRoll,
            __instance,
            runState,
            runState.Act,
            nextEncounter,
            tutorialUnknown,
            __instance.MonsterOdds);
        _currentRoll = __state;
    }

    public static void Postfix(
        UnknownMapPointOdds __instance,
        IRunState runState,
        ref RoomType __result,
        RollFrame __state)
    {
        try
        {
            if (__result != RoomType.Monster
                || __state.Chance <= 0f
                || runState.Rng.UnknownMapPoint.NextFloat() >= __state.Chance)
            {
                return;
            }

            SawatariEventRoute.Schedule(__state.Act, __state.Encounter);
            __result = RoomType.Event;
        }
        finally
        {
            RestoreRoll(__state);
        }
    }

    public static Exception? Finalizer(
        Exception? __exception,
        UnknownMapPointOdds __instance,
        RollFrame __state)
    {
        RestoreRoll(__state);
        return __exception;
    }

    internal static void CaptureAllowedRooms(
        IRunState runState,
        IReadOnlySet<RoomType> allowed)
    {
        RollFrame? roll = _currentRoll;
        if (roll == null
            || roll.CapturedAllowedRooms
            || !ReferenceEquals(roll.RunState, runState))
        {
            return;
        }

        roll.CapturedAllowedRooms = true;
        if (runState is not RunState concreteRunState)
        {
            return;
        }

        EventModel sawatari = ModelDb.Event<TheMovingJungleEvent>();
        bool eligible = runState.CurrentActIndex is 0 or 2
            && NinjaSlayerContentAccess.HasNinjaSlayer(runState)
            && !roll.Encounter.IsWeak
            && allowed.Contains(RoomType.Monster)
            && allowed.Contains(RoomType.Event)
            && !concreteRunState.VisitedEventIds.Contains(sawatari.Id)
            && !roll.TutorialUnknown;
        if (!eligible)
        {
            return;
        }

        // Use the generated run pool: it already includes shared events and unlock filtering.
        // Sawatari occupies one additional equal-weight slot, routed through monster rooms.
        int validEventCount = 1 + SawatariEventRoute.GetRoomSet(roll.Act).events.Count(eventModel =>
            eventModel.IsAllowed(concreteRunState)
            && !concreteRunState.VisitedEventIds.Contains(eventModel.Id));
        float eventOdds = Math.Max(0f, 1f - Math.Max(0f, roll.OriginalMonsterOdds)
            - (allowed.Contains(RoomType.Elite) ? Math.Max(0f, roll.Odds.EliteOdds) : 0f)
            - (allowed.Contains(RoomType.Treasure) ? Math.Max(0f, roll.Odds.TreasureOdds) : 0f)
            - (allowed.Contains(RoomType.Shop) ? Math.Max(0f, roll.Odds.ShopOdds) : 0f));
        roll.Chance = SawatariEventRules.ResolveMonsterReplacementChance(
            eventOdds, validEventCount, roll.OriginalMonsterOdds);
    }

    private static void RestoreRoll(RollFrame roll)
    {
        if (ReferenceEquals(_currentRoll, roll))
        {
            _currentRoll = roll.Previous;
        }
    }

    public sealed class RollFrame(
        RollFrame? previous,
        UnknownMapPointOdds odds,
        IRunState runState,
        ActModel act,
        EncounterModel encounter,
        bool tutorialUnknown,
        float originalMonsterOdds)
    {
        public RollFrame? Previous { get; } = previous;
        public UnknownMapPointOdds Odds { get; } = odds;
        public IRunState RunState { get; } = runState;
        public ActModel Act { get; } = act;
        public EncounterModel Encounter { get; } = encounter;
        public bool TutorialUnknown { get; } = tutorialUnknown;
        public float OriginalMonsterOdds { get; } = originalMonsterOdds;
        public bool CapturedAllowedRooms { get; set; }
        public float Chance { get; set; }
    }
}

public sealed class SawatariUnknownRoomTypeCapturePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_sawatari_unknown_room_types";
    public static string Description => "Capture the host-filtered unknown-room types without invoking hooks twice.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(Hook),
            nameof(Hook.ModifyUnknownMapPointRoomTypes),
            [typeof(IRunState), typeof(IReadOnlySet<RoomType>)])
    ];

    [HarmonyPriority(Priority.Last)]
    public static void Postfix(IRunState runState, IReadOnlySet<RoomType> __result) =>
        SawatariUnknownRoomRollPatch.CaptureAllowedRooms(runState, __result);
}

public sealed class SawatariPullEventPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_sawatari_pull_routed_event";
    public static string Description => "Pull Sawatari without advancing the ordinary event queue.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(ActModel), nameof(ActModel.PullNextEvent), [typeof(RunState)])
    ];

    public static bool Prefix(ActModel __instance, RunState runState, ref EventModel __result)
    {
        if (!SawatariEventRoute.TryActivate(__instance))
        {
            return true;
        }

        EventModel sawatari = ModelDb.Event<TheMovingJungleEvent>();
        __instance.MarkRoomVisited(RoomType.Monster);
        runState.AddVisitedEvent(sawatari);
        __result = sawatari;
        return false;
    }
}

public sealed class SawatariRoomVisitPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_sawatari_monster_visit_accounting";
    public static string Description => "Count a routed Sawatari room as the consumed normal encounter.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(ActModel), nameof(ActModel.MarkRoomVisited), [typeof(RoomType)])
    ];

    public static bool Prefix(ActModel __instance, RoomType roomType) =>
        roomType != RoomType.Event || !SawatariEventRoute.ConsumeSuppressedEventVisit(__instance);
}

public sealed class SawatariCombatEndGatePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_sawatari_combat_end_gate";
    public static string Description => "Keep the embedded combat open through Sawatari choices.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(Hook), nameof(Hook.ShouldStopCombatFromEnding), [typeof(ICombatState)])
    ];

    public static void Postfix(ICombatState combatState, ref bool __result)
    {
        __result |= SawatariEventSession.ShouldStopCombat(combatState);
    }
}

public sealed class SawatariWinCheckPatch : IPatchMethod
{
#if !NINJASLAYER_CHANNEL_STABLE
    private static readonly Type TurnState = typeof(CombatManager).Assembly.GetType(
        "MegaCrit.Sts2.Core.Combat.CombatTurnState", throwOnError: true)!;
    private static readonly PropertyInfo State = TurnState.GetProperty("State")!;
#endif
    public static string PatchId => "ninjaslayer_sawatari_position_after_victory";
    public static string Description => "Position friendly Sawatari after native death and summon resolution.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(CombatManager), nameof(CombatManager.CheckWinCondition),
#if NINJASLAYER_CHANNEL_STABLE
            [])
#else
            [TurnState])
#endif
    ];

#if NINJASLAYER_CHANNEL_STABLE
    public static void Prefix(CombatManager __instance, out CombatState? __state) =>
        __state = __instance.DebugOnlyGetState();
#else
    public static void Prefix(object __0, out CombatState? __state) =>
        __state = (CombatState)State.GetValue(__0)!;
#endif

    public static async Task<bool> Postfix(Task<bool> __result, CombatState? __state)
    {
        bool ended = await __result;
        if (!ended && SawatariEventSession.TryGet(__state, out var session)) session.AfterWinCheck();
        return ended;
    }
}

public sealed class SawatariTurnEndPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_sawatari_turn_end";
    public static string Description => "Wait for all players to end their turn before Sawatari's choice.";
    public static bool IsCritical => true;
#if NINJASLAYER_CHANNEL_STABLE
    private static readonly PropertyInfo CombatToken = AccessTools.Property(typeof(CombatManager), "CombatCt");
#else
    private static readonly Type TurnState = typeof(CombatManager).Assembly.GetType(
        "MegaCrit.Sts2.Core.Combat.CombatTurnState", throwOnError: true)!;
    private static readonly PropertyInfo State = TurnState.GetProperty("State")!;
    private static readonly PropertyInfo CombatToken = TurnState.GetProperty("Ct")!;
#endif
    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(CombatManager), "EndPlayerTurnPhaseOneInternal",
#if NINJASLAYER_CHANNEL_STABLE
            [])
#else
            [TurnState])
#endif
    ];

#if NINJASLAYER_CHANNEL_STABLE
    public static void Prefix(CombatManager __instance, out (CombatState? State, CancellationToken Ct) __state) =>
        __state = (__instance.DebugOnlyGetState(), (CancellationToken)CombatToken.GetValue(__instance)!);
#else
    public static void Prefix(object __0, out (CombatState? State, CancellationToken Ct) __state) =>
        __state = ((CombatState)State.GetValue(__0)!, (CancellationToken)CombatToken.GetValue(__0)!);
#endif
    public static async Task Postfix(Task __result, (CombatState? State, CancellationToken Ct) __state)
    {
        await __result;
        if (SawatariEventSession.TryGet(__state.State, out var session)) await session.AfterPlayerTurnEnded(__state.Ct);
    }
}

public sealed class SawatariDuelDeathAnimationPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_sawatari_duel_retreat";
    public static string Description => "Suppress normal death visuals for Sawatari's event retreat.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NCreature), nameof(NCreature.StartDeathAnim), [typeof(bool)])
    ];

    public static bool Prefix(NCreature __instance, ref float __result)
    {
        if (!SawatariEventSession.IsActiveDuelCreature(__instance.Entity))
        {
            return true;
        }

        ((ForestSawatariMonster)__instance.Entity.Monster!).PlayDeathVoice();
        __result = 0f;
        return false;
    }
}

public sealed class SawatariDuelRewardsPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_sawatari_duel_rewards";
    public static string Description => "Offer the duel's fixed event relic instead of ordinary loot.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(RewardsSet), nameof(RewardsSet.WithRewardsFromRoom), [typeof(AbstractRoom)])
    ];

    public static bool Prefix(RewardsSet __instance, AbstractRoom room, ref RewardsSet __result)
    {
        if (room is not CombatRoom combatRoom
            || combatRoom.ParentEventId != ModelDb.Event<TheMovingJungleEvent>().Id
            || !combatRoom.ExtraRewards.TryGetValue(__instance.Player, out var extraRewards)
            || !extraRewards.OfType<RelicReward>().Any(reward => reward.Relic is NinjaSlayer.Relics.BioBambooSplintRelic))
        {
            return true;
        }

        // Native fixed extra rewards retain their model and owner across victory reloads.
        __result = __instance.EmptyForRoom(room);
        __instance.Rewards.AddRange(extraRewards);
        return false;
    }
}
