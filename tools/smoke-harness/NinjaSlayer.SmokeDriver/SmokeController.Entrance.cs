using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Events;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    internal AncientEntranceAnimation.EntranceVariant? PreviewEntranceVariant { get; private set; }
    private bool _sawatariEntryObserved;

    internal void ObserveEventEntrance(Player player)
    {
        if (_theater?.IsEntrancePreview != true) return;
        var companion = NCombatRoom.Instance!.CreatureNodes.Single(n =>
            n.Entity.Monster is SawatariMonster && n.Entity.Side == CombatSide.Player);
        Require(companion.IsVisibleInTree() && companion.Visuals.IsVisibleInTree(),
            "Sawatari must already be visible in the friendly slot before Ninja Slayer enters.");
        Require(!player.Creature.GetCreatureNode()!.Visuals.Visible,
            "Ninja Slayer must remain hidden before the event entrance starts.");
        _sawatariEntryObserved = true;
        _checkpoints.Write("sawatari.companion-before-entrance");
    }

    private sealed partial class TheaterRuntime
    {
        internal bool IsEntrancePreview => _script.Purpose == "entrance";

        private async Task SawatariEventEntrance(TheaterStep step)
        {
            Require(IsEntrancePreview, "Event entrance requires its dedicated preview.");
            _driver.PreviewEntranceVariant = Enum.Parse<AncientEntranceAnimation.EntranceVariant>(step.Mode!);
            _driver._sawatariEntryObserved = false;
            try
            {
                var run = RunManager.Instance.DebugOnlyGetState()!;
                Type route = ProductType("NinjaSlayer.Code.Patches.SawatariEventRoute");
                InvokeMethod(route, null, "Schedule", run.Act, ModelDb.Encounter<GremlinMercNormal>());
                Require((bool)InvokeMethod(route, null, "TryActivate", run.Act)!, "Could not route Sawatari event.");
                await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<SawatariEvent>());
                await _driver.WaitUntilAsync(() => CombatManager.Instance.IsInProgress
                    && !CombatManager.Instance.IsStarting && _player.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
                    "Sawatari event did not finish the entrance and start combat", _cancel);
                Require(_driver._sawatariEntryObserved, "Sawatari event skipped the entrance.");
                var ninja = _player.Creature.GetCreatureNode()!;
                Require(ninja.Visuals.IsVisibleInTree(), "Ninja Slayer did not return to the battle layout.");
                var companion = NCombatRoom.Instance!.CreatureNodes.Single(n =>
                    n.Entity.Monster is SawatariMonster && n.Entity.Side == CombatSide.Player);
                AddActor("friendly_sawatari", companion.Entity);
                await Wait(2);
                Cover("sawatari-native-event-entrance");
            }
            finally { _driver.PreviewEntranceVariant = null; }
        }
    }
}

[HarmonyPatch(typeof(AncientEntranceAnimation), nameof(AncientEntranceAnimation.Play), [typeof(Player)])]
internal static class EventPreviewEntrance
{
    private static bool Prefix(Player player, ref Task __result)
    {
        if (SmokeController.Current?.UseFullArchitectGreeting == true)
        {
            // The native full greeting stages and plays its own entrance.
            __result = Task.CompletedTask;
            return false;
        }
        if (SmokeController.Current is not { PreviewEntranceVariant: { } variant } driver) return true;
        driver.ObserveEventEntrance(player);
        __result = AncientEntranceAnimation.Play(player, variant);
        return false;
    }
}
