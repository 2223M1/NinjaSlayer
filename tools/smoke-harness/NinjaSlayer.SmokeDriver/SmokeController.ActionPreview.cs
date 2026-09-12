using System.Diagnostics;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.AutoSlay;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task ExecuteActionPreviewAsync(CancellationToken cancellationToken)
    {
        Func<bool> autoSlayerCheck = NonInteractiveMode.AutoSlayerCheck;
        var sections = new JsonArray();
        string directory = _configuration.ActionPreviewDirectory
            ?? throw new InvalidOperationException("Action preview requires an output directory.");
        Directory.CreateDirectory(directory);
        var recorder = new TornadoViewportRecording(_tree.Root, directory);
        bool recording = false;
        try
        {
            await WaitUntilAsync(() => CombatManager.Instance.IsInProgress, "Action preview combat did not start", cancellationToken);
            ICombatState combat = CombatManager.Instance.DebugOnlyGetState()!;
            var player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())!;
            await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
                "Action preview player phase did not start", cancellationToken);
            NonInteractiveMode.AutoSlayerCheck = static () => false;
            SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
            SaveManager.Instance.PrefsSave.MuteInBackground = false;
            await WaitFrames(240);
            VerifyPreviewModelIds();
            FindDescendant<NPlayerTurnBanner>(_tree.Root)?.QueueFree();
            foreach (Node toast in _tree.Root.FindChildren("*Toast*", "", true, false))
                if (toast is CanvasItem item) item.Hide();
            if (_configuration.PreviewMenuReturnCheck)
            {
                await recorder.Start();
                recording = true;
                await WaitFrames(60);
                await NGame.Instance!.ReturnToMainMenu();
                await WaitFrames(60);
                Require(NGame.Instance.MainMenu != null, "Native return to main menu did not finish.");
                _checkpoints.Write("preview.menu-return-completed");
                await recorder.Stop();
                recording = false;
                _firstCombatCompleted.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return;
            }
            if (_configuration.PreviewCleanupBaseline)
            {
                await recorder.Start();
                recording = true;
                await WaitFrames(60);
                await recorder.Stop();
                recording = false;
                _firstCombatCompleted.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return;
            }
            foreach (Creature enemy in combat.HittableEnemies)
            {
                enemy.SetMaxHpInternal(1000);
                await CreatureCmd.SetCurrentHp(enemy, 1000);
            }
            player.Creature.SetMaxHpInternal(1000);
            await CreatureCmd.SetCurrentHp(player.Creature, 1000);
            var choice = new BlockingPlayerChoiceContext();
            Creature target = combat.HittableEnemies.First();
            NCreature actor = NCombatRoom.Instance!.GetCreatureNode(player.Creature)!;
            Node2D pose = actor.Visuals.GetNode<Node2D>("%AimPose");
            AccessTools.Property(pose.GetType(), "UseTornadoHitStop").SetValue(pose, true);
            await PowerCmd.Apply<KaratePower>(choice, player.Creature, 2, player.Creature, null);
            await (Task)AccessTools.Method(typeof(ShurikenOrb), "AddStock").Invoke(null, [choice, player, 6])!;
            for (int i = 0; i < 8; i++)
                await CardPileCmd.Add(combat.CreateCard<StrikeNinjaSlayerRedesignV1>(player), PileType.Draw);
            await WaitFrames(40);
            await recorder.Start();
            recording = true;
            long start = recorder.StartTimestamp;
            void Section(string name)
            {
                AutoSlayer.CurrentWatchdog?.Reset(name);
                sections.Add(new JsonObject { ["name"] = name, ["seconds"] = Stopwatch.GetElapsedTime(start).TotalSeconds });
                _checkpoints.Write("action.preview-section", data: new JsonObject { ["name"] = name });
            }
            async Task Play<T>(Creature? selected = null) where T : CardModel
            {
                await PlayerCmd.SetEnergy(6, player);
                CardModel card = combat.CreateCard<T>(player);
                await CardPileCmd.Add(card, PileType.Hand);
                await CardCmd.AutoPlay(choice, card, selected ?? target);
            }
            Vector2 root = actor.Position;
            Section("draw-attack-throw");
            await WaitFrames(60);
            await Play<RedBlackFlameAttackRedesignV1>(player.Creature);
            await Play<StrikeNinjaSlayerRedesignV1>();
            await CardCmd.Discard(choice, PileType.Hand.GetPile(player).Cards.Take(2).ToArray());
            await Play<RoundhouseKickRedesignV1>();
            await WaitFrames(60);
            Require(actor.Position.IsEqualApprox(root), "Draw/throw/kick moved the combat root.");
            Require(pose.Transform.IsEqualApprox(Transform2D.Identity), "Draw/throw/kick did not recover.");

            Section("hurt-block-native-debuff");
            await CreatureCmd.Damage(choice, player.Creature, 8, ValueProp.Move, target);
            await Play<StrikeNinjaSlayerRedesignV1>();
            await WaitFrames(30);
            await CreatureCmd.GainBlock(player.Creature, 20, ValueProp.Unpowered, null);
            await CreatureCmd.Damage(choice, player.Creature, 8, ValueProp.Move, target);
            await WaitFrames(30);
            await PowerCmd.Apply<WeakPower>(choice, player.Creature, 1, target, null);
            await WaitFrames(90);
            Require(actor.Position.IsEqualApprox(root), "Hurt/block/debuff moved the combat root.");
            Require(pose.Transform.IsEqualApprox(Transform2D.Identity), "Hurt/block/debuff did not recover.");

            Section("tornado-alabama-attack");
            await Play<TornadoFistRedesignV1>();
            await Play<AlabamaDropRedesignV1>();
            await Play<StrikeNinjaSlayerRedesignV1>();
            await WaitFrames(90);
            Require(actor.Position.IsEqualApprox(root), "Alabama handoff moved the combat root.");
            Require(pose.Transform.IsEqualApprox(Transform2D.Identity), "Alabama handoff did not recover.");

            Section("finisher");
            foreach (Creature enemy in combat.HittableEnemies.ToArray()) await CreatureCmd.SetCurrentHp(enemy, 1);
            await Play<RoundhouseKickRedesignV1>();
            await WaitFrames(120);

            Section("architect");
            await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<TheArchitect>(), showTransition: false);
            await WaitFrames(720);
            _checkpoints.Write("action.preview-completed");
            await recorder.Stop();
            recording = false;
            File.WriteAllText(Path.Combine(directory, "sections.json"), sections.ToJsonString());
            _firstCombatCompleted.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (Exception exception)
        {
            _firstCombatCompleted.TrySetException(exception);
            throw;
        }
        finally
        {
            if (recording) await recorder.Stop();
            File.WriteAllText(Path.Combine(directory, "sections.json"), sections.ToJsonString());
            NonInteractiveMode.AutoSlayerCheck = autoSlayerCheck;
        }
    }

    private void VerifyPreviewModelIds()
    {
        var types = AbstractModelSubtypes.All.ToList();
        foreach (Mod mod in ModManager.Mods.Where(mod => mod.state == ModLoadState.Loaded))
        {
#if NINJASLAYER_CHANNEL_PREVIEW
            foreach (var assembly in mod.assemblies)
                types.AddRange(ReflectionHelper.GetSubtypesFromAssembly(assembly, typeof(AbstractModel)));
#else
            if (mod.assembly != null)
                types.AddRange(ReflectionHelper.GetSubtypesFromAssembly(mod.assembly, typeof(AbstractModel)));
#endif
        }
        Require(types.Distinct().Count() == types.Count, "Host model-ID sorting input contains a repeated type.");
        Require(types.Select(ModelDb.GetId).Distinct().Count() == types.Count, "Host model-ID sorting input contains duplicate IDs.");
        _checkpoints.Write("preview.model-ids-unique", data: new JsonObject { ["modelCount"] = types.Count });
    }
}
