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
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;
using NinjaSlayer.Relics;

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
        Action? sampleMotion = null;
        var motionFrames = new JsonArray();
        try
        {
            await WaitUntilAsync(() => CombatManager.Instance.IsInProgress, "Action preview combat did not start", cancellationToken);
            ICombatState combat = CombatManager.Instance.DebugOnlyGetState()!;
            var player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())!;
            await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
                "Action preview player phase did not start", cancellationToken);
            NonInteractiveMode.AutoSlayerCheck = static () => false;
            SaveManager.Instance.PrefsSave.FastMode = FastModeType.Fast;
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
            var room = NCombatRoom.Instance!;
            if (_configuration.PreviewFormFinisher is "BattleCompletion" or "EventRelics" or "MotherUnix" or "BalanceV0216" or "BalanceV0217" or "MusicV0217")
            {
                await recorder.Start();
                recording = true;
                if (_configuration.PreviewFormFinisher == "BalanceV0217")
                    await VerifyBalanceV0217Live(directory, cancellationToken);
                else if (_configuration.PreviewFormFinisher == "MusicV0217")
                    await VerifyMusicV0217Live(directory, cancellationToken);
                else if (_configuration.PreviewFormFinisher == "BalanceV0216")
                    await VerifyBalanceV0216Live(directory, cancellationToken);
                else if (_configuration.PreviewFormFinisher == "MotherUnix")
                    await VerifyMotherUnixLive(directory, cancellationToken);
                else if (_configuration.PreviewFormFinisher == "EventRelics")
                    await VerifyEventRelicsLive(directory, cancellationToken);
                else
                    await VerifyBattleCompletion(directory, cancellationToken);
                await recorder.Stop();
                recording = false;
                _firstCombatCompleted.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return;
            }
            if (IsBladeFeedbackPreview || _configuration.PreviewFormFinisher is "SawatariCleanup" or "SawatariWeapons" or "SawatariRig" or "SawatariBow" or "SawatariEvent" or "SawatariMotion")
            {
                await recorder.Start();
                recording = true;
                await VerifySawatariWeaponPresentation(directory, cancellationToken);
                await recorder.Stop();
                recording = false;
                _firstCombatCompleted.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return;
            }
            async Task<Creature> ReplaceEnemy<T>() where T : MonsterModel
            {
                Creature[] previous = combat.HittableEnemies.ToArray();
                Vector2 position = room.GetCreatureNode(previous[0])!.Position;
                Creature replacement = combat.CreateCreature(ModelDb.Monster<T>().ToMutable(), CombatSide.Enemy, null);
                await CreatureCmd.Add(replacement);
                foreach (Creature enemy in previous)
                {
                    NCreature node = room.GetCreatureNode(enemy)!;
                    node.Hide();
                    room.RemoveCreatureNode(node);
                    node.QueueFree();
                    CombatManager.Instance.RemoveCreature(enemy);
                    if (combat.ContainsCreature(enemy)) combat.RemoveCreature(enemy);
                }
                room.GetCreatureNode(replacement)!.Position = position;
                replacement.SetMaxHpInternal(1500);
                await CreatureCmd.SetCurrentHp(replacement, 1500);
                return replacement;
            }
            Creature target = await ReplaceEnemy<SawatariMonster>();
            var kokiRelic = await RelicCmd.Obtain<YamotoKokiCuteRelic>(player);
            await kokiRelic.BeforeCombatStart();
            var yukanoRelic = await RelicCmd.Obtain<YukanoCompanionRelic>(player);
            await yukanoRelic.BeforeCombatStart();
            Creature koki = player.PlayerCombatState!.Pets.Single(p => p.Monster is YamotoKokiMonster);
            Creature yukano = player.PlayerCombatState.Pets.Single(p => p.Monster is YukanoMonster);
            player.Creature.SetMaxHpInternal(500);
            await CreatureCmd.SetCurrentHp(player.Creature, 500);
            var choice = new BlockingPlayerChoiceContext();
            NCreature actor = room.GetCreatureNode(player.Creature)!;
            Node2D pose = actor.Visuals.GetNode<Node2D>("%AimPose");
            AccessTools.Property(pose.GetType(), "UseTornadoHitStop").SetValue(pose, true);
            await PowerCmd.Apply<KaratePower>(choice, player.Creature, 2, player.Creature, null);
            await (Task)AccessTools.Method(typeof(ShurikenOrb), "AddStock").Invoke(null,
                [choice, player, _configuration.PreviewFormFinisher == "ShurikenInertia" ? 3 : 6])!;
            for (int i = 0; i < 40; i++)
                await CardPileCmd.Add(combat.CreateCard<StrikeNinjaSlayerRedesignV1>(player), PileType.Draw, skipVisuals: true);
            NRunMusicController.Instance?.PlayCustomMusic(NinjaSlayerAudio.ForestSawatariBattleMusicEvent);
            await WaitFrames(120);
            await recorder.Start();
            recording = true;
            long start = recorder.StartTimestamp;
            void Section(string name)
            {
                AutoSlayer.CurrentWatchdog?.Reset(name);
                sections.Add(new JsonObject { ["name"] = name, ["seconds"] = Stopwatch.GetElapsedTime(start).TotalSeconds });
                _checkpoints.Write("action.preview-section", data: new JsonObject { ["name"] = name });
            }
            async Task Play<T>(Creature? selected = null, int energy = 6, bool charge = false) where T : CardModel
            {
                if (PileType.Hand.GetPile(player).Cards.Count >= 9)
                    await CardCmd.Discard(choice, PileType.Hand.GetPile(player).Cards.Skip(4).ToArray());
                await PlayerCmd.SetEnergy(energy, player);
                CardModel card = combat.CreateCard<T>(player);
                await CardPileCmd.Add(card, PileType.Draw, skipVisuals: true);
                if (charge)
                {
                    var drag = new Node();
                    actor.AddChild(drag);
                    AccessTools.Method(pose.GetType(), "Drag").Invoke(pose,
                        [drag, card, room.GetCreatureNode(target)!.VfxSpawnPosition, target]);
                    await WaitFrames(12);
                    AccessTools.Method(pose.GetType(), "EndDrag").Invoke(pose, [drag, true]);
                    drag.QueueFree();
                }
                int beforeDamage = target.CurrentHp;
                Task playing = CardCmd.AutoPlay(choice, card,
                    selected ?? (card.TargetType == TargetType.Self ? player.Creature : target));
                if (card is DragonFlyingKickRedesignV1)
                {
                    long? damageAt = null;
                    int heldFrames = 0;
                    while (!playing.IsCompleted)
                    {
                        await WaitFrames(1);
                        if (target.CurrentHp < beforeDamage) damageAt ??= Stopwatch.GetTimestamp();
                        Vector2 travel = (Vector2)AccessTools.Property(pose.GetType(), "Travel").GetValue(pose)!;
                        float kick = (float)AccessTools.Field(pose.GetType(), "_kick").GetValue(pose)!;
                        if (damageAt != null && !playing.IsCompleted)
                        {
                            Require(Math.Abs(travel.Length() - 120f) < .5f && kick > .99f,
                                $"Flying Kick returned before draw/Hooks completed: travel={travel}, kick={kick}.");
                            heldFrames++;
                        }
                    }
                    await playing;
                    Require(heldFrames > 0, "Flying Kick preview did not exercise drawing at its peak.");
                    _checkpoints.Write("action.flying-kick-held-during-draw", data: new JsonObject
                    {
                        ["heldFrames"] = heldFrames,
                        ["secondsAfterDamage"] = Stopwatch.GetElapsedTime(damageAt!.Value).TotalSeconds,
                        ["hand"] = PileType.Hand.GetPile(player).Cards.Count
                    });
                }
                else await playing;
            }
            async Task Move(Creature creature, string id, Creature victim)
            {
                MonsterModel monster = creature.Monster!;
                if (monster is SawatariMonster)
                {
                    await (Task)AccessTools.Method(typeof(SawatariMonster), "PlayAttack").Invoke(monster, [victim])!;
                    return;
                }
                var move = (MoveState)monster.MoveStateMachine!.States[id];
                monster.SetMoveImmediate(move, forceTransition: true);
                await move.PerformMove([victim]);
            }
            async Task RollAndShoot(int rounds)
            {
                CardModel[] surplus = PileType.Hand.GetPile(player).Cards.Skip(3).ToArray();
                if (surplus.Length > 0) await CardCmd.Discard(choice, surplus);
                await (Task)AccessTools.Method(typeof(ShurikenOrb), "AddStock")
                    .Invoke(null, [choice, player, rounds * 2])!;
                for (int i = 0; i < rounds; i++)
                {
                    await CardPileCmd.Draw(choice, 2, player);
                    await WaitFrames(4);
                    await CardCmd.Discard(choice, PileType.Hand.GetPile(player).Cards.TakeLast(2).ToArray());
                }
                _checkpoints.Write("action.roll-volley", data: new JsonObject
                {
                    ["rounds"] = rounds,
                    ["stock"] = player.PlayerCombatState.OrbQueue.Orbs.OfType<ShurikenOrb>().FirstOrDefault()?.StackCount ?? 0,
                    ["hand"] = PileType.Hand.GetPile(player).Cards.Count
                });
            }
            Vector2 root = actor.Position;
            if (_configuration.PreviewFormFinisher is { } showcase)
            {
                sampleMotion = () =>
                {
                    if (!recording || !GodotObject.IsInstanceValid(actor)) return;
                    NCreature? enemyNode = target.GetCreatureNode();
                    NCreature? kokiNode = koki.GetCreatureNode();
                    Sprite2D overlay = actor.Visuals.GetNode<Sprite2D>("AirborneAnchor/AimPose/NarakuVisualOverlay");
                    Sprite2D body = overlay.Visible ? overlay : actor.Visuals.GetNode<Sprite2D>("%Visuals");
                    ShurikenOrbVisual? held = FindDescendant<ShurikenOrbVisual>(actor);
                    int shards = room.CombatVfxContainer.GetChildren().OfType<Node2D>()
                        .Where(node => node.Name.ToString().StartsWith("FormShatter", StringComparison.Ordinal))
                        .Sum(node => node.GetChildCount());
                    motionFrames.Add(new JsonObject
                    {
                        ["seconds"] = Stopwatch.GetElapsedTime(start).TotalSeconds,
                        ["speed"] = SaveManager.Instance.PrefsSave.FastMode.ToString(),
                        ["actorRootX"] = actor.Position.X, ["bodyX"] = body.GlobalPosition.X,
                        ["bodyY"] = body.GlobalPosition.Y, ["bodyScale"] = body.Scale.X,
                        ["form"] = body.Texture.ResourcePath, ["shards"] = shards,
                        ["enemyHp"] = target.CurrentHp, ["playerHp"] = player.Creature.CurrentHp,
                        ["shurikenStock"] = player.PlayerCombatState.OrbQueue.Orbs.OfType<ShurikenOrb>().FirstOrDefault()?.StackCount ?? 0,
                        ["shurikenVisible"] = held?.IsVisibleInTree() ?? false,
                        ["shurikenAngle"] = held?.GetNode<Sprite2D>("DeformedVisuals/Art/Body").RotationDegrees,
                        ["shurikenSpeed"] = held == null ? 0f : (float)AccessTools.Field(typeof(ShurikenOrbVisual), "_angularSpeed").GetValue(held)!,
                        ["enemyRootX"] = enemyNode?.Position.X, ["enemyVisualX"] = enemyNode?.Visuals.Position.X,
                        ["kokiRootX"] = kokiNode?.Position.X, ["kokiVisualX"] = kokiNode?.Visuals.Position.X,
                        ["kokiScreenX"] = kokiNode?.VfxSpawnPosition.X,
                        ["enemyScreenX"] = enemyNode?.VfxSpawnPosition.X,
                        ["darkReturnScaleX"] = room.SceneContainer.GetNodeOrNull<Node2D>("DarkNinjaDarkStrike/FullBody")?.Scale.X,
                        ["darkReturnBlur"] = room.SceneContainer.GetNodeOrNull<Node2D>("DarkNinjaDarkStrike/EntangledSpinBlur")?.IsVisibleInTree() ?? false,
                        ["stolenCards"] = target.Powers.OfType<SwipePower>().Count()
                    });
                };
                RenderingServer.FramePreDraw += sampleMotion;
                if (showcase != "Forms")
                {
                    Section("native-audio-and-action-reference");
                    await Play<KarateStraightRedesignV1>();
                    await WaitFrames(24);
                    await Play<KarateStraightRedesignV1>();
                    await WaitFrames(24);
                }
                if (showcase == "Forms")
                {
                    Section("native-bludgeon-versus-mod-heavy-small-target");
                    target = await ReplaceEnemy<MegaCrit.Sts2.Core.Models.Monsters.TwigSlimeS>();
                    await Play<MegaCrit.Sts2.Core.Models.Cards.Bludgeon>();
                    await WaitFrames(36);
                    await Play<CollapseFistRedesignV1>();
                    await WaitFrames(36);
                    Section("native-bludgeon-versus-mod-heavy-large-target");
                    target = await ReplaceEnemy<MegaCrit.Sts2.Core.Models.Monsters.SewerClam>();
                    await Play<MegaCrit.Sts2.Core.Models.Cards.Bludgeon>();
                    await WaitFrames(36);
                    await Play<StraightKiRedesignV1>();
                    await WaitFrames(36);
                    Section("native-ground-effect-raised-target");
                    NCreature raised = room.GetCreatureNode(target)!;
                    Vector2 targetRoot = raised.Position;
                    raised.Position += new Vector2(0f, -100f);
                    await Play<MegaCrit.Sts2.Core.Models.Cards.Bludgeon>();
                    await WaitFrames(30);
                    await Play<CollapseFistRedesignV1>();
                    await WaitFrames(30);
                    raised.Position = targetRoot;
                    Section("normal-fingertip-and-shatter-to-half");
                    await WaitFrames(60);
                    await Play<NarakuFormRedesignV1>();
                    await WaitFrames(45);
                    Section("shatter-to-upright-full");
                    var relic = await RelicCmd.Obtain<NarakuWithinRelic>(player);
                    await WaitFrames(60);
                    Section("shatter-to-one-soul-priority");
                    await Play<OneBodyOneSoul>();
                    await WaitFrames(60);
                    await Play<HellTornadoRedesignV1>();
                    await WaitFrames(45);
                    Section("layered-shatter-restores-full-mid-orbit");
                    await PowerCmd.Remove<OneBodyOneSoulPower>(player.Creature);
                    await WaitFrames(60);
                    await Play<StrikeNinjaSlayerRedesignV1>();
                    Section("layered-shatter-restores-normal");
                    await PowerCmd.Remove<NarakuFormRedesignPower>(player.Creature);
                    await RelicCmd.Remove(relic);
                    await PowerCmd.Remove<NarakuLifePower>(player.Creature);
                    await WaitFrames(45);
                    await PowerCmd.Remove<HellTornadoRedesignPower>(player.Creature);
                    await PowerCmd.Remove<SoarPower>(player.Creature);
                    await RollAndShoot(1);
                    await WaitFrames(30);
                    Section("forward-finisher-still-direct-placement");
                    await CreatureCmd.SetCurrentHp(target, 1);
                    await Play<CollapseFistRedesignV1>();
                    await WaitFrames(45);
                }
                else if (showcase == "LayoutSlow")
                {
                    target = await ReplaceEnemy<DarkNinjaMonster>();
                    NRunMusicController.Instance?.PlayCustomMusic(NinjaSlayerAudio.DarkNinjaBattleMusicEvent);
                    await PowerCmd.Remove<EvasionPower>(target);
                    await PowerCmd.Remove<KaratePower>(player.Creature);
                    await PowerCmd.Remove<EvasionPower>(player.Creature);
                    Section("ordinary-slow-feedback-and-koki");
                    await Play<KarateStraightRedesignV1>();
                    await WaitFrames(18);
                    await Move(koki, YamotoKokiMonster.IaiSlashMoveId, target);
                    await WaitFrames(30);
                    Section("half-naraku-matches-normal-fingertip");
                    await Play<NarakuFormRedesignV1>();
                    await WaitFrames(60);
                    Section("full-naraku-centered-wrist-shuriken");
                    var relic = await RelicCmd.Obtain<NarakuWithinRelic>(player);
                    await WaitFrames(90);
                    Section("one-soul-height-versus-dark-ninja-standing");
                    await Play<OneBodyOneSoul>();
                    await WaitFrames(90);
                    await PowerCmd.Remove<OneBodyOneSoulPower>(player.Creature);
                    await PowerCmd.Remove<NarakuFormRedesignPower>(player.Creature);
                    await RelicCmd.Remove(relic);
                    await PowerCmd.Remove<NarakuLifePower>(player.Creature);
                    await WaitFrames(24);
                    Section("dark-counter-after-complete-hurt");
                    await Move(target, DarkNinjaMonster.CounterStanceMoveId, player.Creature);
                    await PowerCmd.Remove<IaiPower>(target);
                    await PowerCmd.Apply<IaiPower>(choice, target, 1, target, null);
                    await Play<StrikeNinjaSlayerRedesignV1>();
                    await WaitFrames(30);
                    Section("flying-kick-holds-while-drawing");
                    await Play<DragonFlyingKickRedesignV1>();
                    await WaitFrames(45);
                    Require(((Vector2)AccessTools.Property(pose.GetType(), "Travel").GetValue(pose)!).Length() < .5f,
                        "Flying Kick did not return after the card completed.");
                    Section("full-naraku-shuriken-launch");
                    await Play<NarakuFormRedesignV1>();
                    relic = await RelicCmd.Obtain<NarakuWithinRelic>(player);
                    await WaitFrames(45);
                    await CardCmd.Discard(choice, PileType.Hand.GetPile(player).Cards.ToArray());
                    await CardPileCmd.Add(PileType.Draw.GetPile(player).Cards.ToArray(), PileType.Discard, skipVisuals: true);
                    await CardPileCmd.Draw(choice, 1, player);
                    await WaitFrames(45);
                }
                else if (showcase == "ShurikenInertia")
                {
                    async Task DiscardOne()
                    {
                        CardModel card = combat.CreateCard<StrikeNinjaSlayerRedesignV1>(player);
                        await CardPileCmd.Add(card, PileType.Hand, skipVisuals: true);
                        await CardCmd.Discard(choice, new[] { card });
                    }
                    Section("held-three-blades");
                    await WaitFrames(60);
                    foreach (int remaining in new[] { 2, 1, 0 })
                    {
                        Section($"single-throw-to-{remaining}-stock");
                        await DiscardOne();
                        await WaitFrames(60);
                    }
                    Section("gain-six-stock-without-spin");
                    await (Task)AccessTools.Method(typeof(ShurikenOrb), "AddStock").Invoke(null, [choice, player, 6])!;
                    await WaitFrames(60);
                    Section("continuous-discards-and-hand-follow");
                    for (int shot = 0; shot < 6; shot++) await DiscardOne();
                    await WaitFrames(45);
                    Section("sweep-one-release-two-targets");
                    Creature second = combat.CreateCreature(ModelDb.Monster<MegaCrit.Sts2.Core.Models.Monsters.TwigSlimeS>().ToMutable(), CombatSide.Enemy, null);
                    await CreatureCmd.Add(second);
                    second.SetMaxHpInternal(1000);
                    await CreatureCmd.SetCurrentHp(second, 1000);
                    await PowerCmd.Apply<BladeSweepPower>(choice, player.Creature, 1, player.Creature, null);
                    await (Task)AccessTools.Method(typeof(ShurikenOrb), "AddStock").Invoke(null, [choice, player, 3])!;
                    await WaitFrames(60);
                    await DiscardOne();
                    await WaitFrames(60);
                    Section("roll-and-consecutive-release");
                    await CardPileCmd.Draw(choice, 2, player);
                    await DiscardOne();
                    await DiscardOne();
                    await WaitFrames(60);
                    Require(!player.PlayerCombatState.OrbQueue.Orbs.OfType<ShurikenOrb>().Any(), "Shuriken preview did not deplete stock.");
                }
                else if (showcase == "IaiTiming")
                {
                    foreach (FastModeType mode in new[] { FastModeType.Normal, FastModeType.Fast })
                    {
                        SaveManager.Instance.PrefsSave.FastMode = mode;
                        Section($"koki-ordinary-iai-{mode}");
                        await Move(koki, YamotoKokiMonster.IaiSlashMoveId, target);
                        await WaitFrames(24);
                        await Move(koki, YamotoKokiMonster.IaiSlashMoveId, target);
                        await WaitFrames(36);
                    }
                    target = await ReplaceEnemy<DarkNinjaMonster>();
                    NRunMusicController.Instance?.PlayCustomMusic(NinjaSlayerAudio.DarkNinjaBattleMusicEvent);
                    await PowerCmd.Remove<EvasionPower>(target);
                    await PowerCmd.Remove<KaratePower>(player.Creature);
                    await PowerCmd.Remove<EvasionPower>(player.Creature);
                    await Move(target, DarkNinjaMonster.CounterStanceMoveId, player.Creature);
                    foreach (FastModeType mode in new[] { FastModeType.Normal, FastModeType.Fast })
                    {
                        SaveManager.Instance.PrefsSave.FastMode = mode;
                        for (int repeat = 0; repeat < 2; repeat++)
                        {
                            await PowerCmd.Remove<IaiPower>(target);
                            await PowerCmd.Apply<IaiPower>(choice, target, 1, target, null);
                            Section($"dark-counter-after-hurt-{mode}-{repeat + 1}");
                            await Play<StrikeNinjaSlayerRedesignV1>();
                            await WaitFrames(30);
                        }
                    }
                }
                else if (showcase == "Koki")
                {
                    Section("koki-ordinary-iai");
                    await Move(koki, YamotoKokiMonster.IaiSlashMoveId, target);
                    await WaitFrames(45);
                    Section("koki-continuous-finisher-approach");
                    await CreatureCmd.SetCurrentHp(target, 1);
                    await Move(koki, YamotoKokiMonster.IaiSlashMoveId, target);
                    await WaitFrames(60);
                }
                else if (showcase == "DarkStrike")
                {
                    target = await ReplaceEnemy<DarkNinjaMonster>();
                    NRunMusicController.Instance?.PlayCustomMusic(NinjaSlayerAudio.DarkNinjaBattleMusicEvent);
                    await Move(target, DarkNinjaMonster.CounterStanceMoveId, player.Creature);
                    await PowerCmd.Remove<IaiPower>(target);
                    await PowerCmd.Remove<EvasionPower>(target);
                    await PowerCmd.Remove<EvasionPower>(player.Creature);
                    if (player.Creature.Block > 0) await RemoveSmokeBlock(player.Creature);
                    foreach (CardModel old in CardPile.GetCards(player, PileType.Draw, PileType.Discard).ToArray())
                        await CardPileCmd.RemoveFromCombat(old);
                    var receipts = new List<CardModel>();
                    foreach (CardModel model in new CardModel[] { ModelDb.Card<PlaceholderBlueDefense01>(),
                        ModelDb.Card<GuardStance>(), ModelDb.Card<PalmThrustRedesignV1>() })
                    {
                        CardModel deck = player.RunState.CreateCard(model, player);
                        CardCmd.Upgrade(deck);
                        await CardPileCmd.Add(deck, PileType.Deck);
                        CardModel copy = combat.CloneCard(deck);
                        copy.DeckVersion = deck;
                        await CardPileCmd.Add(copy, PileType.Draw);
                        receipts.Add(copy);
                    }
                    NCreature darkActor = target.GetCreatureNode()!;
                    Vector2 darkRoot = darkActor.Position;
                    Vector2 intentPosition = darkActor.IntentContainer.GlobalPosition;
                    for (int theft = 0; theft < 3; theft++)
                    {
                        SaveManager.Instance.PrefsSave.FastMode = theft == 1 ? FastModeType.Fast : FastModeType.Normal;
                        Section($"dark-strike-theft-{theft + 1}");
                        if (theft == 1) await PowerCmd.Apply<NarakuLifePower>(choice, player.Creature, 100, player.Creature, null);
                        await Move(target, DarkNinjaMonster.DarkStrikeMoveId, player.Creature);
                        Require(target.Powers.OfType<SwipePower>().Count() == theft + 1, "Live Dark Strike did not steal exactly one card.");
                        Require(darkActor.Position.IsEqualApprox(darkRoot)
                            && darkActor.IntentContainer.GlobalPosition.IsEqualApprox(intentPosition), "Dark Strike moved its UI root.");
                        Require(!player.Creature.HasPower<WeakPower>(), "Live Dark Strike applied Weak.");
                        await WaitFrames(45);
                        _tree.Root.GetTexture().GetImage().SavePng(Path.Combine(directory, $"stolen-{theft + 1}.png"));
                    }
                    Section("dark-strike-empty-candidates");
                    await Move(target, DarkNinjaMonster.DarkStrikeMoveId, player.Creature);
                    Require(target.Powers.OfType<SwipePower>().Count() == 3, "Empty piles produced another stolen card.");
                    Section("dark-strike-interrupted-display");
                    Task interruptedStrike = Move(target, DarkNinjaMonster.DarkStrikeMoveId, player.Creature);
                    await WaitUntilAsync(() => room.SceneContainer.GetNodeOrNull<Node2D>("DarkNinjaDarkStrike") != null,
                        "Dark Strike did not create its detached display", cancellationToken);
                    room.SceneContainer.GetNode<Node2D>("DarkNinjaDarkStrike").Free();
                    await interruptedStrike;
                    await WaitFrames(5);
                    Require(darkActor.FindChildren("StolenCardPos", "Node2D", true, false).Single().GetChildCount() == 3,
                        "Interrupted Dark Strike destroyed the held stolen cards.");
                    Section("dark-strike-native-return-rewards");
                    // Keep combat alive to inspect individual native rewards before the terminal UI opens.
                    Creature survivor = await CreatureCmd.Add<MegaCrit.Sts2.Core.Models.Monsters.DampCultist>(combat);
                    await CreatureCmd.Kill(target, force: true);
                    var rewards = ((CombatRoom)player.RunState.CurrentRoom!).ExtraRewards[player]
                        .OfType<MegaCrit.Sts2.Core.Rewards.SpecialCardReward>().ToArray();
                    Require(rewards.Length == 3 && receipts.All(c => !player.Deck.Cards.Contains(c.DeckVersion!)),
                        "Live death must offer three optional cards, not auto-collect them.");
                    await rewards[0].SelectUnsynchronized();
                    rewards[1].OnSkipped();
                    Require(player.Deck.Cards.Contains(receipts.Single(c => c.Id == rewards[0].ToSerializable().SpecialCard!.Id).DeckVersion!),
                        "The selected native stolen-card reward did not return.");
                    target = survivor;
                    await WaitFrames(90);
                    Section("dark-strike-existing-impact-regression");
                    await PowerCmd.Remove<NarakuLifePower>(player.Creature);
                    await VerifyDarkStrike(combat, player);
                    RenderingServer.FramePreDraw -= sampleMotion;
                    sampleMotion = null;
                    await VerifyDarkStrikeEventRewards(directory, Section, cancellationToken);
                }
                else if (showcase == "Reverse")
                {
                    target = await ReplaceEnemy<DarkNinjaMonster>();
                    NRunMusicController.Instance?.PlayCustomMusic(NinjaSlayerAudio.DarkNinjaBattleMusicEvent);
                    await PowerCmd.Remove<EvasionPower>(target);
                    await PowerCmd.Remove<KaratePower>(player.Creature);
                    await PowerCmd.Remove<EvasionPower>(player.Creature);
                    await Move(target, DarkNinjaMonster.CounterStanceMoveId, player.Creature);
                    await PowerCmd.Remove<IaiPower>(target);
                    await PowerCmd.Apply<IaiPower>(choice, target, 1, target, null);
                    Section("reverse-slow-attack-approach");
                    await CreatureCmd.SetCurrentHp(player.Creature, 1);
                    await Play<StrikeNinjaSlayerRedesignV1>();
                    await WaitFrames(45);
                }
                else throw new InvalidOperationException("Unknown form/finisher preview mode.");
                _checkpoints.Write("action.form-finisher-completed");
                await recorder.Stop();
                recording = false;
                File.WriteAllText(Path.Combine(directory, "sections.json"), sections.ToJsonString());
                File.WriteAllText(Path.Combine(directory, "motion-frames.json"), motionFrames.ToJsonString());
                _firstCombatCompleted.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return;
            }
            if (_configuration.PreviewSpecialHeavy)
            {
                object hellMotion = AccessTools.Property(pose.GetType(), "HellTornado").GetValue(pose)!;
                Node2D hellNode = (Node2D)hellMotion;
                sampleMotion = () =>
                {
                    if (!recording || !GodotObject.IsInstanceValid(pose)) return;
                    float? turn = (float?)AccessTools.Field(pose.GetType(), "_somersault").GetValue(pose);
                    motionFrames.Add(new JsonObject
                    {
                        ["seconds"] = Stopwatch.GetElapsedTime(start).TotalSeconds,
                        ["somersault"] = turn,
                        ["poseRotation"] = pose.GlobalRotation,
                        ["somersaultBlur"] = actor.Visuals.GetNodeOrNull<Node2D>("SomersaultExposure")?.IsVisibleInTree() ?? false,
                        ["hellActive"] = (bool)AccessTools.Property(hellMotion.GetType(), "Active").GetValue(hellMotion)!,
                        ["hellAngle"] = (double)AccessTools.Field(hellMotion.GetType(), "_angle").GetValue(hellMotion)!,
                        ["hellBlur"] = hellNode.GetNode<Node2D>("BodyExposure").IsVisibleInTree()
                    });
                };
                RenderingServer.FramePreDraw += sampleMotion;
                foreach (FastModeType mode in new[] { FastModeType.Normal, FastModeType.Fast })
                {
                    SaveManager.Instance.PrefsSave.FastMode = mode;
                    Section($"special-heavy-{mode}-collapse-slaughter-straight-ki");
                    await Play<CollapseFistRedesignV1>();
                    await WaitFrames(12);
                    await Play<Slaughter>();
                    await WaitFrames(12);
                    await Play<StraightKiRedesignV1>();
                    await WaitFrames(12);
                    Require(actor.Position.IsEqualApprox(root), "Special heavy moved the health/status root.");
                }
                Section("special-heavy-roll-and-attack-overlap");
                await RollAndShoot(1);
                await Play<CollapseFistRedesignV1>();
                await Play<StrikeNinjaSlayerRedesignV1>();
                await WaitFrames(15);
                Section("special-heavy-raised-target");
                NCreature raised = room.GetCreatureNode(target)!;
                Vector2 targetBaseline = raised.Position;
                raised.Position += new Vector2(0f, -100f);
                await Play<StraightKiRedesignV1>();
                await WaitFrames(18);
                raised.Position = targetBaseline;
                Section("sawatari-restored-whole-body-and-exchange");
                await Move(target, SawatariMonster.AttackMoveId, player.Creature);
                await Task.WhenAll(Move(target, SawatariMonster.AttackMoveId, player.Creature),
                    Play<StrikeNinjaSlayerRedesignV1>());
                await WaitFrames(18);
                Require(room.GetCreatureNode(target)!.Visuals.GetNode<Node2D>("AirborneAnchor").Transform.IsEqualApprox(Transform2D.Identity),
                    "Bamboo retained a tilted pose after simultaneous hurt.");
                Section("head-orbit-heavy-kick-throw-alabama-tornado");
                await Play<HellTornadoRedesignV1>();
                await WaitFrames(60);
                await Play<StraightKiRedesignV1>();
                await Play<RoundhouseKickRedesignV1>();
                await RollAndShoot(1);
                await Play<AlabamaDropRedesignV1>();
                object hellPose = AccessTools.Property(pose.GetType(), "HellTornado").GetValue(pose)!;
                Require((bool)AccessTools.Property(hellPose.GetType(), "Active").GetValue(hellPose)!,
                    "Alabama removed the persistent Hell Tornado body split.");
                await Play<TornadoFistRedesignV1>(energy: 4);
                await PowerCmd.Remove<HellTornadoRedesignPower>(player.Creature);
                await PowerCmd.Remove<SoarPower>(player.Creature);
                await WaitFrames(18);
                Section("semi-naraku-head-orbit-switch-and-heavy");
                await Play<HellTornadoRedesignV1>();
                await Play<NarakuFormRedesignV1>();
                await Play<StraightKiRedesignV1>();
                await PowerCmd.Remove<NarakuFormRedesignPower>(player.Creature);
                await Play<RoundhouseKickRedesignV1>();
                await PowerCmd.Remove<HellTornadoRedesignPower>(player.Creature);
                await PowerCmd.Remove<SoarPower>(player.Creature);
                await WaitFrames(18);
                Section("one-soul-form-priority-head-orbit-and-heavy");
                await Play<OneBodyOneSoul>();
                await WaitFrames(18);
                await Play<CollapseFistRedesignV1>();
                await Play<HellTornadoRedesignV1>();
                await WaitFrames(60);
                await Play<StraightKiRedesignV1>();
                await RollAndShoot(1);
                await PowerCmd.Remove<OneBodyOneSoulPower>(player.Creature);
                await PowerCmd.Remove<HellTornadoRedesignPower>(player.Creature);
                await PowerCmd.Remove<SoarPower>(player.Creature);
                await WaitFrames(18);
                Section("full-naraku-final-layers-heavy-and-head-orbit");
                var fullRelic = await RelicCmd.Obtain<NarakuWithinRelic>(player);
                await fullRelic.BeforeCombatStart();
                await Play<StraightKiRedesignV1>();
                await Play<HellTornadoRedesignV1>();
                await WaitFrames(60);
                await PowerCmd.Remove<HellTornadoRedesignPower>(player.Creature);
                await PowerCmd.Remove<SoarPower>(player.Creature);
                await PowerCmd.Remove<NarakuFormRedesignPower>(player.Creature);
                await RelicCmd.Remove(fullRelic);
                await PowerCmd.Remove<NarakuLifePower>(player.Creature);
                await WaitFrames(18);
                Section("special-heavy-direct-placement-finisher");
                await CreatureCmd.SetCurrentHp(target, 1);
                await Play<CollapseFistRedesignV1>();
                await WaitFrames(45);
                _checkpoints.Write("action.special-heavy-completed");
                await recorder.Stop();
                recording = false;
                File.WriteAllText(Path.Combine(directory, "sections.json"), sections.ToJsonString());
                File.WriteAllText(Path.Combine(directory, "motion-frames.json"), motionFrames.ToJsonString());
                _firstCombatCompleted.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return;
            }
            Section("sawatari-opening-aim");
            CardModel aimed = combat.CreateCard<StrikeNinjaSlayerRedesignV1>(player);
            var aimDrag = new Node();
            actor.AddChild(aimDrag);
            Vector2 core = actor.GetGlobalTransformWithCanvas() * actor.VfxSpawnPosition;
            foreach (Vector2 pointer in new[] { core + new Vector2(400, -180), core + new Vector2(-180, -100),
                         core + new Vector2(400, 70) })
            {
                AccessTools.Method(pose.GetType(), "Drag").Invoke(pose, [aimDrag, aimed, pointer, null]);
                await WaitFrames(6);
            }
            AccessTools.Method(pose.GetType(), "EndDrag").Invoke(pose, [aimDrag, false]);
            aimDrag.QueueFree();

            Section("sawatari-punch-roll-volley");
            await Play<StrikeNinjaSlayerRedesignV1>();
            await Play<KarateStraightRedesignV1>();
            await RollAndShoot(1);
            await Play<OneDrinkOneStrikeRedesignV1>();

            Section("sawatari-bamboo-hurt-dodge-block");
            await Task.WhenAll(Move(target, SawatariMonster.AttackMoveId, player.Creature),
                Play<StrikeNinjaSlayerRedesignV1>());
            Require(room.GetCreatureNode(target)!.Visuals.GetNode<Node2D>("AirborneAnchor").Transform.IsEqualApprox(Transform2D.Identity),
                "Sawatari retained a tilted attack/hurt snapshot after the exchange.");
            _checkpoints.Write("action.sawatari-exchange-restored");
            await Play<HardItOutRedesignV1>();
            await Task.WhenAll(Move(target, SawatariMonster.AttackMoveId, player.Creature),
                Play<DefendNinjaSlayerRedesignV1>());
            await PowerCmd.Apply<WeakPower>(choice, player.Creature, 1, target, null);
            await PowerCmd.Remove<WeakPower>(player.Creature);

            Section("sawatari-companion-combination");
            await Task.WhenAll(Move(koki, YamotoKokiMonster.SummonMissileMoveId, target),
                Move(yukano, YukanoMonster.ArrowMoveId, target), Play<RoundhouseKickRedesignV1>());
            await Task.WhenAll(Move(koki, YamotoKokiMonster.IaiSlashMoveId, target),
                Move(yukano, YukanoMonster.ShurikenMoveId, target), RollAndShoot(1));
            foreach (Creature missile in player.PlayerCombatState.Pets
                         .Where(p => p.Monster is YamotoKokiOrigamiMissile).ToArray())
                await missile.Monster!.PerformMove();
            await Play<SweepKickRedesignV1>();
            await Play<DragonFlyingKickRedesignV1>();

            Section("sawatari-tornado-alabama");
            await Play<TornadoFistRedesignV1>(energy: 3);
            await Play<AlabamaDropRedesignV1>();
            await Play<StrikeNinjaSlayerRedesignV1>();
            await WaitFrames(6);
            Require(actor.Position.IsEqualApprox(root), "Sawatari sequence moved the combat root.");

            Section("dark-ninja-entrance-counter");
            target = await ReplaceEnemy<DarkNinjaMonster>();
            NRunMusicController.Instance?.PlayCustomMusic(NinjaSlayerAudio.DarkNinjaBattleMusicEvent);
            await Move(target, DarkNinjaMonster.CounterStanceMoveId, player.Creature);
            await PowerCmd.Remove<EvasionPower>(target);
            for (int i = 0; i < 3; i++) await Play<StrikeNinjaSlayerRedesignV1>();
            await PowerCmd.Remove<IaiPower>(target);

            Section("dark-ninja-slash-and-dark-strike");
            await Move(target, DarkNinjaMonster.DarkStrikeMoveId, player.Creature);
            await Task.WhenAll(Move(koki, YamotoKokiMonster.IaiSlashMoveId, target),
                Move(yukano, YukanoMonster.ShurikenMoveId, target), RollAndShoot(1));

            Section("naraku-airborne-attack-throw");
            await Play<NarakuFormRedesignV1>();
            await Play<HellTornadoRedesignV1>();
            await Play<StrikeNinjaSlayerRedesignV1>();
            await Play<RoundhouseKickRedesignV1>();
            await RollAndShoot(1);
            await PowerCmd.Remove<HellTornadoRedesignPower>(player.Creature);
            await PowerCmd.Remove<SoarPower>(player.Creature);

            Section("full-naraku-tornado-combination");
            var narakuRelic = await RelicCmd.Obtain<NarakuWithinRelic>(player);
            await narakuRelic.BeforeCombatStart();
            await Play<TornadoFistRedesignV1>(energy: 4, charge: true);
            await PowerCmd.Remove<NarakuFormRedesignPower>(player.Creature);
            await RelicCmd.Remove(narakuRelic);
            await PowerCmd.Remove<NarakuLifePower>(player.Creature);

            Section("final-roll-volley-tornado-finisher");
            await RollAndShoot(1);
            await Play<SatsubatsuRedesignV1>();
            await CreatureCmd.SetCurrentHp(target, 1);
            await Play<TornadoFistRedesignV1>(energy: 6, charge: true);
            await WaitFrames(45);
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
            if (sampleMotion != null)
            {
                RenderingServer.FramePreDraw -= sampleMotion;
                File.WriteAllText(Path.Combine(directory, "motion-frames.json"), motionFrames.ToJsonString());
            }
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
