using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.AutoSlay;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;
using NinjaSlayer.Relics;
using STS2RitsuLib;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Telemetry;
using STS2RitsuLib.Ui.Toast;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private TheaterRuntime? _theater;
    internal void ObserveTheaterCounter(Creature actor) => _theater?.Counter(actor);

    private async Task RunTheaterPhaseAsync()
    {
        TheaterScript script = TheaterScript.Load(_configuration.TheaterScriptPath!);
        Require(script.Seed == _configuration.Seed, "Launcher and theater seeds differ.");
        var narration = (ModSettingsValueBinding<NinjaSlayerSettingsData, bool>)AccessTools.Field(
            typeof(NinjaSlayerSettings), "_narration").GetValue(null)!;
        narration.Write(script.NarrationEnabled);
        narration.Save();
        Require(narration.Read() == script.NarrationEnabled, "Theater narration setting was not applied.");
        _checkpoints.Write("theater.narration", data: new JsonObject { ["enabled"] = narration.Read() });
        foreach (var applicant in TelemetryRegistry.GetApplicants())
            RitsuLibFramework.SetTelemetryApplicantConsent(applicant.ApplicantId, TelemetryConsentState.Denied);
        SaveManager.Instance.SetFtuesEnabled(false);
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Fast;
        if (script.Purpose == "greeting")
        {
            await RunGreetingPreview(script);
            return;
        }
        var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<NinjaSlayerCharacter>(),
            true, ActModel.GetDefaultList(), [], script.Seed, GameMode.Standard, 0);
        await RunManager.Instance.EnterAct(script.Act - 1);
        Player owner = LocalContext.GetMe(run)!;
        foreach (string name in script.Relics)
        {
            Type type = typeof(MegaCrit.Sts2.Core.Models.Relics.Mango).Assembly.GetTypes()
                .Concat(typeof(MaguroSushiRelic).Assembly.GetTypes())
                .Single(t => t.Name == name && typeof(RelicModel).IsAssignableFrom(t));
            var relic = (RelicModel)AccessTools.Method(typeof(ModelDb), "Relic", [], [type]).Invoke(null, null)!;
            var selector = new TestCardSelector();
            selector.PrepareToSelect(name == "YummyCookie" ? [0, 1, 2, 3] : [0]);
            using var selected = CardSelectCmd.UseSelector(selector);
            if (!owner.Relics.Any(r => r.GetType() == type)) await RelicCmd.Obtain(relic.ToMutable(), owner);
        }
        MapPoint point = run.Map.GetAllMapPoints().First(p => p.PointType == MapPointType.Monster);
        await RunManager.Instance.EnterMapCoord(point.coord);
        await ExecuteTheaterAsync(CancellationToken.None);
        if (script.Purpose == "yukano-popup" && script.Cues.Any(cue => cue.Id == "save_reload"))
            await VerifyYukanoPopupNextCombat(run, point);
        _checkpoints.Write("theater.native-quit");
        NGame.Instance.Quit();
    }

    private async Task ExecuteTheaterAsync(CancellationToken cancellationToken)
    {
        string directory = _configuration.ActionPreviewDirectory!;
        Directory.CreateDirectory(directory);
        TheaterScript script = TheaterScript.Load(_configuration.TheaterScriptPath!);
        await WaitUntilAsync(() => CombatManager.Instance.IsInProgress, "Theater combat did not start", cancellationToken);
        Player player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())!;
        await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
            "Theater player turn did not start", cancellationToken);
        Func<bool> autoSlayerCheck = NonInteractiveMode.AutoSlayerCheck;
        NonInteractiveMode.AutoSlayerCheck = static () => false;
        try
        {
            using var theater = new TheaterRuntime(this, script, directory, player, cancellationToken);
            _theater = theater;
            await theater.Prepare();
            await theater.Run();
        }
        finally { _theater = null; NonInteractiveMode.AutoSlayerCheck = autoSlayerCheck; }
    }

    private sealed partial class TheaterRuntime : IDisposable
    {
        private readonly SmokeController _driver;
        private readonly TheaterScript _script;
        private readonly string _directory;
        private readonly Player _player;
        private readonly CancellationToken _cancel;
        private readonly ICombatState _combat;
        private readonly NCombatRoom _room;
        private readonly BlockingPlayerChoiceContext _choice = new();
        private readonly Dictionary<string, Creature> _actors = [];
        private readonly JsonArray _timeline = [];
        private readonly JsonArray _damage = [];
        private readonly JsonArray _motion = [];
        private readonly Dictionary<string, List<double>> _coverage = [];
        private readonly List<(Creature Actor, Action<int, int> Handler)> _hpSubscriptions = [];
        private readonly TornadoViewportRecording _recorder;
        private CombatCinematicCameraLease? _camera;
        private NarakuWithinRelic? _fullNaraku;
        private long _start;
        private bool _recording;
        private string _cue = "setup";
        private string _enemy = "sawatari";
        private Vector2 _enemySlot;
        private int _counterCount;
        private int _volleyCount;
        private int _rollCount;
        private int _knifeRounds;
        private int _missileRounds;
        private int _misfires;
        private double _captureStartSeconds;

        internal TheaterRuntime(SmokeController driver, TheaterScript script, string directory,
            Player player, CancellationToken cancel)
        {
            _driver = driver; _script = script; _directory = directory; _player = player; _cancel = cancel;
            _combat = CombatManager.Instance.DebugOnlyGetState()!;
            _room = NCombatRoom.Instance!;
            _recorder = new TornadoViewportRecording(driver._tree.Root, directory, "ultrafast",
                RenderingServer.GetCurrentRenderingMethod() != "gl_compatibility");
        }

        private double Seconds => _start == 0 ? 0 : Stopwatch.GetElapsedTime(_start).TotalSeconds;
        private Creature Actor(string name) => _actors[name == "enemy" ? _enemy : name];
        private NCreature Node(string name) => Actor(name).GetCreatureNode()
            ?? throw new InvalidOperationException($"Theater actor {name} has no node.");
        private Node2D Pose => Node("ninja").Visuals.GetNode<Node2D>("%AimPose");
        private static Type ProductType(string name) => typeof(ShurikenOrb).Assembly.GetType(name, true)!;
        private static object? InvokeMethod(Type type, object? instance, string method, params object?[] args) =>
            AccessTools.Method(type, method).Invoke(instance, args);
        private static object? Call(object instance, string method, params object?[] args) =>
            InvokeMethod(instance.GetType(), instance, method, args);
        private static Task Animation(string type, string method, params object?[] args) =>
            (Task)InvokeMethod(ProductType("NinjaSlayer.Code.ExternalAnimations." + type), null, method, args)!;

        internal async Task Prepare()
        {
            Engine.MaxFps = 0;
            SaveManager.Instance.PrefsSave.FastMode = FastModeType.Fast;
            SaveManager.Instance.PrefsSave.MuteInBackground = false;
            Require(_combat.RunState.CurrentActIndex == 2, "Theater is not in Act 3.");
            await _driver.WaitFrames(90);
            Creature[] previous = _combat.HittableEnemies.ToArray();
            _enemySlot = previous[0].GetCreatureNode()!.Position;
            var sawatari = (SawatariMonster)ModelDb.Monster<SawatariMonster>().ToMutable();
            sawatari.ActThree = true;
            Creature enemy = _combat.CreateCreature(sawatari, CombatSide.Enemy, null);
            await CreatureCmd.Add(enemy);
            foreach (Creature old in previous)
            {
                NCreature node = old.GetCreatureNode()!;
                node.Hide(); _room.RemoveCreatureNode(node); node.QueueFree();
                CombatManager.Instance.RemoveCreature(old);
                if (_combat.ContainsCreature(old)) _combat.RemoveCreature(old);
            }
            enemy.GetCreatureNode()!.Position = _enemySlot;
            AddActor("ninja", _player.Creature);
            AddActor("sawatari", enemy);
            Require(enemy.MaxHp == 280, "Sawatari must use native Act 3 HP.");
            foreach (var card in _player.PlayerCombatState!.AllCards.ToArray())
                await CardPileCmd.RemoveFromCombat(card);
            foreach (CardModel original in _player.Deck.Cards)
            {
                CardModel copy = _combat.CloneCard(original);
                copy.DeckVersion = original;
                await CardPileCmd.Add(copy, PileType.Draw, skipVisuals: true);
            }
            for (int i = _player.Deck.Cards.Count; i < 45; i++)
                await CardPileCmd.Add(_combat.CreateCard<StrikeNinjaSlayerRedesignV1>(_player), PileType.Draw, skipVisuals: true);
            await CardPileCmd.Draw(_choice, 4, _player, fromHandDraw: true);
            await PowerCmd.Apply<KaratePower>(_choice, _player.Creature, _script.Karate, _player.Creature, null);
            if (_script.Purpose == "promo")
            {
                await RelicCmd.Obtain<YamotoKokiCuteRelic>(_player);
                await RelicCmd.Obtain<YukanoCompanionRelic>(_player);
            }
            if (_script.Purpose == "yukano-popup") await RelicCmd.Obtain<YukanoCompanionRelic>(_player);
            await RemovePower<EvasionPower>(_player.Creature);
            await PlayerCmd.SetEnergy(6, _player);
            AccessTools.Property(Pose.GetType(), "UseTornadoHitStop").SetValue(Pose, true);
            FindDescendant<NPlayerTurnBanner>(_driver._tree.Root)?.QueueFree();
            await _driver.WaitFrames(60);
            RitsuToastService.CloseAll(true);
            if (_script.Purpose == "promo")
            {
                var healthBar = Node("sawatari").GetNode<NCreatureStateDisplay>("%HealthBar")
                    .GetNode<NHealthBar>("%HealthBar");
                Require(healthBar.IsVisibleInTree()
                    && ReferenceEquals(AccessTools.Field(typeof(NHealthBar), "_creature").GetValue(healthBar), enemy),
                    "Sawatari must display his native bound health bar.");
                _driver._checkpoints.Write("theater.sawatari-health-bar", data: new JsonObject
                    { ["hp"] = enemy.CurrentHp, ["maxHp"] = enemy.MaxHp, ["visible"] = true });
            }
            if (_script.Purpose == "finisher-audit") await PrepareFinisherAudit();
            if (_script.Purpose == "yukano-popup") RenderingServer.FramePostDraw += Sample;
            else RenderingServer.FramePreDraw += Sample;
        }

        private void AddActor(string name, Creature creature)
        {
            _actors.Add(name, creature);
            void Changed(int before, int after)
            {
                if (_start == 0) return;
                _damage.Add(new JsonObject { ["cue"] = _cue, ["seconds"] = Seconds, ["actor"] = name,
                    ["before"] = before, ["after"] = after });
            }
            creature.CurrentHpChanged += Changed;
            _hpSubscriptions.Add((creature, Changed));
        }

        internal async Task Run()
        {
            var config = _driver._configuration;
            string first = config.TheaterFromCue ?? _script.Cues[0].Id;
            string last = config.TheaterToCue ?? _script.Cues[^1].Id;
            int from = Array.FindIndex(_script.Cues, c => c.Id == first);
            int to = Array.FindIndex(_script.Cues, c => c.Id == last);
            Require(from >= 0 && to >= from, "Theater cue range is invalid.");
            _start = Stopwatch.GetTimestamp();
            try
            {
                for (int index = 0; index <= to; index++)
                {
                    TheaterCue cue = _script.Cues[index];
                    if (cue.NotBefore > Seconds) await Wait(cue.NotBefore - Seconds);
                    if (index == from)
                    {
                        await _recorder.Start(); _recording = true;
                        if (from == 0) _start = _recorder.StartTimestamp;
                        _captureStartSeconds = Stopwatch.GetElapsedTime(_start, _recorder.StartTimestamp).TotalSeconds;
                    }
                    _cue = cue.Id;
                    _driver._checkpoints.Write("theater.cue", data: new JsonObject { ["id"] = _cue, ["seconds"] = Seconds });
                    var row = new JsonObject { ["id"] = _cue, ["start"] = Seconds, ["before"] = State() };
                    _timeline.Add(row);
                    foreach (var step in cue.Steps)
                        await _driver.WaitTaskAsync(Step(step), $"Theater cue stalled: {cue.Id}",
                            TimeSpan.FromSeconds(step.Action == "architect_execution" && step.Greeting == "full" ? 45 : 20));
                    row["end"] = Seconds;
                    row["after"] = State();
                }
                if (to == _script.Cues.Length - 1 && Seconds < _script.Duration)
                    await Wait(_script.Duration - Seconds);
            }
            finally
            {
                _camera?.Dispose(); _camera = null;
                if (_recording) { await _recorder.Stop(); _recording = false; }
                WriteReport();
            }
            _driver._checkpoints.Write("theater.completed", data: new JsonObject { ["seconds"] = Seconds,
                ["counterCount"] = _counterCount, ["rolls"] = _rollCount, ["volleys"] = _volleyCount,
                ["knifeRounds"] = _knifeRounds, ["missileRounds"] = _missileRounds, ["misfires"] = _misfires });
        }

        private async Task Step(TheaterStep step)
        {
            _cancel.ThrowIfCancellationRequested();
            if (step.Delay > 0) await Wait(step.Delay);
            for (int repeat = 0; repeat < step.Repeat; repeat++)
            {
                if (step.Sequence != null) { foreach (var child in step.Sequence) await Step(child); continue; }
                if (step.Parallel != null) { await Task.WhenAll(step.Parallel.Select(Step)); continue; }
                switch (step.Action)
                {
                    case "wait": await Wait(step.Seconds); break;
                    case "card": await PlayCard(step); break;
                    case "give_card": await CardPileCmd.Add(CreateCard(step.Card!), PileType.Hand, skipVisuals: true); break;
                    case "speed": SaveManager.Instance.PrefsSave.FastMode = Enum.Parse<FastModeType>(step.Mode!); break;
                    case "swap_sides": await SwapSides(); break;
                    case "replace_enemy": await ReplaceEnemy(step.Monster!); break;
                    case "move": await Move(step); break;
                    case "roll_volley": await RollVolley(step.Count); break;
                    case "knife_exchange": await KnifeExchange(step.Count); break;
                    case "entrance": await Entrance(step.Actor); break;
                    case "missiles": await Missiles(step.FriendlyFire); break;
                    case "apology": await Apology(); break;
                    case "takeover": await Takeover(); break;
                    case "form": await Form(step.Form!); break;
                    case "clear_air": await ClearAir(); break;
                    case "camera": await Camera(step.Actor, step.Zoom, step.Height, step.Seconds); break;
                    case "clear_block": await RemoveSmokeBlock(Actor(step.Actor)); break;
                    case "block": await CreatureCmd.GainBlock(Actor(step.Actor), step.Amount, ValueProp.Unpowered, null); break;
                    case "power": await Power(step.Actor, step.Power!, step.Amount); break;
                    case "remove_power": await RemovePower(Actor(step.Actor), step.Power!); break;
                    case "aim": await Aim(); break;
                    case "aim_motion": await AimMotion(step.Mode!); break;
                    case "architect_compare": await ArchitectComparison(); break;
                    case "architect_execution": await ArchitectExecution(step); break;
                    case "sawatari_event_entrance": await SawatariEventEntrance(step); break;
                    case "theft_round": await TheftRound(step.Mode!); break;
                    case "blood_check": await BloodCheck(step.Mode!); break;
                    case "overhead_check": await OverheadCheck(); break;
                    case "audit_calibration": SfxCmd.Play(NinjaSlayerAudio.NinjaSlayerSlowAttackEvent); await Wait(1.5); SfxCmd.Play(NinjaSlayerAudio.NinjaSlayerHurtEvent); await Wait(1.5); break;
                    case "audit_orb": await OrbCmd.EvokeNext(_choice, _player); break;
                    case "companion_facing": await CompanionFacingPreview(); break;
                    case "popup_check": await PopupCheck(step.Mode!); break;
                    default: throw new InvalidDataException($"Unsupported theater action {step.Action}.");
                }
                foreach (string cover in step.Covers) Cover(cover);
            }
        }

        private async Task PlayCard(TheaterStep step)
        {
            if (_player.PlayerCombatState!.Hand.Cards.Count >= 9)
                await CardCmd.Discard(_choice, _player.PlayerCombatState.Hand.Cards.Where(c => c is not SawatariMachete).Take(3).ToArray());
            await PlayerCmd.SetEnergy(step.Energy, _player);
            CardModel card = CreateCard(step.Card!);
            await CardPileCmd.Add(card, PileType.Hand, skipVisuals: true);
            if (step.Charge)
            {
                var drag = new Node(); Node("ninja").AddChild(drag);
                Call(Pose, "Drag", drag, card, Node("enemy").VfxSpawnPosition, Actor("enemy"));
                await Wait(step.Seconds > 0 ? step.Seconds : .25);
                Call(Pose, "EndDrag", drag, true); drag.QueueFree();
            }
            var selector = new TestCardSelector();
            selector.PrepareToSelect(step.Selection);
            using var selection = CardSelectCmd.UseSelector(selector);
            await PlayCreatedCard(card, card.TargetType is TargetType.AnyEnemy or TargetType.AnyAlly ? Actor(step.Target) : null);
        }

        private CardModel CreateCard(string name)
        {
            Type type = typeof(StrikeNinjaSlayerRedesignV1).Assembly.GetTypes().Concat(typeof(MegaCrit.Sts2.Core.Models.Cards.Shiv).Assembly.GetTypes())
                .Single(type => type.Name == name && typeof(CardModel).IsAssignableFrom(type));
            var canonical = (CardModel)AccessTools.Method(typeof(ModelDb), "Card", [], [type]).Invoke(null, null)!;
            return _combat.CreateCard(canonical, _player);
        }

        private async Task SwapSides()
        {
            NCreature owner = Node("ninja"), target = Node("enemy");
            float x = owner.Position.X;
            owner.Position = new(target.Position.X, owner.Position.Y);
            target.Position = new(x, target.Position.Y);
            _enemySlot = target.Position;
            Call(Pose, "RequestTurn", target.GlobalPosition.X < owner.GlobalPosition.X);
            Node2D body = target.Visuals.GetCurrentBody();
            body.Scale = new(-body.Scale.X, body.Scale.Y);
            await Wait(.2);
        }

        private async Task ReplaceEnemy(string name)
        {
            Type type = typeof(MegaCrit.Sts2.Core.Models.Monsters.ThievingHopper).Assembly.GetTypes()
                .Single(type => type.Name == name && typeof(MonsterModel).IsAssignableFrom(type));
            var canonical = (MonsterModel)AccessTools.Method(typeof(ModelDb), "Monster", [], [type]).Invoke(null, null)!;
            Creature old = Actor("enemy");
            Vector2 slot = Node("enemy").Position;
            Creature replacement = _combat.CreateCreature(canonical.ToMutable(), CombatSide.Enemy, null);
            await CreatureCmd.Add(replacement);
            NCreature node = old.GetCreatureNode()!;
            node.Hide(); _room.RemoveCreatureNode(node); node.QueueFree();
            CombatManager.Instance.RemoveCreature(old);
            if (_combat.ContainsCreature(old)) _combat.RemoveCreature(old);
            replacement.GetCreatureNode()!.Position = slot;
            _enemy = name;
            AddActor(name, replacement);
        }

        private static async Task PlayCreatedCard(CardModel card, Creature? target)
        {
            Require(card.CanPlay(out _, out _) && card.IsValidTarget(target), $"Theater card cannot be played: {card.Id}");
            _ = TaskHelper.RunSafely(card.OnEnqueuePlayVfx(target));
            var action = new PlayCardAction(card, target);
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
            await action.CompletionTask;
            if (action.Exception != null) throw action.Exception;
        }

        private async Task Move(TheaterStep step)
        {
            Creature actor = Actor(step.Actor), target = Actor(step.Target);
            if (actor.Monster is SawatariMonster sawatari)
            {
                string method = step.Move switch
                {
                    "bamboo" => "PlayAttack", "dual" => "PlayDualAttack", "bow" => "ArrowMove", "throw" => "ThrowMove",
                    _ => throw new InvalidDataException("Unknown Sawatari move.")
                };
                await (Task)Call(sawatari, method, method is "PlayAttack" or "PlayDualAttack" ? target : new Creature[] { target })!;
                return;
            }
            var move = (MoveState)actor.Monster!.MoveStateMachine!.States[step.Move!];
            int beforeHp = target.CurrentHp;
            decimal beforeBlock = target.Block;
            actor.Monster.SetMoveImmediate(move, true);
            await move.PerformMove([target]);
            if (actor.Monster is YukanoMonster && step.Move is YukanoMonster.ArrowMoveId or YukanoMonster.ShurikenMoveId)
                Require(target.CurrentHp < beforeHp || target.Block < beforeBlock,
                    $"Yukano {step.Move} finished without any real damage or block loss.");
        }

        private async Task RollVolley(int shots)
        {
            var hand = _player.PlayerCombatState!.Hand;
            if (hand.Cards.Count > 6)
                await CardCmd.Discard(_choice, hand.Cards.Where(c => c is not SawatariMachete).Take(hand.Cards.Count - 6).ToArray());
            await (Task)InvokeMethod(typeof(ShurikenOrb), null, "AddStock", _choice, _player, shots)!;
            var before = hand.Cards.ToHashSet();
            await CardPileCmd.Draw(_choice, shots, _player);
            _rollCount++; Cover("backflip");
            await Wait(.09);
            await CardCmd.Discard(_choice, hand.Cards.Where(c => !before.Contains(c)).ToArray());
            _volleyCount++; Cover("shuriken-volley");
        }

        private async Task KnifeExchange(int knives)
        {
            for (int i = 0; i < knives; i++) await Move(new() { Actor = "sawatari", Target = "ninja", Move = "throw" });
            foreach (var knife in _player.PlayerCombatState!.Hand.Cards.OfType<SawatariMachete>().ToArray())
            {
                await PlayerCmd.SetEnergy(6, _player);
                await PlayCreatedCard(knife, Actor("sawatari"));
            }
            _knifeRounds++; Cover("knife-round-trip");
        }

        private async Task Entrance(string name)
        {
            Creature pet = name switch
            {
                "koki" => await PlayerCmd.AddPet<YamotoKokiMonster>(_player),
                "yukano" => await PlayerCmd.AddPet<YukanoMonster>(_player),
                _ => throw new InvalidDataException("Unknown companion.")
            };
            AddActor(name, pet);
            InvokeMethod(ProductType("NinjaSlayer.Code.Combat.CompanionIntentLifecycle"), null, "BeginCombat", pet);
            await Animation("YamotoKokiCombatAnimations", "PlayEntrance", pet, name == "koki");
        }

        private async Task Missiles(bool misfire)
        {
            await Move(new() { Actor = "koki", Move = YamotoKokiMonster.SummonMissileMoveId });
            Creature[] missiles = _player.PlayerCombatState!.Pets.Where(p => p.Monster is YamotoKokiOrigamiMissile && p.IsAlive).ToArray();
            Require(missiles.Length == 2, "A theater missile round must contain exactly two live missiles.");
            if (_script.Purpose == "finisher-audit")
            {
                await ((YamotoKokiOrigamiMissile)missiles[0].Monster!).ExecuteExplosion(missiles[0]);
                _missileRounds++;
                return;
            }
            var launches = new List<Task>();
            for (int i = 0; i < missiles.Length; i++)
            {
                Creature victim = misfire && i == missiles.Length - 1 ? _player.Creature : Actor("enemy");
                launches.Add((Task)Call(missiles[i].Monster!, "ExplodeMove", (object)new Creature[] { victim })!);
                if (i + 1 < missiles.Length) await Wait(.2);
            }
            await Task.WhenAll(launches);
            _missileRounds++;
            if (misfire) { _misfires++; Cover("friendly-fire"); }
        }

        private async Task Apology()
        {
            Call(Pose, "RequestTurn", true); await Wait(.17);
            Call(Pose, "RequestTurn", false);
            await Task.WhenAll(Wait(.17), Animation("YamotoKokiCombatAnimations", "PlayFarewell", Actor("koki")));
            Require(Node("yukano").Visuals.Modulate == Colors.White, "Yukano did not regain the first companion slot tint.");
            Cover("apology-exit"); Cover("companion-relocation");
        }

        private async Task Aim()
        {
            var drag = new Node(); Node("ninja").AddChild(drag);
            CardModel card = _combat.CreateCard<StrikeNinjaSlayerRedesignV1>(_player);
            Call(Pose, "Drag", drag, card, Node("enemy").VfxSpawnPosition + new Vector2(0, -150), Actor("enemy"));
            await Wait(.18); Call(Pose, "EndDrag", drag, false); drag.QueueFree();
        }

        private async Task AimMotion(string mode)
        {
            NCreature ninja = Node("ninja"), enemy = Node("enemy");
            Vector2 root = ninja.Position, enemyRoot = enemy.Position, enemyVisual = enemy.Visuals.Position;
            Type facing = ProductType("NinjaSlayer.Code.ExternalAnimations.NinjaSlayerFacingState");
            bool Committed() => (bool)InvokeMethod(facing, null, "ResolveCommittedFacingLeft", ninja)!;
            bool Shown() => ninja.Visuals.GetNode<Node2D>("AirborneAnchor").Scale.X < 0;
            bool baseline = Committed();
            var drag = new Node(); ninja.AddChild(drag);
            CardModel card = _combat.CreateCard<StrikeNinjaSlayerRedesignV1>(_player);
            SurroundedPower? surrounded = null;
            Vector2 Core(NCreature node) => node.Visuals.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin;
            void Point(Vector2 point, Creature? hovered = null) => Call(Pose, "Drag", drag, card, point, hovered);
            async Task Sweep(Vector2 from, Vector2 to, double seconds, Creature? hovered = null)
            {
                long started = Stopwatch.GetTimestamp();
                double elapsed;
                do
                {
                    elapsed = Stopwatch.GetElapsedTime(started).TotalSeconds;
                    Point(from.Lerp(to, (float)Math.Min(1, elapsed / seconds)), hovered);
                    await _driver.WaitFrames(1);
                } while (elapsed < seconds);
            }
            try
            {
                if (mode == "tracking")
                {
                    enemy.Visuals.Position += Vector2.Up * 160f;
                    Vector2 target = Core(enemy), neutral = new(target.X, Core(ninja).Y);
                    Vector2 outside = target + Vector2.Up * 260f;
                    await Sweep(neutral, target, .35);
                    await Sweep(target, outside, .12);
                    await Sweep(outside, outside, .35);
                    await Sweep(outside, neutral, .12);
                    await Sweep(neutral, target, .18, enemy.Entity);
                    await Sweep(target, target, .25, enemy.Entity);
                    float settled = (float)AccessTools.Field(Pose.GetType(), "_angle").GetValue(Pose)!;
                    for (int sync = 0; sync < 10; sync++) Call(Pose, "SyncNow");
                    Require(settled == (float)AccessTools.Field(Pose.GetType(), "_angle").GetValue(Pose)!,
                        "Repeated pose sync accelerated aiming.");
                }
                else if (mode == "cancel")
                {
                    Vector2 front = Core(enemy);
                    float axis = ninja.Visuals.GetNode<Node2D>("AirborneAnchor").GetGlobalTransformWithCanvas().Origin.X;
                    Vector2 back = new(2f * axis - front.X, front.Y);
                    await Sweep(front, back, .2);
                    await Sweep(back, back, .25);
                    Require(Shown() != baseline && Committed() == baseline, "Preview facing was committed.");
                    Call(Pose, "EndDrag", drag, false);
                    await Wait(.045);
                    await Sweep(back, back, .055);
                    Call(Pose, "EndDrag", drag, false);
                    await Wait(.3);
                    Require(Shown() == baseline && Committed() == baseline, "Cancellation lost its facing baseline.");
                    await Sweep(back, back, .05);
                    Call(Pose, "EndDrag", drag, false);
                    await Wait(.3);
                    Require(Shown() == baseline, "Mid-turn cancellation did not return.");
                }
                else if (mode == "takeover")
                {
                    Vector2 target = Core(enemy);
                    await Sweep(target + Vector2.Up * 200, target, .1);
                    Call(Pose, "EndDrag", drag, true);
                    await PlayCard(new() { Card = nameof(KarateStraightRedesignV1) });
                }
                else if (mode == "surrounded")
                {
                    await PowerCmd.Apply<SurroundedPower>(_choice, ninja.Entity, 1, ninja.Entity, null);
                    await PowerCmd.Apply<BackAttackLeftPower>(_choice, enemy.Entity, 1, enemy.Entity, null);
                    surrounded = ninja.Entity.GetPower<SurroundedPower>()!;
                    enemy.Position = new(ninja.Position.X - 320f, enemy.Position.Y);
                    Vector2 left = Core(enemy), right = Core(ninja) + Vector2.Right * 500f;
                    var originalDirection = surrounded.Facing;
                    decimal damageBefore = surrounded.ModifyDamageMultiplicative(ninja.Entity, 10, ValueProp.Move, enemy.Entity, null
#if !NINJASLAYER_CHANNEL_STABLE
                        , null
#endif
                    );
                    await Sweep(left, left, .3);
                    Require(Shown() && surrounded.Facing == originalDirection, "Preview changed Surrounded direction.");
                    Require(damageBefore == surrounded.ModifyDamageMultiplicative(ninja.Entity, 10, ValueProp.Move, enemy.Entity, null
#if !NINJASLAYER_CHANNEL_STABLE
                        , null
#endif
                    ),
                        "Preview changed back-attack damage.");
                    Call(Pose, "EndDrag", drag, false);
                    await Wait(.25);
                    Require(!Shown(), "Cancelled Surrounded preview did not return right.");
                    await PlayCard(new() { Card = nameof(KarateStraightRedesignV1) });
                    Require(surrounded.Facing == SurroundedPower.Direction.Left, "Actual card did not turn Surrounded facing.");
                    await Sweep(right, right, .3);
                    Require(!Shown() && Committed(), "Preview did not preserve the new committed left facing.");
                    Call(Pose, "EndDrag", drag, false);
                    await Wait(.3);
                    Require(Shown() && Committed(), "Cancellation undid the actual card's left-facing state.");
                }
                else throw new InvalidDataException("Unknown aim motion: " + mode);
            }
            finally
            {
                Call(Pose, "EndDrag", drag, false);
                drag.QueueFree();
                await Wait(.3);
                enemy.Position = enemyRoot;
                enemy.Visuals.Position = enemyVisual;
                if (surrounded != null)
                {
                    await RemovePower<SurroundedPower>(ninja.Entity);
                    await RemovePower<BackAttackLeftPower>(enemy.Entity);
                    InvokeMethod(facing, null, "SetFacing", ninja, baseline);
                }
            }
            Require(ninja.Position.IsEqualApprox(root), "Aiming moved the player layout root.");
            Cover("aim-" + mode);
        }

        private async Task Form(string form)
        {
            switch (form)
            {
                case "semi": await PlayCard(new() { Card = nameof(NarakuFormRedesignV1) }); break;
                case "full":
                    _fullNaraku = await RelicCmd.Obtain<NarakuWithinRelic>(_player);
                    await _fullNaraku.BeforeCombatStart(); break;
                case "soul": await PlayCard(new() { Card = nameof(OneBodyOneSoul) }); break;
                case "normal":
                    await ClearAir();
                    await RemovePower<OneBodyOneSoulPower>(_player.Creature);
                    if (_fullNaraku != null) { await RelicCmd.Remove(_fullNaraku); _fullNaraku = null; }
                    await RemovePower<NarakuLifePower>(_player.Creature);
                    await RemovePower<NarakuFormRedesignPower>(_player.Creature); break;
                default: throw new InvalidDataException("Unknown theater form.");
            }
            Cover("form-" + form);
        }

        private async Task ClearAir()
        {
            await RemovePower<HellTornadoRedesignPower>(_player.Creature);
            await RemovePower<SoarPower>(_player.Creature);
        }

        private Task Power(string actor, string name, int amount)
        {
            Type type = typeof(KaratePower).Assembly.GetTypes().Concat(typeof(WeakPower).Assembly.GetTypes())
                .Single(t => t.Name == name && typeof(PowerModel).IsAssignableFrom(t));
            MethodInfo method = typeof(PowerCmd).GetMethods().Single(m => m.Name == "Apply" && m.IsGenericMethod
                && m.GetParameters().Length == 6 && m.GetParameters()[1].ParameterType == typeof(Creature));
            return (Task)method.MakeGenericMethod(type).Invoke(null, [_choice, Actor(actor), (decimal)amount, _player.Creature, null, false])!;
        }

        private static Task RemovePower<T>(Creature actor) where T : PowerModel => PowerCmd.Remove<T>(actor);
        private static async Task RemovePower(Creature actor, string name)
        {
            PowerModel? power = actor.Powers.SingleOrDefault(p => p.GetType().Name == name);
            if (power != null) await PowerCmd.Remove(power);
        }

        private async Task Camera(string actor, float zoom, float height, double seconds)
        {
            if (_camera == null && !CombatCinematicCameraLease.TryAcquire(_room, "theater", out _camera))
                throw new InvalidOperationException("Theater camera is already owned.");
            var camera = _camera!;
            Vector2 start = camera.CurrentPosition;
            float scale = camera.CurrentScale;
            await Tween(seconds, p =>
            {
                float nextScale = Mathf.Lerp(scale, camera.BaselineScale.X * zoom, p);
                Vector2 point = actor == "wide" ? Vector2.Zero
                    : camera.GetLocalCenter(Node(actor).Visuals.VfxSpawnPosition) + new Vector2(0, height);
                Vector2 destination = actor == "wide" ? camera.BaselinePosition
                    : camera.GetCameraPosition(point, nextScale, camera.ViewportSize * new Vector2(.5f, .43f));
                camera.SetTransform(start.Lerp(destination, p), nextScale);
            });
            if (actor == "wide") { camera.Dispose(); _camera = null; }
        }

        private async Task Takeover()
        {
            Vector2 sawatariPosition = Node("sawatari").Position;
            Creature dark = _combat.CreateCreature(ModelDb.Monster<DarkNinjaMonster>().ToMutable(), CombatSide.Enemy, null);
            AccessTools.Property(dark.Monster!.GetType(), "HasPlayedBegin").SetValue(dark.Monster, true);
            await CreatureCmd.Add(dark);
            AddActor("dark", dark);
            Require(dark.MaxHp == 180, "Dark Ninja must use native HP.");
            Node("sawatari").Position = sawatariPosition;
            NCreature darkNode = Node("dark"); darkNode.Position = _enemySlot;
            darkNode.Hide(); darkNode.IntentContainer.Hide();
            await RemovePower<EvasionPower>(dark);
            Type outcome = ProductType("NinjaSlayer.Code.ExternalAnimations.DarkStrikeImpactOutcome");
            MethodInfo hit = GetType().GetMethod(nameof(StabImpact), BindingFlags.Instance | BindingFlags.NonPublic)!.MakeGenericMethod(outcome);
            Delegate callback = hit.CreateDelegate(typeof(Func<,>).MakeGenericType(typeof(Creature), typeof(Task<>).MakeGenericType(outcome)), this);
            var camera = Camera("sawatari", 2.3f, -20, .25);
            Func<Node2D, Task> finish = async root =>
            {
                await camera;
                await Camera("wide", 1f, 0, .28);
                Vector2 start = root.Position;
                Vector2 end = _room.SceneContainer.GetGlobalTransformWithCanvas().AffineInverse()
                    * Node("sawatari").Visuals.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin + new Vector2(-105, 0);
                var body = root.GetNode<Sprite2D>("FullBody");
                Type projectionType = ProductType("NinjaSlayer.Code.ExternalAnimations.VerticalAxisSpinProjection");
                Vector2 axis = body.GetGlobalTransformWithCanvas().Origin;
                object projection = InvokeMethod(projectionType, null, "CaptureCurrent", body, axis.X, axis)!;
                object blur = InvokeMethod(ProductType("NinjaSlayer.Code.Nodes.EntangledSpinMotionBlur"), null,
                    "Create", body, body.GetRect())!;
                await Tween(.28, p =>
                {
                    root.Position = start.Lerp(end, p) - Vector2.Down * (70 * Mathf.Sin(p * Mathf.Pi));
                    float elapsed = p * .28f;
                    Func<double, double> history = age => 180f * Math.Clamp((elapsed - age) / .28f, 0d, 1d);
                    Call(projection, "ApplyDegrees", p * 180f, history);
                    Call(blur, "Record", projection, p * 180f, history, 0f);
                });
                Call(blur, "Stop");
                Call(projection, "Restore");
                root.Scale = new Vector2(-Math.Abs(root.Scale.X), root.Scale.Y);
                Vector2 kickStart = root.Position;
                await Tween(.125, p => { root.Position = kickStart + new Vector2(65 * p, 0); root.Rotation = -Mathf.Pi * .5f * p; });
                NinjaSlayerCombatVfx.PlayDefectStrikeHitFx(Actor("sawatari"));
                Require(Actor("sawatari").CurrentHp == 1, "Dark Strike must leave exactly one HP before the kick.");
                await CreatureCmd.Damage(_choice, new[] { Actor("sawatari") }, 1m, ValueProp.Unpowered | ValueProp.Unblockable, dark, null
#if !NINJASLAYER_CHANNEL_STABLE
                    , null
#endif
                );
                Vector2 slot = _room.SceneContainer.GetGlobalTransformWithCanvas().AffineInverse()
                    * darkNode.Visuals.GetNode<Sprite2D>("%Visuals").GetGlobalTransformWithCanvas().Origin;
                await Tween(.2, p => { root.Position = (kickStart + new Vector2(65, 0)).Lerp(slot, p); root.Rotation = -Mathf.Pi * .5f * (1 - p); });
            };
            await Animation("DarkNinjaSpecialAttackPresentation", "PlayDarkStrike", dark,
                new Creature[] { Actor("sawatari") }, (Func<Creature, bool>)(c => c.IsAlive), callback, finish, 1,
                (Func<Creature, bool>)(c => c.Block < 14 && c.GetPower<EvasionPower>() == null));
            _enemy = "dark";
            darkNode.Show();
            darkNode.IntentContainer.Show();
            Cover("dark-strike-entrance"); Cover("sawatari-kicked-offscreen");
        }

        private async Task<T> StabImpact<T>(Creature target)
        {
            await CreatureCmd.Damage(_choice, [target], 14m, ValueProp.Move, Actor("dark"), null
#if !NINJASLAYER_LEGACY_DAMAGE_API
                , null
#endif
            );
            return (T)Activator.CreateInstance(typeof(T), true, false, 0, true, true)!;
        }

        private async Task Wait(double seconds)
        {
            if (seconds <= 0) return;
            await _driver._tree.ToSignal(_driver._tree.CreateTimer(seconds, processAlways: false), SceneTreeTimer.SignalName.Timeout);
        }

        private async Task Tween(double seconds, Action<float> apply)
        {
            if (seconds <= 0) { apply(1); return; }
            Tween tween = _room.CreateTween();
            tween.TweenMethod(Callable.From<float>(apply), 0f, 1f, seconds).SetTrans(Godot.Tween.TransitionType.Sine).SetEase(Godot.Tween.EaseType.InOut);
            await _room.ToSignal(tween, Godot.Tween.SignalName.Finished);
        }

        private void Cover(string name)
        {
            if (!_coverage.TryGetValue(name, out var times)) _coverage[name] = times = [];
            times.Add(Seconds);
        }

        internal void Counter(Creature actor)
        {
            if (_actors.TryGetValue("dark", out Creature? dark) && ReferenceEquals(actor, dark))
            {
                _counterCount++;
                Cover("dark-iai-counter");
                _driver._checkpoints.Write("theater.iai", data: new JsonObject
                    { ["cue"] = _cue, ["seconds"] = Seconds, ["count"] = _counterCount });
            }
        }

        private void Sample()
        {
            if (_start == 0) return;
            var row = new JsonObject { ["seconds"] = Seconds, ["cue"] = _cue };
            if (IsArchitectPreview) SampleArchitect(row);
            if (_script.Purpose == "finisher-audit") SampleFinisherAudit(row);
            if (_script.Purpose == "aim")
            {
                row["aimAngle"] = (float)AccessTools.Field(Pose.GetType(), "_angle").GetValue(Pose)!;
                row["displayFacingLeft"] = Node("ninja").Visuals.GetNode<Node2D>("AirborneAnchor").Scale.X < 0f;
                row["surroundedFacing"] = Actor("ninja").GetPower<SurroundedPower>()?.Facing.ToString();
            }
            foreach (var (name, actor) in _actors)
            {
                if (actor.GetCreatureNode() is not { } node || !GodotObject.IsInstanceValid(node)) continue;
                Vector2 core = node.Visuals.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin;
                if (_script.Purpose == "finisher-audit") core = _auditRender * core;
                row[name] = new JsonObject { ["x"] = node.Position.X, ["y"] = node.Position.Y,
                    ["coreX"] = core.X, ["coreY"] = core.Y, ["hp"] = actor.CurrentHp,
                    ["iai"] = actor.GetPowerAmount<IaiPower>() };
                if (name == "yukano")
                {
                    var sprite = (Sprite2D)node.Body;
                    Rect2 body = sprite.GetGlobalTransformWithCanvas() * sprite.GetRect();
                    Vector2 intentBottom = node.IntentContainer.GetGlobalTransformWithCanvas()
                        * new Vector2(node.IntentContainer.Size.X * .5f, node.IntentContainer.Size.Y);
                    row["yukanoIntent"] = new JsonObject { ["bodyTop"] = body.Position.Y,
                        ["bottom"] = intentBottom.Y, ["visible"] = node.IntentContainer.IsVisibleInTree() };
                    Node2D anchor = node.Visuals.GetNode<Node2D>("AirborneAnchor");
                    row["yukanoPose"] = new JsonObject { ["rotation"] = anchor.Rotation,
                        ["scaleX"] = anchor.Transform.X.Length(), ["scaleY"] = anchor.Transform.Y.Length() };
                }
                if (_cue == "dark_strike_closeup" && name == "sawatari")
                {
                    Node2D body = node.Visuals.GetCurrentBody();
                    row["stabTarget"] = new JsonObject { ["x"] = body.Position.X,
                        ["y"] = body.Position.Y, ["rotation"] = body.Rotation, ["z"] = EffectiveZ(body) };
                }
            }
            if (_script.Purpose == "yukano-popup" || _cue is "yukano_arrow_support" or "yukano_shuriken_support")
            {
                row["renderFrame"] = Engine.GetFramesDrawn();
                if (_room.GetNodeOrNull<Node>("YukanoArrowPopup") is { } popup)
                {
                    row["popup"] = new JsonObject
                    {
                        ["position"] = (double)AccessTools.Property(popup.GetType(), "PlaybackPosition").GetValue(popup)!,
                        ["releasePosition"] = (double)AccessTools.Property(popup.GetType(), "ReleasePosition").GetValue(popup)!,
                        ["releaseFrame"] = (int)AccessTools.Property(popup.GetType(), "ReleaseRenderFrame").GetValue(popup)!
                    };
                }
                var projectiles = new JsonArray();
                foreach (Sprite2D projectile in _room.CombatVfxContainer.GetChildren().OfType<Sprite2D>())
                {
                    bool arrow = projectile.Texture is AtlasTexture atlas
                        && atlas.Atlas.ResourcePath.EndsWith("crossbow_ruby_raider.png", StringComparison.Ordinal);
                    if (!arrow && projectile.Texture?.ResourcePath != YukanoMonster.ShurikenTexturePath) continue;
                    Vector2 position = projectile.GetGlobalTransformWithCanvas().Origin;
                    projectiles.Add(new JsonObject { ["id"] = projectile.GetInstanceId(),
                        ["kind"] = arrow ? "arrow" : "shuriken", ["x"] = position.X, ["y"] = position.Y,
                        ["visible"] = projectile.IsVisibleInTree() });
                }
                row["yukanoProjectiles"] = projectiles;
            }
            if (_cue == "dark_strike_closeup" && _room.SceneContainer.GetNodeOrNull<Node2D>("DarkNinjaDarkStrike") is { } stab)
                row["stab"] = new JsonObject { ["x"] = stab.Position.X, ["y"] = stab.Position.Y,
                    ["scaleX"] = stab.Scale.X, ["z"] = stab.ZIndex,
                    ["bladeZ"] = EffectiveZ(stab.GetNode<Sprite2D>("FrontSword")),
                    ["bladeVisible"] = stab.GetNode<Sprite2D>("FrontSword").Visible,
                    ["fullBodyVisible"] = stab.GetNode<Sprite2D>("FullBody").Visible };
            _motion.Add(row);
        }

        private static int EffectiveZ(CanvasItem item)
        {
            int z = item.ZIndex;
            while (item.ZAsRelative && item.GetParent() is CanvasItem parent)
            {
                z += parent.ZIndex;
                item = parent;
            }
            return z;
        }

        private JsonObject State()
        {
            var actors = new JsonObject();
            foreach (var (name, actor) in _actors)
                actors[name] = new JsonObject { ["hp"] = actor.CurrentHp, ["maxHp"] = actor.MaxHp, ["block"] = actor.Block,
                    ["powers"] = new JsonArray(actor.Powers.OrderBy(p => p.GetType().Name)
                        .Select(p => JsonValue.Create(p.GetType().Name + ":" + p.Amount)).ToArray()) };
            return new JsonObject { ["actors"] = actors,
                ["hand"] = new JsonArray(_player.PlayerCombatState!.Hand.Cards.Select(c => JsonValue.Create(c.Id.ToString())).ToArray()),
                ["stock"] = _player.PlayerCombatState.OrbQueue.Orbs.OfType<ShurikenOrb>().Sum(o => o.StackCount),
                ["counters"] = _counterCount };
        }

        private void WriteReport()
        {
            File.Copy(_driver._configuration.TheaterScriptPath!, Path.Combine(_directory, "script.json"), true);
            File.WriteAllText(Path.Combine(_directory, "timeline.json"), _timeline.ToJsonString());
            File.WriteAllText(Path.Combine(_directory, "damage.json"), _damage.ToJsonString());
            File.WriteAllText(Path.Combine(_directory, "motion.json"), _motion.ToJsonString());
            if (_script.Purpose == "finisher-audit") WriteFinisherAudit();
            var coverage = new JsonObject();
            foreach (var (name, times) in _coverage) coverage[name] = new JsonArray(times.Select(t => JsonValue.Create(t)).ToArray());
            File.WriteAllText(Path.Combine(_directory, "coverage.json"), coverage.ToJsonString());
            File.WriteAllText(Path.Combine(_directory, "runtime.json"), new JsonObject
            {
                ["act"] = _script.Act, ["mode"] = SaveManager.Instance.PrefsSave.FastMode.ToString(),
                ["captureStartSeconds"] = _captureStartSeconds,
                ["fromCue"] = _driver._configuration.TheaterFromCue ?? _script.Cues[0].Id,
                ["toCue"] = _driver._configuration.TheaterToCue ?? _script.Cues[^1].Id,
                ["rehearsal"] = _driver._configuration.TheaterRehearsal,
                ["state"] = State()
            }.ToJsonString());
            File.WriteAllText(Path.Combine(_directory, "script.sha256"),
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_driver._configuration.TheaterScriptPath!))).ToLowerInvariant());
        }

        public void Dispose()
        {
            RenderingServer.FramePreDraw -= Sample;
            RenderingServer.FramePostDraw -= Sample;
            FinisherAuditObservationPatch.Record = null;
            foreach (var (actor, handler) in _hpSubscriptions) actor.CurrentHpChanged -= handler;
            _camera?.Dispose();
        }
    }
}

[HarmonyPatch(typeof(IaiPower), "TryCounter")]
internal static class TheaterCounterObserver
{
    private static void Prefix(IaiPower __instance) => SmokeController.Current?.ObserveTheaterCounter(__instance.Owner);
}
