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
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Content;
using NinjaSlayer.Events;
using NinjaSlayer.Ancients;
using NinjaSlayer.Monsters;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class EventValidationRunGenerationPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_event_validation_run_generation";
    public static string Description => "Snapshot event validation and select Nancy Lee before act preloading.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(RunManager), nameof(RunManager.GenerateRooms), Type.EmptyTypes)
    ];

    public static void Prefix(RunManager __instance)
    {
        RunState? runState = __instance.DebugOnlyGetState();
        if (runState == null)
        {
            return;
        }

        bool enabled = NinjaSlayerSettings.ForceAllEventsOnce
            && runState.Players.Count == 1
            && NinjaSlayerContentAccess.HasNinjaSlayer(runState);
        NinjaSlayerRunData.SnapshotEventValidation(runState, enabled);
    }

    public static void Postfix(RunManager __instance)
    {
        RunState? runState = __instance.DebugOnlyGetState();
        if (runState == null
            || !NinjaSlayerRunData.IsEventValidationEnabled(runState)
            || runState.Acts.ElementAtOrDefault(2) is not Glory)
        {
            return;
        }

        GameCompatibility.EventCombat.SetAncient(
            runState.Acts[2],
            ModelDb.AncientEvent<NancyLee>());
    }
}

internal static class SawatariEventRoute
{
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

        return GameCompatibility.EventCombat.GetPreviousNormalEncounter(act)
            ?? throw new InvalidOperationException("Sawatari event has no routed normal encounter.");
    }

    public static bool ConsumeSuppressedEventVisit(ActModel act)
    {
        if (!States.TryGetValue(act, out State? state) || !state.SuppressNextEventVisit)
        {
            return false;
        }

        state.SuppressNextEventVisit = false;
        return true;
    }

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
            [typeof(Rng), typeof(UnlockState), typeof(bool)])
    ];

    public static void Postfix(ActModel __instance)
    {
        SawatariEventRoute.Reset(__instance);
        __instance.RemoveEventFromSet(ModelDb.Event<SawatariEvent>());
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
            if (__state.ForcedMonsterOdds)
            {
                __instance.MonsterOdds = __state.OriginalMonsterOdds;
            }

            RestoreRoll(__state);
        }
    }

    public static Exception? Finalizer(
        Exception? __exception,
        UnknownMapPointOdds __instance,
        RollFrame __state)
    {
        if (__exception != null && __state.ForcedMonsterOdds)
        {
            __instance.MonsterOdds = __state.OriginalMonsterOdds;
        }

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

        EventModel sawatari = ModelDb.Event<SawatariEvent>();
        bool eligible = runState.CurrentActIndex == 0
            && NinjaSlayerContentAccess.HasNinjaSlayer(runState)
            && !roll.Encounter.IsWeak
            && allowed.Contains(RoomType.Monster)
            && allowed.Contains(RoomType.Event)
            && !concreteRunState.VisitedEventIds.Contains(sawatari.Id)
            && !roll.TutorialUnknown;
        float naturalChance = eligible
            ? SawatariEventRules.ResolveMonsterReplacementChance(
                roll.Odds.EventOdds,
                GameCompatibility.EventCombat.CountValidEvents(roll.Act, concreteRunState),
                roll.OriginalMonsterOdds)
            : 0f;
        roll.ForcedMonsterOdds = NinjaSlayerRunData.IsEventValidationEnabled(concreteRunState)
            && naturalChance > 0f;
        roll.Chance = roll.ForcedMonsterOdds ? 1f : naturalChance;
        if (roll.ForcedMonsterOdds)
        {
            roll.Odds.MonsterOdds = 1f;
        }
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
        public bool ForcedMonsterOdds { get; set; }
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

        EventModel sawatari = ModelDb.Event<SawatariEvent>();
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

        __result = 0f;
        return false;
    }
}

public sealed class SawatariDuelRewardsPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_sawatari_duel_rewards";
    public static string Description => "Replace the strong-monster rewards with two relics after the duel.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(RewardsSet), nameof(RewardsSet.WithRewardsFromRoom), [typeof(AbstractRoom)])
    ];

    public static void Postfix(RewardsSet __instance, AbstractRoom room)
    {
        if (room is not CombatRoom combatRoom
            || !SawatariEventSession.ShouldReplaceRewards(combatRoom))
        {
            return;
        }

        __instance.Rewards.Clear();
        __instance.Rewards.Add(new RelicReward(__instance.Player));
        __instance.Rewards.Add(new RelicReward(__instance.Player));
    }
}
