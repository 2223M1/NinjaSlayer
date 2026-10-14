using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyOctoberFeedbackCardsAndEvents()
    {
        VerifyCardMetadata();
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat(ninjaSlayer: true);
            var card = AddCard<FlameGuard>(combat, upgraded: upgraded);
            await CardCmd.AutoPlay(Choice, card, null);
            Require(combat.Player.Creature.Block == (upgraded ? 12 : 10)
                && combat.Player.Creature.GetPowerAmount<NarakuLifePower>() == (upgraded ? 5 : 4)
                && PileType.Draw.GetPile(combat.Player).Cards.OfType<BlackFlame>().Count() == 1,
                "Flame Guard must grant Block, Naraku Life and one Black Flame.");
            var tea = AddCard<Chado>(combat, upgraded: upgraded);
            tea.IncreaseEnergy(3);
            int before = combat.Player.PlayerCombatState!.Energy;
            Require(tea.EnergyCost.GetWithModifiers(CostModifiers.Local) == (upgraded ? 0 : 1),
                "Chado upgrade must reduce cost without changing accumulated energy.");
            await CardCmd.AutoPlay(Choice, tea, null);
            Require(combat.Player.PlayerCombatState.Energy == before + 4 && tea.Pile?.Type == PileType.Exhaust,
                "Upgraded Chado must retain breathing energy and native Exhaust.");
        }
        GD.Print("PASS October cards: Flame Guard 10/12 Block + 4/5 Naraku + Black Flame; Chado 1/0 cost and preserved energy.");

        string hostLoc = System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_HOST_LOCALIZATION")
            ?? throw new InvalidOperationException("Specify exact host localization for event contracts.");
        LocManager.Instance.SetLanguage("zhs");
        foreach (string table in new[] { "events", "characters" })
            LocManager.Instance.GetTable(table).MergeWith(JsonSerializer.Deserialize<Dictionary<string, string>>(
                System.IO.File.ReadAllText(Path.Combine(hostLoc, "zhs", table + ".json")))!);
        using (var combat = new OrbCombat(ninjaSlayer: true))
        {
            var philosophers = ModelDb.Event<ColorfulPhilosophers>().ToMutable();
#if NINJASLAYER_CHANNEL_STABLE
            await philosophers.BeginEvent(combat.Player, false);
#else
            await philosophers.BeginEvent(combat.Player, null, false);
#endif
            Require(philosophers.CurrentOptions.Count == 3 && philosophers.CurrentOptions.All(o => o.Description != null),
                "Native Colorful Philosophers must offer three valid options for NinjaSlayer.");
        }
        GD.Print("PASS Colorful Philosophers: native option generation with NinjaSlayer and RitsuLib.");
    }

    private async Task VerifySurvivingReverseFinisherTornado(OrbCombat combat, NCreature survivor, NCreature enemy)
    {
        var harmony = new Harmony("NinjaSlayer.OrbContracts.ReverseTornado");
        _hurtRoom = new FreeInputContractRoom { Size = new(1280, 720) };
        foreach (string property in new[] { "SceneContainer", "CombatVfxContainer", "BackCombatVfxContainer" })
        {
            var control = new Control { Size = _hurtRoom.Size };
            _hurtRoom.AddChild(control);
            AccessTools.Property(typeof(NCombatRoom), property).SetValue(_hurtRoom, control);
        }
        AddChild(_hurtRoom);
        var background = new Control { Name = "BgContainer" };
        _hurtRoom.SceneContainer.AddChild(background);
        background.Owner = _hurtRoom;
        background.UniqueNameInOwner = true;
        harmony.Patch(AccessTools.PropertyGetter(typeof(NCombatRoom), "Instance"),
            prefix: new HarmonyMethod(GetType(), nameof(ResolveHurtRoom)));
        harmony.Patch(AccessTools.Method(typeof(NCombatRoom), nameof(NCombatRoom.GetCreatureNode), [typeof(Creature)]),
            prefix: new HarmonyMethod(GetType(), nameof(ResolveHurtActor)));
        var assembly = typeof(HellTornadoPower).Assembly;
        Type requestType = assembly.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherSessionRequest", true)!;
        Type sessionType = assembly.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherSession", true)!;
        Type registryType = assembly.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherSessionRegistry", true)!;
        Type scenarioType = assembly.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherScenarioKind", true)!;
        Type conditionType = assembly.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherCompletionCondition", true)!;
        Node2D aim = survivor.Visuals.GetNode<Node2D>("%AimPose");
        Node2D tornado = (Node2D)AccessTools.Property(aim.GetType(), "HellTornado").GetValue(aim)!;
        var oldSpeed = SaveManager.Instance.PrefsSave.FastMode;
        object? session = null;
        try
        {
            foreach (var speed in new[] { FastModeType.Normal, FastModeType.Fast, FastModeType.Instant })
            {
                SaveManager.Instance.PrefsSave.FastMode = speed;
                await PowerCmd.Apply<SoarPower>(Choice, survivor.Entity, 1, survivor.Entity, null);
                var power = await PowerCmd.Apply<HellTornadoPower>(Choice, survivor.Entity, 1, survivor.Entity, null);
                Require(CombatCinematicCameraLease.TryAcquire(_hurtRoom, "reverse-tornado-contract", out var camera),
                    "Reverse finisher fixture could not acquire its camera.");
                object request = Activator.CreateInstance(requestType,
                    Enum.Parse(scenarioType, "EnemyExecutesNinjaSlayer"), Enum.Parse(conditionType, "AnyCandidateLethal"),
                    enemy.Entity, enemy, survivor, new[] { survivor.Entity }, camera!, null, false, 1, null, null, 0, false)!;
                object?[] args = [request, combat.State, _hurtRoom, null];
                Require((bool)AccessTools.Method(registryType, "TryRegisterSession").Invoke(null, args)!, "Finisher registration failed.");
                session = args[3]!;
                AccessTools.Method(sessionType, "Begin").Invoke(session, null);
                // Forecasts may be invalidated by native defenses/another hook. The
                // victim is still alive, and presentation must not remove its power.
                Require((bool)AccessTools.Property(tornado.GetType(), "Active").GetValue(tornado)!,
                    $"Reverse finisher disabled the surviving player's Hell Tornado in {speed}.");
                await (Task)AccessTools.Method(sessionType, "CancelAsync").Invoke(session, null)!;
                session = null;
                Task nextTurn = power!.AfterAutoPrePlayPhaseEnteredEarly(Choice, combat.Player);
                Task timeout = Task.Delay(3000);
                Require(await Task.WhenAny(nextTurn, timeout) == nextTurn, "Surviving Hell Tornado blocked the next turn.");
                await nextTurn;
                Require(!survivor.Entity.HasPower<HellTornadoPower>() && !survivor.Entity.HasPower<SoarPower>(),
                    "Hell Tornado did not remove itself and Soar after landing.");
            }
        }
        finally
        {
            if (session != null) await (Task)AccessTools.Method(sessionType, "CancelAsync").Invoke(session, null)!;
            SaveManager.Instance.PrefsSave.FastMode = oldSpeed;
            _hurtRoom.Free(); _hurtRoom = null;
            harmony.UnpatchAll(harmony.Id);
        }
        GD.Print("PASS reverse finisher survivor: Hell Tornado remains active, then ends and lands in Normal/Fast/Instant.");
    }
}
