using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using NinjaSlayer.Scripts;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Events;

[RegisterActEvent(typeof(Overgrowth))]
[RegisterActEvent(typeof(Underdocks))]
public sealed class SawatariEvent : ModEventTemplate
{
#if NINJASLAYER_CHANNEL_STABLE
    private static readonly FieldInfo EmbeddedCombatState =
        AccessTools.Field(typeof(EventModel), "_combatStateForCombatLayout")
        ?? throw new MissingFieldException(
            typeof(EventModel).FullName,
            "_combatStateForCombatLayout");
#else
    private static readonly FieldInfo CombatSynchronizer =
        AccessTools.Field(typeof(EventModel), "_combatSynchronizer")
        ?? throw new MissingFieldException(typeof(EventModel).FullName, "_combatSynchronizer");
    private static readonly PropertyInfo EmbeddedCombatState =
        AccessTools.Property(CombatSynchronizer.FieldType, "CombatStateForLayout")
        ?? throw new MissingMemberException(
            CombatSynchronizer.FieldType.FullName,
            "CombatStateForLayout");
#endif

    public override bool IsShared => true;
    public override EventLayoutType LayoutType => EventLayoutType.Combat;

    public override EncounterModel CanonicalEncounter
    {
        get
        {
            RunState runState = RunManager.Instance.DebugOnlyGetState()
                ?? throw new InvalidOperationException("Sawatari event requires an active run.");
            return SawatariEventRoute.ResolveEncounter(runState.Act);
        }
    }

    public override bool IsAllowed(IRunState runState) =>
        runState.CurrentActIndex == 0
        && NinjaSlayerContentAccess.HasNinjaSlayer(runState);

    protected override IReadOnlyList<EventOption> GenerateInitialOptions() =>
    [
        new EventOption(this, null, InitialOptionKey("WAIT"))
    ];

    public override IEnumerable<string> GetAssetPaths(IRunState runState)
    {
        if (TestMode.IsOn)
        {
            return [];
        }

        return base.GetAssetPaths(runState)
            .Concat(ModelDb.Monster<SawatariMonster>().AssetPaths)
            .Distinct();
    }

    public override void OnRoomEnter() => ClearCurrentOptions();

    public override Task AfterEventStarted()
    {
        Player? owner = Owner;
        if (owner == null || !LocalContext.IsMe(owner))
        {
            return Task.CompletedTask;
        }

        return BeginLocalEvent(owner);
    }

    internal void BeginEmbeddedCombat()
    {
        EncounterModel encounter = CanonicalEncounter;
#if NINJASLAYER_CHANNEL_STABLE
        encounter = encounter.ToMutable();
#endif
        EnterCombatWithoutExitingEvent(
            encounter,
            [],
            shouldResumeAfterCombat: false);
    }

    internal void ShowIntermissionPage()
    {
        SetEventState(
            PageDescription("INTERMISSION"),
            [
                new EventOption(
                    this,
                    TakeRegularLoot,
                    ModOptionKey("INTERMISSION", "TAKE_LOOT")),
                new EventOption(
                    this,
                    StartDuel,
                    ModOptionKey("INTERMISSION", "DUEL"))
            ]);
    }

    internal void ShowDuelResultPage()
    {
        SetEventState(
            PageDescription("DUEL_RESULT"),
            [
                new EventOption(
                    this,
                    TakeDuelRewards,
                    ModOptionKey("DUEL_RESULT", "TAKE_LOOT"))
            ]);
    }

    internal void FinishForFallback()
    {
        if (!IsFinished)
        {
            SetEventFinished(PageDescription("INTERMISSION"));
        }
    }

    private Task BeginLocalEvent(Player owner)
    {
        SawatariEvent[] events = RunManager.Instance.EventSynchronizer.Events
            .OfType<SawatariEvent>()
            .ToArray();
        SawatariEventSession? session = null;
        try
        {
            CombatState state = GetEmbeddedCombatState()
                ?? throw new InvalidOperationException("Embedded Sawatari combat state is unavailable.");
            NCombatRoom room = NEventRoom.Instance?.EmbeddedCombatRoom
                ?? throw new InvalidOperationException("Embedded Sawatari combat room is unavailable.");
            EventRoom eventRoom = RunManager.Instance.DebugOnlyGetState()?.CurrentRoom as EventRoom
                ?? throw new InvalidOperationException("Sawatari event room is unavailable.");

            session = SawatariEventSession.Create(state, room, events, owner, eventRoom);
        }
        catch (Exception exception)
        {
            Entry.Logger.Error($"Sawatari event setup failed; continuing as a normal encounter: {exception}");
            session?.AbortBeforeCombat();
            SawatariEventUi.Hide();
            foreach (SawatariEvent eventModel in events)
            {
                eventModel.FinishForFallback();
                eventModel.BeginEmbeddedCombat();
            }
            return Task.CompletedTask;
        }

        SawatariEventUi.Hide();
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.ForestSawatariBeginEvent);
        foreach (SawatariEvent eventModel in events)
        {
            eventModel.BeginEmbeddedCombat();
        }
        return Task.CompletedTask;
    }

    private CombatState? GetEmbeddedCombatState()
    {
#if NINJASLAYER_CHANNEL_STABLE
        object? value = EmbeddedCombatState.GetValue(this);
#else
        object? synchronizer = CombatSynchronizer.GetValue(this);
        if (synchronizer == null)
        {
            return null;
        }
        if (!CombatSynchronizer.FieldType.IsInstanceOfType(synchronizer))
        {
            throw new InvalidOperationException(
                "EventModel._combatSynchronizer has an unexpected runtime type.");
        }

        object? value = EmbeddedCombatState.GetValue(synchronizer);
#endif
        return value switch
        {
            null => null,
            CombatState state => state,
            _ => throw new InvalidOperationException(
                "The embedded event combat state has an unexpected runtime type.")
        };
    }

    private Task TakeRegularLoot()
    {
        SetEventFinished(PageDescription("INTERMISSION"));
        return RunLocalSession(session => session.TakeRegularLoot());
    }

    private Task StartDuel()
    {
        ClearCurrentOptions();
        return RunLocalSession(session => session.StartDuel());
    }

    private Task TakeDuelRewards()
    {
        SetEventFinished(PageDescription("DUEL_RESULT"));
        return RunLocalSession(session => session.TakeDuelRewards());
    }

    private Task RunLocalSession(Func<SawatariEventSession, Task> action)
    {
        Player? owner = Owner;
        if (owner == null || !LocalContext.IsMe(owner))
        {
            return Task.CompletedTask;
        }

        return SawatariEventSession.TryGet(owner.Creature.CombatState, out SawatariEventSession? session)
            ? action(session)
            : Task.CompletedTask;
    }
}

internal static class SawatariEventUi
{
    public static void Hide() =>
        (NEventRoom.Instance?.Layout as NCombatEventLayout)?.HideEventVisuals();

    public static void Show(bool isMultiplayer)
    {
        NCombatEventLayout? layout = NEventRoom.Instance?.Layout as NCombatEventLayout;
        if (layout == null)
        {
            throw new InvalidOperationException("Sawatari event layout is unavailable.");
        }

        layout.GetNode<CanvasItem>("%EventDescription").Visible = true;
        layout.GetNode<CanvasItem>("%SharedEventLabel").Visible = isMultiplayer;
        Control options = layout.GetNode<Control>("%OptionsContainer");
        options.Visible = true;
        options.GetChildren().OfType<Control>().FirstOrDefault()?.GrabFocus();
    }
}
