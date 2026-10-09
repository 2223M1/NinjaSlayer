using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Cards;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;
using NinjaSlayer.Relics;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private INetGameService? _network;

    public override void _Process(double delta) => _network?.Update();

    private async Task VerifyMultiplayer(string role)
    {
        MessageTypes.Initialize();
        ActionTypes.Initialize();
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.InitSettingsDataForTest();
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.InitPrefsDataForTest();
        MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
        string directory = System.Environment.GetEnvironmentVariable("NINJASLAYER_MULTIPLAYER_DIRECTORY")!;
        ushort port = ushort.Parse(System.Environment.GetEnvironmentVariable("NINJASLAYER_MULTIPLAYER_PORT")!);
#if !NINJASLAYER_CHANNEL_STABLE
        var version = new PeerVersionInfo
        {
            version = Metadata(typeof(ShurikenOrb).Assembly, "NinjaSlayerGameApiVersion"), idDatabaseHash = ModelIdSerializationCache.Hash,
            gameplayAffectingMods = ["NinjaSlayer"], otherMods = []
        };
#endif
        if (role == "host")
        {
#if NINJASLAYER_CHANNEL_STABLE
            var host = new NetHostGameService();
#else
            var host = new NetHostGameService(version);
#endif
            var transport = new ENetHost(host);
            // Native ENetHost hardcodes 0.0.0.0; bind this test's socket to loopback only.
            var connection = new ENetConnection();
            Require(connection.CreateHostBound("127.0.0.1", port, 1) == Error.Ok, "Loopback port is unavailable.");
            AccessTools.Field(typeof(ENetHost), "_connection").SetValue(transport, connection);
            AccessTools.Field(typeof(ENetHost), "_isConnected").SetValue(transport, true);
            AccessTools.Field(typeof(NetHostGameService), "_netHost").SetValue(host, transport);
            _network = host;
            System.IO.File.WriteAllText(Path.Combine(directory, "listening"), "ready");
            await WaitNetwork(() => host.ConnectedPeers.Count == 1, "client handshake");
            host.SetPeerReadyForBroadcasting(2);
        }
        else
        {
            await WaitNetwork(() => System.IO.File.Exists(Path.Combine(directory, "listening")), "host startup");
#if NINJASLAYER_CHANNEL_STABLE
            var client = new NetClientGameService();
#else
            var client = new NetClientGameService(version);
#endif
            var transport = new ENetClient(client);
            client.Initialize(transport, PlatformType.None);
            _network = client;
            Require(await transport.ConnectToHost(2, "127.0.0.1", port) is null, "Client transport connection failed.");
            await WaitNetwork(() => client.IsConnected, "host handshake");
        }

        if (System.Environment.GetEnvironmentVariable("NINJASLAYER_MULTIPLAYER_GREETING_ONLY") == "1")
        {
            await VerifyGreetingBarrier(role, directory);
            return;
        }
        using var combat = new OrbCombat(ninjaSlayer: true);
        Player first = combat.Player;
        Player second = Player.CreateForNewRun<NinjaSlayerCharacter>(UnlockState.all, 2);
        second.InitializeSeed("second-player");
        combat.State.AddPlayer(second);
        second.ResetCombatState();
        Creature otherEnemy = combat.AddEnemy();
        var run = RunState.CreateForTest([first, second], seed: "multiplayer-contract");
        RunManager.Instance.SetUpTest(run, _network);
        MegaCrit.Sts2.Core.Context.LocalContext.NetId = _network.NetId;
        NetCombatCardDb.Instance.StartCombat(run.Players);
        foreach (Player player in run.Players)
        {
            player.PlayerCombatState!.Phase = PlayerTurnPhase.Play;
            await PlayerCmd.SetEnergy(30, player);
        }
        RunManager.Instance.ActionQueueSynchronizer.SetCombatState(ActionSynchronizerCombatState.PlayPhase);
        RunManager.Instance.ActionExecutor.Unpause();
        int completed = 0;
        RunManager.Instance.ActionExecutor.AfterActionExecuted += _ => completed++;
        var plays = new (CardModel Card, Creature? Target)[]
        {
            (combat.State.CreateCard<ReadyShuriken>(first), null),
            (combat.State.CreateCard<ReadyShuriken>(second), null),
            (combat.State.CreateCard<Zap>(first), null),
            (combat.State.CreateCard<Dualcast>(first), null),
            (combat.State.CreateCard<Dualcast>(second), null),
            (combat.State.CreateCard<StrikeStrike>(second), combat.Enemy),
            (combat.State.CreateCard<StrikeStrike>(second), combat.Enemy),
            (combat.State.CreateCard<NarakuForm>(first), null),
            (combat.State.CreateCard<StrikeNinjaSlayer>(second), combat.Enemy),
            (combat.State.CreateCard<StrikeNinjaSlayer>(first), combat.Enemy)
        };
        plays[0].Card.UpgradeInternal();
        PileType?[] destinations = [PileType.Discard, PileType.Discard, PileType.Discard,
            PileType.Discard, PileType.Discard, PileType.Exhaust, PileType.Exhaust, null, PileType.Discard, PileType.Discard];
        foreach (var (card, _) in plays) await CardPileCmd.Add(card, PileType.Hand);
        System.IO.File.WriteAllText(Path.Combine(directory, role + ".ready"), "ready");
        using var openingSelector = CardSelectCmd.UseSelector(new SelectCards(_ => []));
        await WaitNetwork(() => System.IO.File.Exists(Path.Combine(directory, "host.ready"))
            && System.IO.File.Exists(Path.Combine(directory, "client.ready")), "both combat fixtures");
        for (int step = 0; step < plays.Length; step++)
        {
            var (card, target) = plays[step];
            Require(card.CanPlay(out var reason, out _) && card.IsValidTarget(target),
                $"Invalid test play {card.Id}: {reason}, target {target}.");
            if (card.Owner.NetId == _network.NetId)
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(card, target));
            int expected = step + 1;
            await WaitNetwork(() => completed >= expected, $"native card action {expected}");
            Require(card.Pile?.Type == destinations[step],
                $"Action {expected} resolved to the wrong pile.");
        }
        Require(first.PlayerCombatState!.OrbQueue.Orbs.OfType<ShurikenOrb>().Single().StackCount == 2
            && first.PlayerCombatState.OrbQueue.Capacity == 1 && second.PlayerCombatState!.OrbQueue.Capacity == 0
            && !second.PlayerCombatState!.OrbQueue.Orbs.OfType<ShurikenOrb>().Any(),
            "Mixed normal/dedicated priority or multiplayer stock ownership changed.");
        Require(PileType.Hand.GetPile(second).Cards.OfType<StrikeStrike>().Count() == 2
            && !PileType.Hand.GetPile(first).Cards.OfType<StrikeStrike>().Any(),
            "Strike Strike must generate two fresh cards for the playing owner on both peers.");
        Require(first.Creature.HasPower<NarakuFormPower>() && !second.Creature.HasPower<NarakuFormPower>()
            && plays[8].Card.Pile?.Type == PileType.Discard && plays[9].Card.Pile?.Type == PileType.Discard,
            "Naraku Form crossed player ownership or changed the attack destination.");
        Require(PileType.Draw.GetPile(first).Cards.OfType<BlackFlame>().Count() == 0
            && !PileType.Draw.GetPile(second).Cards.OfType<BlackFlame>().Any(),
            "Naraku must no longer generate Black Flame cards.");
        int glamCase = 0;
        foreach (Player caster in new[] { first, second })
        foreach (bool upgraded in new[] { false, true })
        {
            foreach (Player player in run.Players)
            {
                foreach (var old in player.Piles.Where(p => p.Type != PileType.Deck).SelectMany(p => p.Cards).ToArray())
                    await CardPileCmd.RemoveFromCombat(old);
                for (int i = 0; i < 12; i++)
                    await CardPileCmd.Add(combat.State.CreateCard<DefendIronclad>(player), PileType.Draw);
                await PlayerCmd.SetEnergy(30, player);
            }
            Player teammate = caster == first ? second : first;
            var kindle = combat.State.CreateCard<Kindle>(caster);
            if (upgraded) kindle.UpgradeInternal();
            CardCmd.Enchant<Glam>(kindle, 1);
            var next = combat.State.CreateCard<DefendNinjaSlayer>(teammate);
            await CardPileCmd.Add(kindle, PileType.Hand);
            await CardPileCmd.Add(next, PileType.Hand);
            int expected = completed + 1;
            string fixture = ".glam-" + glamCase++;
            System.IO.File.WriteAllText(Path.Combine(directory, role + fixture), "ready");
            await WaitNetwork(() => System.IO.File.Exists(Path.Combine(directory, "host" + fixture))
                && System.IO.File.Exists(Path.Combine(directory, "client" + fixture)), "both Glam fixtures");
            if (caster.NetId == _network.NetId)
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(kindle, null));
            await WaitNetwork(() => completed >= expected, "Glam Kindle owner " + caster.NetId);
            Require(kindle.Pile?.Type == PileType.Discard && PileType.Hand.GetPile(caster).Cards.Count == (upgraded ? 6 : 4)
                && PileType.Discard.GetPile(caster).Cards.OfType<BlackFlame>().Count() == 2,
                "Glam Kindle repeated draws or generated card owner differ between peers.");
            expected++;
            if (teammate.NetId == _network.NetId)
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(next, null));
            await WaitNetwork(() => completed >= expected, "teammate following Glam Kindle");
            Require(next.Pile?.Type == PileType.Discard, "Teammate queue did not resolve after Glam Kindle.");
        }
        GD.Print("PASS native ENet Glam Kindle: both owners, base+upgrade, two replays, owned draws/statuses and teammate continuation.");
        openingSelector.Dispose();
