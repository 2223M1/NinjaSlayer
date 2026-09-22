using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.AutoSlay.Helpers;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Events;
using NinjaSlayer.Monsters;
using NinjaSlayer.Powers;
using NinjaSlayer.Relics;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task VerifySawatariCleanup(string directory, CancellationToken ct)
    {
        var choice = new BlockingPlayerChoiceContext();
        var manager = CombatManager.Instance;
        var run = RunManager.Instance.DebugOnlyGetState()!;
        var player = LocalContext.GetMe(run)!;
        var state = manager.DebugOnlyGetState()!;
        var previous = state.Enemies.ToArray();
        var dark = state.CreateCreature(ModelDb.Monster<DarkNinjaMonster>().ToMutable(), CombatSide.Enemy, null);
        await CreatureCmd.Add(dark);
        foreach (var enemy in previous) await CreatureCmd.Kill(enemy, force: true);
        await PowerCmd.Remove<EvasionPower>(dark);
        await PowerCmd.Remove<KaratePower>(player.Creature);
        await PowerCmd.Remove<EvasionPower>(player.Creature);
        await PowerCmd.Apply<StrengthPower>(choice, dark, 4, dark, null);
        dark.SetCurrentHpInternal(100);
        var iai = await PowerCmd.Apply<IaiPower>(choice, dark, 1, dark, null);
        int flashes = 0;
        iai!.Flashed += _ => flashes++;
        async Task Strike()
        {
            var card = state.CreateCard<StrikeNinjaSlayerRedesignV1>(player);
            await CardPileCmd.Add(card, PileType.Hand);
            await CardCmd.AutoPlay(choice, card, dark);
        }
        int hp = player.Creature.CurrentHp;
        await Strike();
        Require(flashes == 1 && player.Creature.CurrentHp == hp - 4, "Living Dark Ninja did not counter exactly once.");
        await PowerCmd.Remove<IaiPower>(dark);
        iai = await PowerCmd.Apply<IaiPower>(choice, dark, 1, dark, null);
        flashes = 0;
        iai!.Flashed += _ => flashes++;
        hp = player.Creature.CurrentHp;
        dark.SetCurrentHpInternal(1);
        FinisherSmokeObserver.Reset();
        await Strike();
        await RequireCompletedFinisherAsync("NinjaSlayerAttack", 1, dark, ct);
        Require(dark.IsDead && flashes == 0 && player.Creature.CurrentHp == hp,
            "Confirmed lethal Finisher still flashed or dealt Iai damage.");
        _tree.Root.GetTexture().GetImage().SavePng(Path.Combine(directory, "dark-lethal-no-iai.png"));
        _checkpoints.Write("dark.iai-live-and-confirmed-lethal");
        await manager.CheckWinCondition();

        // A second room keeps this interruption separate from the completed Finisher above.
        var normalRoom = new RoomConsoleCmd().Process(player, ["Monster"]);
        Require(normalRoom.success, normalRoom.msg);
        await normalRoom.task!;
        await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
            "Interruption fixture did not reach the player turn", ct);
        state = manager.DebugOnlyGetState()!;
        dark = state.CreateCreature(ModelDb.Monster<DarkNinjaMonster>().ToMutable(), CombatSide.Enemy, null);
        await CreatureCmd.Add(dark);
        await PowerCmd.Remove<EvasionPower>(dark);
        await PowerCmd.Apply<StrengthPower>(choice, dark, 4, dark, null);
        iai = await PowerCmd.Apply<IaiPower>(choice, dark, 1, dark, null);
        flashes = 0;
        iai!.Flashed += _ => flashes++;
        hp = player.Creature.CurrentHp;
        Task pendingStrike = Strike();
        await WaitUntilAsync(() => NinjaSlayer.Code.ExternalAnimations.StaggerAnimation.IsActive(dark),
            "Counter interruption did not reach the hurt animation", ct);
        await CreatureCmd.Kill(dark, force: true);
        await pendingStrike;
        Require(flashes == 0 && player.Creature.CurrentHp == hp, "Death during hurt wait did not cancel Iai.");
        _checkpoints.Write("dark.iai-death-during-hurt");
        var finishRoom = new WinConsoleCmd().Process(player, []);
        Require(finishRoom.success, finishRoom.msg);
        await finishRoom.task!;

        Type route = typeof(SawatariEvent).Assembly.GetType("NinjaSlayer.Code.Patches.SawatariEventRoute", true)!;
        foreach (var scenario in new[] { (Act: 1, Fog: false, Duel: false, Kill: "thorns"),
            (Act: 1, Fog: true, Duel: true, Kill: "native"),
            (Act: 3, Fog: false, Duel: true, Kill: "support"),
            (Act: 3, Fog: true, Duel: false, Kill: "native"),
            (Act: 3, Fog: false, Duel: false, Kill: "area") })
        {
            string label = $"act{scenario.Act}-{(scenario.Fog ? "fogmog" : "gremlin")}-{(scenario.Duel ? "duel" : "loot")}-{scenario.Kill}";
            foreach (var relic in player.Relics.OfType<BioBambooRelic>().ToArray()) await RelicCmd.Remove(relic);
            var act = new ActConsoleCmd().Process(player, [scenario.Act.ToString()]);
            Require(act.success, act.msg);
            await act.task!;
            EncounterModel encounter = scenario.Fog ? ModelDb.Encounter<FogmogNormal>() : ModelDb.Encounter<GremlinMercNormal>();
            AccessTools.Method(route, "Schedule").Invoke(null, [run.Act, encounter]);
            Require((bool)AccessTools.Method(route, "TryActivate").Invoke(null, [run.Act])!, "Could not route fixture encounter.");
            // The event console command bypasses native replay initialization.
            await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<SawatariEvent>());
            await WaitUntilAsync(() => manager.IsInProgress && !manager.IsStarting
                && player.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
                "Cleanup fixture did not reach player turn", ct);
            await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
            NMapScreen.Instance?.Close(animateOut: false);
            await WaitFrames(2);
            state = manager.DebugOnlyGetState()!;
            var room = NCombatRoom.Instance!;
            Creature companion = state.Creatures.Single(c => c.Monster is SawatariMonster && c.Side == CombatSide.Player);
            Creature original = state.Enemies.Single(e => e.Monster is Fogmog or GremlinMerc);
            if (scenario.Fog)
            {
                await (Task)AccessTools.Method(typeof(Fogmog), "IllusionMove").Invoke(original.Monster, [state.PlayerCreatures.ToArray()])!;
                Require(state.Enemies.Any(e => e.Monster is EyeWithTeeth), "Fogmog did not summon the native illusion.");
                // Leave a zero-HP revival model before killing its owner.
                await CreatureCmd.Kill(state.Enemies.Single(e => e.Monster is EyeWithTeeth), force: true);
                await manager.CheckWinCondition();
                Require(!manager.IsPaused && companion.Side == CombatSide.Player && GetSawatariOptions().Count == 0,
                    "Killing a summon started the choice while its owner was alive.");
            }
            await CreatureCmd.Kill(original, force: true);
            Require(!manager.IsPaused, "Death callback paused combat before the native victory check.");
            if (!scenario.Fog)
            {
                Require(state.Enemies.Count(e => e.IsAlive && e.Monster is SneakyGremlin or FatGremlin) == 2,
                    "Native mercenary death did not spawn both replacements.");
                await manager.CheckWinCondition();
                Require(!manager.IsPaused && companion.Side == CombatSide.Player && GetSawatariOptions().Count == 0,
                    "Split enemies failed to prevent Sawatari's choice.");
                var children = state.Enemies.Where(e => e.IsAlive).ToArray();
                if (scenario.Kill == "area")
                {
                    await CreatureCmd.Damage(choice, children, 999, ValueProp.Unpowered, player.Creature, null
#if !NINJASLAYER_LEGACY_DAMAGE_API
                        , null
#endif
                    );
                }
                else
                {
                    await CreatureCmd.Kill(children[0], force: true);
                    await manager.CheckWinCondition();
                    Require(!manager.IsPaused, "One remaining split enemy did not block the choice.");
                    children[1].SetCurrentHpInternal(1);
                    if (scenario.Kill == "thorns")
                    {
                        await PowerCmd.Apply<ThornsPower>(choice, player.Creature, 100, player.Creature, null);
                        await DamageCmd.Attack(1).FromMonster(children[1].Monster!).Execute(choice);
                        await PowerCmd.Remove<ThornsPower>(player.Creature);
                    }
                    else
                    {
                        // Exercise the actual ally hook at a fresh round, including its target selection.
                        state.RoundNumber++;
                        await companion.Monster!.AfterSideTurnStart(CombatSide.Player, state.PlayerCreatures.ToArray(), state);
                        Require(!companion.HasPower<StrengthPower>() && !companion.HasPower<VigorPower>()
                            && !companion.HasPower<PlatingPower>(), "Ally killing blow gained enemy-only buffs.");
                    }
                    Require(children[1].IsDead, $"{scenario.Kill} did not kill the remaining split enemy.");
                }
            }
            await manager.CheckWinCondition();
            await manager.CheckWinCondition();
            await WaitFrames(60);
            await EndSawatariPlayerTurn(ct);
            await WaitUntilAsync(() => manager.IsPaused && GetSawatariOptions().Count == 2
                && GetSawatariOptions().All(option => option.IsVisibleInTree() && option.IsEnabled),
                "No intermission after complete death/summon resolution", ct);
            Require(!state.Enemies.Any(), "Intermission retained an enemy or dead revival model.");
            await WaitFrames(30);
            _tree.Root.GetTexture().GetImage().SavePng(Path.Combine(directory, label + "-choice.png"));
            await UiHelper.Click(GetSawatariOptions()[scenario.Duel ? 1 : 0]);
            if (scenario.Duel)
            {
                await WaitUntilAsync(() => !manager.IsPaused && state.Enemies.Any(e => e.Monster is SawatariMonster),
                    "Duel did not start", ct);
                Require(state.Enemies.Count() == 1 && state.Enemies.Single().Monster is SawatariMonster,
                    "A split enemy or illusion entered Sawatari's duel.");
                await WaitFrames(45);
                Require(state.Enemies.Count() == 1, "A retired illusion revived during the duel.");
                await CreatureCmd.Kill(state.Enemies.Single(), force: true);
                await WaitUntilAsync(() => manager.IsPaused && GetSawatariOptions().Count == 1
                    && GetSawatariOptions()[0].IsVisibleInTree() && GetSawatariOptions()[0].IsEnabled,
                    "Duel reward choice missing", ct);
                Require(!player.Relics.OfType<BioBambooRelic>().Any(),
                    "Duel victory granted Bio-Bamboo before manual collection.");
                await WaitFrames(90);
                _tree.Root.GetTexture().GetImage().SavePng(Path.Combine(directory, label + "-relic.png"));
                await UiHelper.Click(GetSawatariOptions()[0]);
            }
            await WaitUntilAsync(() => !manager.IsInProgress && run.CurrentRoom is CombatRoom { IsPreFinished: true },
                "Reward choice did not finish native victory handling", ct);
            Require(!state.Enemies.Any(e => e.IsAlive) && !manager.IsPaused, "Reward choice left a live enemy or pause.");
            if (scenario.Duel)
            {
                await WaitUntilAsync(() => NOverlayStack.Instance?.Peek() is NRewardsScreen,
                    "Duel did not show native manual relic rewards", ct);
                var rewardScreen = (NRewardsScreen)NOverlayStack.Instance!.Peek()!;
                var rewards = UiHelper.FindAll<NRewardButton>(rewardScreen).ToArray();
                Require(rewards.Length == 2 && rewards.All(button => button.Reward is RelicReward)
                    && rewards.Count(button => button.Reward is RelicReward { Relic: BioBambooRelic }) == 1,
                    "Duel must offer Bamboo and one random relic.");
                await WaitFrames(60);
                _tree.Root.GetTexture().GetImage().SavePng(Path.Combine(directory, label + "-reward.png"));
                if (scenario.Act == 1) await UiHelper.Click(rewards.Single(button => button.Reward is RelicReward { Relic: BioBambooRelic }));
                await UiHelper.Click(UiHelper.FindFirst<MegaCrit.Sts2.Core.Nodes.CommonUi.NProceedButton>(rewardScreen)!);
            }
            Require(player.Relics.OfType<BioBambooRelic>().Count() == (scenario.Duel && scenario.Act == 1 ? 1 : 0),
                "Manual Bamboo reward did not respect collection or skipping.");
            _checkpoints.Write("sawatari.cleanup." + label);
        }
        _checkpoints.Write("sawatari.cleanup-completed");
    }
}
