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
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;

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
        MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
        string directory = System.Environment.GetEnvironmentVariable("NINJASLAYER_MULTIPLAYER_DIRECTORY")!;
        ushort port = ushort.Parse(System.Environment.GetEnvironmentVariable("NINJASLAYER_MULTIPLAYER_PORT")!);
        var version = new PeerVersionInfo
        {
            version = Metadata(typeof(ShurikenOrb).Assembly, "NinjaSlayerGameApiVersion"), idDatabaseHash = ModelIdSerializationCache.Hash,
            gameplayAffectingMods = ["NinjaSlayer"], otherMods = []
        };
        if (role == "host")
        {
            var host = new NetHostGameService(version);
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
            var client = new NetClientGameService(version);
            var transport = new ENetClient(client);
            client.Initialize(transport, PlatformType.None);
            _network = client;
            Require(await transport.ConnectToHost(2, "127.0.0.1", port) is null, "Client transport connection failed.");
            await WaitNetwork(() => client.IsConnected, "host handshake");
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
            (combat.State.CreateCard<PreparedShurikenRedesignV1>(first), null),
            (combat.State.CreateCard<PreparedShurikenRedesignV1>(second), null),
            (combat.State.CreateCard<Dualcast>(first), null),
            (combat.State.CreateCard<Dualcast>(second), null),
            (combat.State.CreateCard<ChopStrikeRedesignV1>(second), combat.Enemy),
            (combat.State.CreateCard<ChopStrikeRedesignV1>(second), combat.Enemy),
            (combat.State.CreateCard<NarakuFormRedesignV1>(first), null),
            (combat.State.CreateCard<StrikeNinjaSlayerRedesignV1>(second), combat.Enemy),
            (combat.State.CreateCard<StrikeNinjaSlayerRedesignV1>(first), combat.Enemy)
        };
        plays[0].Card.UpgradeInternal();
        plays[5].Card.AddKeyword(CardKeyword.Exhaust);
        PileType?[] destinations = [PileType.Discard, PileType.Discard, PileType.Discard,
            PileType.Discard, PileType.Hand, PileType.Exhaust, null, PileType.Discard, PileType.Exhaust];
        foreach (var (card, _) in plays) await CardPileCmd.Add(card, PileType.Hand);
        System.IO.File.WriteAllText(Path.Combine(directory, role + ".ready"), "ready");
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
        Require(first.PlayerCombatState!.OrbQueue.Orbs.OfType<ShurikenOrb>().Single().StackCount == 1
            && !second.PlayerCombatState!.OrbQueue.Orbs.OfType<ShurikenOrb>().Any(),
            "One player's evoke consumed another player's stock.");
        Require(first.Creature.HasPower<NarakuFormRedesignPower>() && !second.Creature.HasPower<NarakuFormRedesignPower>()
            && plays[7].Card.Pile?.Type == PileType.Discard && plays[8].Card.Pile?.Type == PileType.Exhaust,
            "Naraku Form crossed player ownership or lost its exhaust behavior.");
        Require(PileType.Draw.GetPile(first).Cards.OfType<BlackFlameRedesignV1>().Count() == 1
            && !PileType.Draw.GetPile(second).Cards.OfType<BlackFlameRedesignV1>().Any(),
            "Black Flame generation crossed player ownership.");
        var snapshot = new
        {
            EnemyHp = new[] { combat.Enemy.CurrentHp, otherEnemy.CurrentHp },
            Players = run.Players.Select(player => new
            {
                player.NetId, player.PlayerCombatState!.Energy,
                Orbs = player.PlayerCombatState.OrbQueue.Orbs.OfType<ShurikenOrb>().Select(orb => new { orb.StackCount, orb.OwnsTransientSlot }),
                Cards = player.PlayerCombatState.AllCards.Select(card => new { Id = card.Id.ToString(), Pile = card.Pile?.Type.ToString() })
            })
        };
        System.IO.File.WriteAllText(Path.Combine(directory, role + ".json"), JsonSerializer.Serialize(snapshot));
        await WaitNetwork(() => System.IO.File.Exists(Path.Combine(directory, "host.json"))
            && System.IO.File.Exists(Path.Combine(directory, "client.json")), "both final snapshots");
        Require(System.IO.File.ReadAllText(Path.Combine(directory, "host.json"))
            == System.IO.File.ReadAllText(Path.Combine(directory, "client.json")), "Host/client product states diverged.");
        GD.Print("PASS two-process native ENet/action-queue stock, multi-evoke, Naraku ownership, card destinations and RNG agreement");
    }

    private static async Task WaitNetwork(Func<bool> predicate, string operation)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!predicate()) await Task.Delay(10, timeout.Token);
        GD.Print($"PASS multiplayer {operation}");
    }
}