#if !NINJASLAYER_CHANNEL_STABLE
        int localSelections = 0;
        bool selectCombatPile = false;
        using var selector = CardSelectCmd.UseSelector(new SelectCards(options =>
        {
            localSelections++;
            Player owner = options[0].Owner;
            var synchronizer = RunManager.Instance.PlayerChoiceSynchronizer;
            uint choiceId = synchronizer.ChoiceIds[run.Players.ToList().IndexOf(owner)] - 1;
            // Native LocalSelector bypasses the UI branch that sends its result.
            synchronizer.SyncLocalChoice(owner, choiceId, selectCombatPile || options[0].Pile?.Type == PileType.Hand
                ? PlayerChoiceResult.FromMutableCombatCards(options.Take(1))
                : PlayerChoiceResult.FromIndexes([0]));
            return options.Take(1);
        }), localOnly: true);
        foreach (Player player in run.Players)
        {
            foreach (CardModel card in PileType.Hand.GetPile(player).Cards.ToArray())
                await CardPileCmd.Add(card, PileType.Discard);
            var discard = combat.State.CreateCard<Jujutsu>(player);
            var sly = combat.State.CreateCard<ShurikenCreation>(player);
            var nested = combat.State.CreateCard<ShurikenCreation>(player);
            var last = combat.State.CreateCard<DefendIronclad>(player);
            await CardPileCmd.Add(discard, PileType.Hand);
            await CardPileCmd.Add(sly, PileType.Hand);
            await CardPileCmd.Add(combat.State.CreateCard<DefendIronclad>(player), PileType.Hand);
            await CardPileCmd.Add(last, PileType.Draw, CardPilePosition.Top);
            await CardPileCmd.Add(nested, PileType.Draw, CardPilePosition.Top);
            int before = completed;
            string fixture = $"choices-{player.NetId}";
            System.IO.File.WriteAllText(Path.Combine(directory, $"{role}.{fixture}"), "ready");
            await WaitNetwork(() => System.IO.File.Exists(Path.Combine(directory, $"host.{fixture}"))
                && System.IO.File.Exists(Path.Combine(directory, $"client.{fixture}")), "both choice fixtures");
            if (player.NetId == _network.NetId)
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(discard, null));
            await WaitNetwork(() => completed > before, $"player {player.NetId} hand discard and nested Scry choices");
            Require(sly.Pile?.Type == PileType.Discard && nested.Pile?.Type == PileType.Discard
                && last.Pile?.Type == PileType.Discard && discard.Pile?.Type == PileType.Exhaust,
                "Native synchronized choices must resolve both Sly cards and the nested discard.");
            Require(player.PlayerCombatState!.OrbQueue.Orbs.OfType<ShurikenOrb>().Single().StackCount == 6,
                "Nested discard chains must finish before each Sly card replenishes its stock.");
        }
        Require(localSelections == 3, "Only the local player may make the three synchronized choices.");
        GD.Print("PASS synchronized hand discard and nested Scry/Sly choices");
        foreach (Player player in run.Players)
        {
            var kick = combat.State.CreateCard<HalfMoonCompassKick>(player);
            var tea = combat.State.CreateCard<Chado>(player);
            var spare = combat.State.CreateCard<Chado>(player);
            await CardPileCmd.Add(kick, PileType.Hand);
            await CardPileCmd.Add(tea, PileType.Hand);
            await CardPileCmd.Add(spare, PileType.Hand);
            int before = completed;
            string fixture = $"halfmoon-{player.NetId}";
            System.IO.File.WriteAllText(Path.Combine(directory, $"{role}.{fixture}"), "ready");
            await WaitNetwork(() => System.IO.File.Exists(Path.Combine(directory, $"host.{fixture}"))
                && System.IO.File.Exists(Path.Combine(directory, $"client.{fixture}")), "both Half-Moon fixtures");
            if (player.NetId == _network.NetId)
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(kick, null));
            await WaitNetwork(() => completed > before, "native Half-Moon selection");
            Require(tea.Pile?.Type == PileType.Exhaust && spare.Pile?.Type == PileType.Hand
                && kick.Pile?.Type == PileType.Hand && kick.DynamicVars.Damage.BaseValue == 20,
                "Half-Moon must synchronize exactly one selected tea, damage doubling and return on both peers.");
        }
        Require(localSelections == 4, "Half-Moon may prompt only its local owner.");
        GD.Print("PASS synchronized Half-Moon exact-one tea selection, doubling and return for both players");
        selectCombatPile = true;
        foreach (Player player in run.Players)
        {
            foreach (var old in player.Piles.Where(p => p.Type != PileType.Deck).SelectMany(p => p.Cards).ToArray())
                await CardPileCmd.RemoveFromCombat(old);
            await PowerCmd.Apply<ResiliencePower>(Choice, player.Creature, 1, player.Creature, null);
            await PowerCmd.Apply<StratagemPower>(Choice, player.Creature, 1, player.Creature, null);
            for (int i = 0; i < 3; i++) await CardPileCmd.Add(combat.State.CreateCard<DefendIronclad>(player), PileType.Discard);
            var card = combat.State.CreateCard<NarakusMight>(player);
            await CardPileCmd.Add(card, PileType.Hand);
            await PlayerCmd.SetEnergy(30, player);
            int before = completed;
            string fixture = $"resilience-{player.NetId}";
            File.WriteAllText(Path.Combine(directory, role + "." + fixture), "ready");
            await WaitNetwork(() => File.Exists(Path.Combine(directory, "host." + fixture))
                && File.Exists(Path.Combine(directory, "client." + fixture)), "both Resilience fixtures");
            if (player.NetId == _network.NetId)
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(card, combat.Enemy));
            await WaitNetwork(() => completed >= before + 2, "Resilience draw and native Stratagem hook action");
            Require(PileType.Hand.GetPile(player).Cards.Count == 3 && PileType.Hand.GetPile(player).Cards.OfType<BlackFlame>().Count() == 1,
                "Resilience shuffle/selection lost a draw, duplicated status generation or left the action paused.");
            await PowerCmd.Remove(player.Creature.GetPower<ResiliencePower>()!);
            await PowerCmd.Remove(player.Creature.GetPower<StratagemPower>()!);
        }
        Require(localSelections == 5, "Resilience's native selection must be made only by the owning peer.");
        GD.Print("PASS synchronized Resilience status generation, empty-deck shuffle, Stratagem choice and both action queues.");
