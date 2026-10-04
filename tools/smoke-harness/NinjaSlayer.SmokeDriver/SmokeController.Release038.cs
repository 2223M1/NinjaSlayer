using Godot;
using HarmonyLib;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.AutoSlay;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Encounters;
using NinjaSlayer.Monsters;
using NinjaSlayer.Powers;
using NinjaSlayer.Relics;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task RunRelease038Async()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
        STS2RitsuLib.Data.ModDataStore.For("NinjaSlayer")
            .Get<NinjaSlayerSettingsData>("ninja_slayer_settings").BriefBossGreetingEnabled = true;
        var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<NinjaSlayerCharacter>(),
            true, ActModel.GetDefaultList(), [], _configuration.Seed, GameMode.Standard, 0);
        await RunManager.Instance.EnterAct(0);
        await RunManager.Instance.EnterMapCoord(run.Map.GetAllMapPoints().First(p => p.PointType == MapPointType.Monster).coord);
        var player = LocalContext.GetMe(run)!;
        await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play, "0.3.8 fixture did not start.");
        var choice = new BlockingPlayerChoiceContext();
        var actor = player.Creature.GetCreatureNode()!;
        Vector2 scale = actor.Scale;
        var probe = new Harmony("NinjaSlayer.SmokeDriver.Release038");
        probe.Patch(AccessTools.Method(typeof(ByrdFallAnimation), "PlayLandingImpact"),
            prefix: new HarmonyMethod(typeof(Release038Probe), nameof(Release038Probe.Landed)));
        probe.Patch(AccessTools.Method(typeof(VfxCmd), nameof(VfxCmd.PlayOnSide)),
            prefix: new HarmonyMethod(typeof(Release038Probe), nameof(Release038Probe.SideVfx)));
        try
        {
            await VerifySpinAudioAsync(player);
            foreach (int form in new[] { 0, 1, 2, 3 })
            {
                if (form == 1) await PowerCmd.Apply<NarakuFormPower>(choice, player.Creature, 1, player.Creature, null);
                if (form == 2) await RelicCmd.Obtain<NarakuUnleashedRelic>(player);
                if (form == 3) await PowerCmd.Apply<OneMindOneBodyPower>(choice, player.Creature, 3, player.Creature, null);
                await WaitFrames(3);
                foreach (float size in new[] { .8f, 1.3f })
                {
                    actor.Scale = scale * size;
                    await WaitFrames(2);
                    Vector2 position = actor.Position;
                    Release038Probe.Landing = () =>
                    {
                        Sprite2D source = actor.Visuals.GetNode<Sprite2D>("%Visuals");
                        var overlay = (Sprite2D)actor.Visuals.FindChild("NarakuVisualOverlay", true);
                        Sprite2D shown = overlay.Visible ? overlay : source;
                        Vector2 head = form switch
                        {
                            2 => new(47.989f, -324.22532f),
                            3 => new(-353.442f, -1325.026f),
                            _ => new(628f, -281f)
                        };
                        if (shown.FlipH) head.X = -head.X;
                        if (shown.FlipV) head.Y = -head.Y;
                        float headY = (shown.GetGlobalTransformWithCanvas() * (head + shown.Offset)).Y;
                        float groundY = actor.Visuals.GetNode<Marker2D>("GroundContact").GetGlobalTransformWithCanvas().Origin.Y;
                        _checkpoints.Write("release038.head-contact", data: new JsonObject
                        { ["form"] = form, ["scale"] = size, ["gap"] = headY - groundY });
                        Require(Mathf.Abs(headY - groundY) < 2f, "Inverted entrance did not land on its head.");
                    };
                    await AncientEntranceAnimation.Play(player, AncientEntranceAnimation.EntranceVariant.InvertedFallFromTopLeft);
                    Release038Probe.Landing = null;
                    Require(actor.Position.IsEqualApprox(position) && actor.Scale.IsEqualApprox(scale * size),
                        "Inverted entrance changed external scale or final position.");
                    await AncientEntranceAnimation.Play(player, AncientEntranceAnimation.EntranceVariant.WallSlideFromLeft);
                    Require(actor.Position.IsEqualApprox(position) && actor.Scale.IsEqualApprox(scale * size),
                        "Wall entrance changed external scale or final position.");
                }
            }
            actor.Scale = scale;
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<DarkNinjaEncounter>().ToMutable());
            await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play, "Dark Ninja greeting did not release the turn.");
            var dark = NCombatRoom.Instance!.CreatureNodes.Single(n => n.Entity.Monster is DarkNinjaMonster).Entity;
            player.Creature.SetMaxHpInternal(5000);
            player.Creature.SetCurrentHpInternal(5000);
            var monster = (DarkNinjaMonster)dark.Monster!;
            string[] moves = [DarkNinjaMonster.CounterStanceMoveId, DarkNinjaMonster.DarkStrikeMoveId,
                DarkNinjaMonster.DeathSlashMoveId, DarkNinjaMonster.DarkRobeMoveId,
                DarkNinjaMonster.DarkStrikeMoveId, DarkNinjaMonster.DeathSlashMoveId, DarkNinjaMonster.DarkRobeMoveId];
            foreach (string move in moves)
            {
                Require(monster.NextMove.Id == move, "Live Dark Ninja intent order differs.");
                int vfx = Release038Probe.Slashes;
                await monster.PerformMove();
                Require(dark.GetPowerAmount<StrengthPower>() == 10, "Dark Ninja gained Strength outside its opener.");
                Require(Release038Probe.Slashes - vfx == (move == DarkNinjaMonster.DeathSlashMoveId ? 1 : 0),
                    "Death Slash must emit one native side-center VFX.");
                monster.RollMove([player.Creature]);
                _checkpoints.Write("release038.dark-move", data: new JsonObject { ["move"] = move, ["hp"] = player.Creature.CurrentHp });
            }
            await PowerCmd.Apply<EvasionPower>(choice, player.Creature, 99, player.Creature, null);
            await PlayerCmd.AddPet<YamotoKokiMonster>(player);
            monster.SetMoveImmediate((MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine.MoveState)
                monster.MoveStateMachine!.States[DarkNinjaMonster.DeathSlashMoveId], true);
            int before = Release038Probe.Slashes;
            int hp = player.Creature.CurrentHp;
            await monster.PerformMove();
            Require(Release038Probe.Slashes == before + 1 && player.Creature.CurrentHp == hp,
                "A fully evaded Death Slash with an ally must retain one side-center swing without HP loss.");
            _checkpoints.Write("release038.dark-evaded-with-ally");
            await RunManager.Instance.EnterAct(run.Acts.Count - 1);
            await CardPileCmd.RemoveFromDeck(player.Deck.Cards.ToArray(), showPreview: false);
            await CardPileCmd.Add(run.CreateCard(ModelDb.Card<NinjaSlayer.Cards.Standard.AlabamaDrop>(), player),
                MegaCrit.Sts2.Core.Entities.Cards.PileType.Deck);
            await RelicCmd.Obtain<NSTVPressPassRelic>(player);
            await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<TheArchitect>());
            var model = ((EventRoom)run.CurrentRoom!).LocalMutableEvent;
            await WaitUntilAsync(() => model.CurrentOptions.Count > 0, "Architect options missing.");
            var architect = (TheArchitect)model;
            var dialogues = architect.DialogueSet.CharacterDialogues[player.Character.Id.Entry];
            Require(dialogues.Count == 4, "Architect dialogue was registered more than once.");
            for (int visits = 0; visits < 9; visits++)
            {
                var valid = architect.DialogueSet.GetValidDialogues(player.Character.Id, visits, visits, false).ToArray();
                Require(valid.Length > 0 && valid.All(d => d.Lines.All(l => l.LineText is { } text && text.Exists()
                    && !text.GetFormattedText().Contains("THE_ARCHITECT"))),
                    "Architect dialogue contains an unresolved key at a later/repeated visit.");
            }
            _checkpoints.Write("release111.architect-dialogue", data: new JsonObject { ["sequences"] = dialogues.Count, ["visitsChecked"] = 9 });

            Require(NCombatRoom.Instance!.GetNodeOrNull("NinjaSlayerArchitectExecution") == null,
                "Architect executed during opening dialogue.");
            Require(model.CurrentOptions.All(option => option.TextKey != "NINJA_SLAYER_REPORTER_PASS_RECORD"),
                "Press Pass must not bypass the Architect victory dialogue.");
            await model.CurrentOptions.Single().Chosen();
            // Native options appear before the separate room entrance finishes.
            // Sample execution UI from its standing layout, not the entrance slide.
            await Cmd.Wait(1f);
            int wins = SaveManager.Instance.Progress.Wins;
            Func<bool> autoslay = NonInteractiveMode.AutoSlayerCheck;
            NonInteractiveMode.AutoSlayerCheck = static () => false;
            try
            {
                probe.Patch(AccessTools.Method(typeof(ArchitectExecutionCinematic), "ExitScene"),
                    prefix: new HarmonyMethod(typeof(Release038Probe), nameof(Release038Probe.Exiting)));
                Release038Probe.Exit = () =>
                {
                    var owner = player.Creature.GetCreatureNode()!;
                    Require(owner.Visuals.GlobalPosition.X - owner.GlobalPosition.X > 100f,
                        "Architect Alabama recovery snapped the body back to its layout root before walking off.");
                };
                using var architectUi = new FinisherUiProbe(player.Creature.GetCreatureNode()!);
                Require(model.CurrentOptions.All(option => option.TextKey != "NINJA_SLAYER_REPORTER_PASS_RECORD"),
                    "Press Pass reappeared on the final Architect Continue.");
                await model.CurrentOptions.Single().Chosen();
                await WaitUntilAsync(() => SaveManager.Instance.Progress.Wins > wins,
                    "Architect Continue did not finish the run.");
                await WaitFrames(3);
                architectUi.Verify();
                _checkpoints.Write("finisher.architect-stationary-ui.completed");
            }
            finally { NonInteractiveMode.AutoSlayerCheck = autoslay; }
            Require(SaveManager.Instance.Progress.Wins == wins + 1, "Architect victory did not complete exactly once.");
            _checkpoints.Write("release038.architect-continue-victory");
            _checkpoints.Write("release109.press-pass-architect-victory");
        }
        finally
        {
            Release038Probe.Landing = null;
            Release038Probe.Exit = null;
            probe.UnpatchAll(probe.Id);
        }
        _checkpoints.Write("release038.completed");
        NGame.Instance.Quit();
    }
}

internal static class Release038Probe
{
    internal static Action? Exit;
    public static void Exiting() => Exit?.Invoke();
    internal static Action? Landing;
    internal static int Slashes;
    public static void Landed() => Landing?.Invoke();
    public static void SideVfx(CombatSide side, string path)
    {
        if (path == VfxCmd.giantHorizontalSlashPath && side == CombatSide.Player) Slashes++;
    }
}
