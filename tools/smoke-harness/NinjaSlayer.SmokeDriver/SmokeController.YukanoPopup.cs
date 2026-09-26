using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Migrations;
using MegaCrit.Sts2.Core.Saves.Test;
using NinjaSlayer.Monsters;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task VerifyYukanoPopupNextCombat(RunState run, MapPoint previous)
    {
        var next = run.Map.GetAllMapPoints().First(p => p.PointType == MapPointType.Monster && p.coord != previous.coord);
        await RunManager.Instance.EnterMapCoord(next.coord);
        await WaitUntilAsync(() => CombatManager.Instance.IsInProgress, "Next popup test combat did not start", CancellationToken.None);
        var owner = LocalContext.GetMe(run)!;
        await WaitUntilAsync(() => owner.PlayerCombatState?.Phase == PlayerTurnPhase.Play, "Next player turn did not start", CancellationToken.None);
        Type type = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.YukanoArrowPopup", true)!;
        object slot = AccessTools.Field(type, "_runData").GetValue(null)!;
        object data = AccessTools.Method(slot.GetType(), "Get").Invoke(slot, [run])!;
        Require((bool)AccessTools.Property(data.GetType(), "Shown").GetValue(data)!, "Next combat lost the popup flag.");
        var actor = owner.PlayerCombatState!.Pets.FirstOrDefault(p => p.Monster is YukanoMonster && p.IsAlive)
            ?? await PlayerCmd.AddPet<YukanoMonster>(owner);
        var target = CombatManager.Instance.DebugOnlyGetState()!.HittableEnemies.First();
        var move = (MoveState)actor.Monster!.MoveStateMachine!.States[YukanoMonster.ArrowMoveId];
        int hp = target.CurrentHp;
        decimal block = target.Block;
        await move.PerformMove([target]);
        Require(target.CurrentHp < hp || target.Block < block, "Cross-combat arrow did not deal real damage.");
        Require(NCombatRoom.Instance!.GetNodeOrNull<Node>("YukanoArrowPopup") == null, "Popup replayed across combat.");
        _checkpoints.Write("yukano-popup.next-combat", data: new JsonObject { ["shown"] = true });
    }

    private sealed partial class TheaterRuntime
    {
        private async Task PopupCheck(string mode)
        {
            Type type = ProductType("NinjaSlayer.Code.ExternalAnimations.YukanoArrowPopup");
            object slot = AccessTools.Field(type, "_runData").GetValue(null)!;
            RunState run = (RunState)_player.RunState;
            bool Shown(RunState state)
            {
                object value = Call(slot, "Get", state)!;
                return (bool)AccessTools.Property(value.GetType(), "Shown").GetValue(value)!;
            }
            if (mode is "unused" or "used")
                Require(Shown(run) == (mode == "used"), $"Popup flag did not match {mode}.");
            else if (mode == "save")
            {
                var files = new MockGodotFileIo("user://popup-save-contract");
                var saves = new RunSaveManager(1, files, new MigrationManager(files), forceSynchronous: true);
                await saves.SaveRun(RunManager.Instance.ToSave(null), false);
                var loaded = saves.LoadRunSave();
                Require(loaded.Success, "Popup native save did not reload.");
                RunState restored = RunState.FromSerializable(loaded.SaveData!);
                Require(Shown(restored), "Run reload lost the shared popup flag.");
                string path = RunSaveManager.GetRunSavePath(1, RunSaveManager.runSaveFileName);
                string json = files.ReadFile(path)!;
                System.IO.File.WriteAllText(System.IO.Path.Combine(_directory, "popup-run.save"), json);
                JsonNode old = JsonNode.Parse(json)!;
                old["_ritsulib"]!["run_saved_data"]!["NinjaSlayer"]!.AsObject().Remove("yukano_arrow_popup");
                files.WriteFile(path, old.ToJsonString());
                var legacy = saves.LoadRunSave();
                Require(legacy.Success && !Shown(RunState.FromSerializable(legacy.SaveData!)), "Old run should default to unshown.");
            }
            else if (mode is "pause-cancel" or "target-invalid")
            {
                Require(!Shown(run), "Pause/cancel test needs a fresh run.");
                var actor = Actor("yukano");
                var target = Actor("enemy");
                int hp = target.CurrentHp;
                var move = (MoveState)actor.Monster!.MoveStateMachine!.States[YukanoMonster.ArrowMoveId];
                Task attack = move.PerformMove([target]);
                Node? popup = null;
                for (int i = 0; i < 600; i++)
                {
                    await _driver.WaitFrames(1);
                    popup = _room.GetNodeOrNull<Node>("YukanoArrowPopup");
                    if (popup != null && (double)AccessTools.Property(type, "PlaybackPosition").GetValue(popup)! > .25) break;
                }
                Require(popup != null, "Popup never started.");
                double Position() => (double)AccessTools.Property(type, "PlaybackPosition").GetValue(popup)!;
                if (mode == "target-invalid") await ReplaceEnemy("ThievingHopper");
                else
                {
                    AccessTools.Property(typeof(CombatManager), "IsPaused").SetValue(CombatManager.Instance, true);
                    try
                    {
                        await _driver.WaitFrames(2);
                        double before = Position();
                        await _driver.WaitFrames(30);
                        Require(Math.Abs(Position() - before) < .001 && target.CurrentHp == hp,
                            "Paused popup advanced or dealt premature damage.");
                    }
                    finally { AccessTools.Property(typeof(CombatManager), "IsPaused").SetValue(CombatManager.Instance, false); }
                    InvokeMethod(ProductType("NinjaSlayer.Code.ExternalAnimations.NinjaSlayerRapidAnimationCoordinator"),
                        null, "CancelAndRestore", actor);
                }
                await attack;
                await _driver.WaitFrames(4);
                Require(target.CurrentHp == hp && _room.GetNodeOrNull<Node>("YukanoArrowPopup") == null && Shown(run),
                    "Cancellation must consume the visible popup, clean up and not fire.");
            }
            else if (mode == "setup-finisher") await CreatureCmd.SetCurrentHp(Actor("enemy"), 15);
            else if (mode == "finished") Require(Actor("enemy").IsDead && Shown(run), "Arrow finisher did not complete.");
            else if (mode == "reentry")
            {
                Require(Shown(run), "Reentry test requires the first popup.");
                Node("yukano").Hide();
                InvokeMethod(ProductType("NinjaSlayer.Code.Combat.CompanionIntentLifecycle"), null, "Retire", Actor("yukano"));
                _actors.Remove("yukano");
                await Entrance("yukano");
                await Move(new() { Actor = "yukano", Move = YukanoMonster.ArrowMoveId });
                Require(_room.GetNodeOrNull<Node>("YukanoArrowPopup") == null, "Companion reentry replayed the popup.");
            }
            else throw new InvalidDataException($"Unknown popup check: {mode}");
            _driver._checkpoints.Write("yukano-popup." + mode, data: new JsonObject { ["shown"] = Shown(run) });
        }
    }
}