#endif
        foreach (Player player in run.Players)
        {
            await AddStock(player, 2);
            await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.FocusPower>(Choice, player.Creature, 2, player.Creature, null);
            int stock = player.PlayerCombatState!.OrbQueue.Orbs.OfType<ShurikenOrb>().Single().StackCount;
            foreach (CardModel card in new CardModel[]
            {
                combat.State.CreateCard<StarlessNight>(player),
                combat.State.CreateCard<Moonsault>(player)
            })
            {
                await CardPileCmd.Add(card, PileType.Hand);
                int before = completed;
                string fixture = $"conversion-{player.NetId}-{card.Id.Entry}";
                System.IO.File.WriteAllText(Path.Combine(directory, $"{role}.{fixture}"), "ready");
                await WaitNetwork(() => System.IO.File.Exists(Path.Combine(directory, $"host.{fixture}"))
                    && System.IO.File.Exists(Path.Combine(directory, $"client.{fixture}")), "both conversion fixtures");
                if (player.NetId == _network.NetId)
                    RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(card, null));
                await WaitNetwork(() => completed > before, "native converted volley action");
                if (card is StarlessNight) await AddStock(player, 1);
            }
            var tokens = player.PlayerCombatState.AllCards.OfType<StrongShuriken>().ToArray();
            Require(tokens.Length == 1 && tokens.All(card => card.SnapshotDamage == 8),
                "One stock grant creates one synchronized eight-damage snapshot token.");
        }
        foreach (Player player in run.Players)
        {
            foreach (var card in CardPile.GetCards(player, PileType.Hand, PileType.Draw).ToArray())
                await CardPileCmd.Add(card, PileType.Discard);
            await AddStock(player, 3);
            int stock = player.PlayerCombatState!.OrbQueue.Orbs.OfType<ShurikenOrb>().Single().StackCount;
            int tokens = player.PlayerCombatState.AllCards.OfType<StrongShuriken>().Count();
            await CardPileCmd.Add(combat.State.CreateCard<Wound>(player), PileType.Draw);
            await CardPileCmd.Add(combat.State.CreateCard<Wound>(player), PileType.Draw);
            await CardPileCmd.Add(combat.State.CreateCard<DefendIronclad>(player), PileType.Draw);
            await CardPileCmd.Add(combat.State.CreateCard<DefendIronclad>(player), PileType.Draw);
            var burning = combat.State.CreateCard<PressTheAttack>(player);
            await CardPileCmd.Add(burning, PileType.Hand);
            await PlayerCmd.SetEnergy(10, player);
            int before = completed;
            string fixture = $"burning-batch-{player.NetId}";
            System.IO.File.WriteAllText(Path.Combine(directory, $"{role}.{fixture}"), "ready");
            await WaitNetwork(() => System.IO.File.Exists(Path.Combine(directory, $"host.{fixture}"))
                && System.IO.File.Exists(Path.Combine(directory, $"client.{fixture}")), "both batch fixtures");
            if (player.NetId == _network.NetId)
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(burning, combat.Enemy));
            await WaitNetwork(() => completed > before, "native Burning Blood batch");
            Require(player.PlayerCombatState.AllCards.OfType<StrongShuriken>().Count() == tokens
                && !player.PlayerCombatState.OrbQueue.Orbs.OfType<ShurikenOrb>().Any(),
                "Discard batches consume stock without generating tokens.");
        }
        GD.Print("PASS synchronized Burning Blood batch cap and independent player allowances");
        var sawatari = (ForestSawatariMonster)ModelDb.Monster<ForestSawatariMonster>().ToMutable();
        sawatari.ActThree = true;
        Creature weaponEnemy = combat.State.CreateCreature(sawatari, CombatSide.Enemy, null);
        await CreatureCmd.Add(weaponEnemy);
        foreach (Player player in run.Players)
        {
            player.Creature.SetMaxHpInternal(500);
            await CreatureCmd.SetCurrentHp(player.Creature, 500);
            foreach (CardModel card in PileType.Hand.GetPile(player).Cards.ToArray())
                await CardPileCmd.Add(card, PileType.Discard);
        }
        sawatari.RollMove(combat.State.PlayerCreatures);
        for (int move = 0; move < 3; move++)
        {
            await sawatari.PerformMove();
            sawatari.RollMove(combat.State.PlayerCreatures);
        }
        Machete[] machetes = run.Players.SelectMany(player => player.PlayerCombatState!.AllCards)
            .OfType<Machete>().ToArray();
        Require(machetes.Length == 2 && sawatari.MacheteCount == 0,
            "Two networked throws must transfer exactly two knives across all players.");
        var caughtHands = machetes.Select(card => new { card.Owner.NetId, card.HeldHand }).ToArray();
        var returnedHands = new List<int>();
        for (int index = 0; index < machetes.Length; index++)
        {
            Machete card = machetes[index];
            await CardPileCmd.Add(card, PileType.Hand);
            await PlayerCmd.SetEnergy(10, card.Owner);
            int before = completed;
            string fixture = $"machete-{index}";
            System.IO.File.WriteAllText(Path.Combine(directory, $"{role}.{fixture}"), "ready");
            await WaitNetwork(() => System.IO.File.Exists(Path.Combine(directory, $"host.{fixture}"))
                && System.IO.File.Exists(Path.Combine(directory, $"client.{fixture}")), "both machete fixtures");
            if (card.Owner.NetId == _network.NetId)
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(card, weaponEnemy));
            await WaitNetwork(() => completed > before, "native machete return action");
            Require(card.Pile?.Type == PileType.Exhaust && sawatari.MacheteCount == index + 1,
                "A synchronized Machete play must exhaust and return one knife.");
            returnedHands.Add(sawatari.HeldMachetes);
        }
        Require(sawatari.NextMove.Id == ForestSawatariMonster.DualMoveId,
            "Returning both networked knives must immediately show the dual-attack intent.");
        foreach (Player player in run.Players)
        {
            await RelicCmd.Obtain<BioBambooSplintRelic>(player);
            await RelicCmd.Obtain<BeppinShardRelic>(player);
        }
        foreach (Player player in run.Players)
        {
            var bamboo = player.Relics.OfType<BioBambooSplintRelic>().Single();
            for (int index = 0; index < 2; index++)
            {
                var card = combat.State.CreateCard<StrikeIronclad>(player);
                await CardPileCmd.Add(card, PileType.Hand);
                await PlayerCmd.SetEnergy(10, player);
                int before = completed;
                string fixture = $"bamboo-{player.NetId}-{index}";
                System.IO.File.WriteAllText(Path.Combine(directory, $"{role}.{fixture}"), "ready");
                await WaitNetwork(() => System.IO.File.Exists(Path.Combine(directory, $"host.{fixture}"))
                    && System.IO.File.Exists(Path.Combine(directory, $"client.{fixture}")), "both relic fixtures");
                if (player.NetId == _network.NetId)
                    RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(card, combat.Enemy));
                await WaitNetwork(() => completed > before, "native Bamboo attack");
                Require(bamboo.DisplayAmount == (index == 0 ? 1 : 0), "Networked Bamboo counter differs.");
            }
            Require(player.Creature.GetPowerAmount<PlatingPower>() == 1, "Networked Bamboo did not grant Plating to its owner.");
            Player other = run.Players.Single(candidate => candidate != player);
            decimal otherKarate = other.Creature.GetPowerAmount<KaratePower>();
            decimal karate = player.Creature.GetPowerAmount<KaratePower>();
            await PowerCmd.Apply<NarakuLifePower>(Choice, player.Creature, 20, player.Creature, null);
            await CreatureCmd.Damage(Choice, player.Creature, 1, ValueProp.Unpowered | ValueProp.Unblockable, player.Creature);
            await CreatureCmd.Damage(Choice, player.Creature, 1, ValueProp.Unpowered | ValueProp.Unblockable, player.Creature);
            Require(player.Creature.GetPowerAmount<KaratePower>() == karate + 7
                && other.Creature.GetPowerAmount<KaratePower>() == otherKarate,
                "Networked Fragment repeated or crossed player ownership.");
        }
        var snapshot = new
        {
            CaughtHands = caughtHands, ReturnedHands = returnedHands,
            Sawatari = new { weaponEnemy.CurrentHp, sawatari.HeldMachetes, sawatari.MustThrow,
                sawatari.NextThrowHand, Intent = sawatari.NextMove.Id },
            EnemyHp = new[] { combat.Enemy.CurrentHp, otherEnemy.CurrentHp },
            Players = run.Players.Select(player => new
            {
                player.NetId, player.PlayerCombatState!.Energy,
                Relics = player.Relics.Select(relic => new { Id = relic.Id.ToString(), relic.DisplayAmount }),
                Karate = player.Creature.GetPowerAmount<KaratePower>(),
                Plating = player.Creature.GetPowerAmount<PlatingPower>(),
                OrbCapacity = player.PlayerCombatState.OrbQueue.Capacity,
                Orbs = player.PlayerCombatState.OrbQueue.Orbs.Select(orb => new { orb.Id, Stock = (orb as ShurikenOrb)?.StackCount }),
                Cards = player.PlayerCombatState.AllCards.Select(card => new { Id = card.Id.ToString(), Pile = card.Pile?.Type.ToString(),
                    Snapshot = (card as StrongShuriken)?.SnapshotDamage,
                    MacheteHand = (card as Machete)?.HeldHand })
            })
        };
        System.IO.File.WriteAllText(Path.Combine(directory, role + ".json"), JsonSerializer.Serialize(snapshot));
        await WaitNetwork(() => System.IO.File.Exists(Path.Combine(directory, "host.json"))
            && System.IO.File.Exists(Path.Combine(directory, "client.json")), "both final snapshots");
        Require(System.IO.File.ReadAllText(Path.Combine(directory, "host.json"))
            == System.IO.File.ReadAllText(Path.Combine(directory, "client.json")), "Host/client product states diverged.");
        GD.Print("PASS two-process native ENet/action-queue stock, multi-evoke, Naraku ownership and RNG agreement");
        GD.Print("PASS synchronized Starless conversion, per-shot token count and Focus damage snapshots");
        GD.Print("PASS synchronized Sawatari knife transfers, native status plays, exhaust and immediate intents");
        GD.Print("PASS synchronized event relic obtain, native attack counters and Naraku loss ownership");
        await VerifySawatariNetworkTurnBarrier(combat, role, directory);
    }

    private static async Task WaitNetwork(Func<bool> predicate, string operation)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!predicate()) await Task.Delay(10, timeout.Token);
        GD.Print($"PASS multiplayer {operation}");
    }
}
