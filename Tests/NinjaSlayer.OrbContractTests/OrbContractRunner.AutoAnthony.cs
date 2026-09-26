using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;
using NinjaSlayer.Content;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Powers;
using MegaCrit.Sts2.Core.Models.Powers;
using System.Collections;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static Assembly? LoadAnthonyContractAssemblies(Mod productMod)
    {
        string? path = System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_ANTHONY_DLL");
        if (path is null) return null;
#if NINJASLAYER_CHANNEL_STABLE
        throw new NotSupportedException("AutoAnthony 0.3.102 requires the preview host.");
#else
        var context = AssemblyLoadContext.GetLoadContext(typeof(CardModel).Assembly)!;
        Assembly anthony = context.LoadFromAssemblyPath(Path.GetFullPath(path));
        Assembly bridge = context.LoadFromAssemblyPath(Path.GetFullPath(
            System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_ANTHONY_BRIDGE")!));
        productMod.assemblies.Add(bridge);
        ((List<Mod>)AccessTools.Field(typeof(ModManager), "_mods").GetValue(null)!).Add(new Mod
        {
            path = "res://", manifest = new ModManifest { id = "AutoAnthony" },
            state = ModLoadState.Loaded, assemblies = [anthony]
        });
        GD.Print($"Anthony oracle {path}; SHA256 {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}");
        return bridge;
#endif
    }

    private static async Task VerifyAnthonyPool(Assembly bridge)
    {
        MegaCrit.Sts2.Core.Context.LocalContext.NetId = 1;
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.InitSettingsDataForTest();
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.InitPrefsDataForTest();
        MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
        AccessTools.Property(typeof(MegaCrit.Sts2.Core.Runs.RunManager), "NetService").SetValue(
            MegaCrit.Sts2.Core.Runs.RunManager.Instance, new MegaCrit.Sts2.Core.Multiplayer.NetSingleplayerGameService());
        Type run = bridge.GetType("NinjaSlayer.AutoAnthony.NinjaAnthonyRun", true)!;
        Type api = AccessTools.TypeByName("AutoAnthony.ComponentRunSettingsApi");
        object settings = AccessTools.Property(api, "Local").GetValue(null)!;
        AccessTools.Method(run, "Generate").Invoke(null, ["NINJA_ANTHONY_CONTRACT", settings]);
        string Snapshot() => System.Text.Json.JsonSerializer.Serialize(AccessTools.Field(run, "Current").GetValue(null));
        string first = Snapshot();
        AccessTools.Method(run, "Generate").Invoke(null, ["NINJA_ANTHONY_CONTRACT", settings]);
        Require(first == Snapshot(), "The same seed and settings changed the generated pool.");
        var cards = ModelDb.CardPool<NinjaSlayerCardPool>().AllCards.ToArray();
        Require(cards.Count(c => c.GetType().Assembly == bridge) == 80, "Generated rewards must expose 20/35/25 stable slots.");
        var generated = cards.Where(c => c.GetType().Assembly == bridge).ToArray();
        foreach (var card in generated)
        {
            CardModel upgraded = card.ToMutable();
            upgraded.UpgradeInternal();
            upgraded.FinalizeUpgradeInternal();
            Require(upgraded.CurrentUpgradeLevel == 1, "Generated card could not upgrade.");
        }
        GD.Print("PASS optional generated pool: registration, 80 reward slots, same-seed generation and upgrades.");
        object Settings(params (string Name, bool Value)[] overrides)
        {
            var constructor = settings.GetType().GetConstructors().Single(c => c.GetParameters().Length == 9);
            return constructor.Invoke(constructor.GetParameters().Select(p =>
            {
                var change = overrides.SingleOrDefault(pair => string.Equals(pair.Name, p.Name, StringComparison.OrdinalIgnoreCase));
                return change.Name is null ? AccessTools.Property(settings.GetType(), p.Name!).GetValue(settings) : change.Value;
            }).ToArray());
        }
        void Generate(object options) => AccessTools.Method(run, "Generate").Invoke(null, ["NINJA_ANTHONY_CONTRACT", options]);
        Generate(Settings(("Enabled", false)));
        var ordinary = ModelDb.CardPool<NinjaSlayerCardPool>().AllCards.ToArray();
        Require(ordinary.All(c => c.GetType().Assembly != bridge),
            "Disabled Anthony mode replaced the ordinary card pool.");
        Generate(Settings(("PreserveOriginalCards", true), ("ReplaceStartingCards", false)));
        Require(ModelDb.CardPool<NinjaSlayerCardPool>().AllCards.Count() == ordinary.Length + 80
            && ordinary.All(c => ModelDb.CardPool<NinjaSlayerCardPool>().AllCards.Contains(c))
            && ModelDb.Character<NinjaSlayerCharacter>().StartingDeck.All(c => c.GetType().Assembly != bridge),
            "Preserve-original or starting-deck setting was ignored.");
        Generate(Settings(("UltimateChaos", true), ("NumericRandomMode", true), ("RandomCardArt", true)));
        string ultimate = Snapshot();
        Generate(Settings(("UltimateChaos", true), ("NumericRandomMode", true), ("RandomCardArt", true)));
        Require(ultimate == Snapshot() && ultimate.Contains("$original", StringComparison.Ordinal),
            "Ultimate chaos or random-art variants changed on same-seed regeneration.");
        Generate(settings);
        GD.Print("PASS inherited generation settings: disabled mode, preserve originals, native starters, ultimate chaos, numeric randomization and saved art identity.");
        Type nativeApi = AccessTools.TypeByName("AutoAnthony.AutoAnthonyNativeCardApi");
        Type freeformApi = AccessTools.TypeByName("AutoAnthony.AutoAnthonyFreeformCardApi");
        Type generatedType = AccessTools.TypeByName("ChaosCardGenerator.GeneratedCard");
        MethodInfo create = AccessTools.Method(freeformApi, "CreateForCombat",
            [typeof(MegaCrit.Sts2.Core.Entities.Players.Player), typeof(string), generatedType, typeof(string)]);
        object Definition(CardModel source)
        {
            object?[] args = [source, null, null];
            Require((bool)AccessTools.Method(nativeApi, "TryCreateDefinition").Invoke(null, args)!, "Source missing from external decomposition: " + source.Id);
            return args[2]!;
        }
        CardModel Create(OrbCombat arena, CardModel source, bool upgrade = false)
        {
            var card = (CardModel)create.Invoke(null, [arena.Player, "ninjaslayer", Definition(source), source.PortraitPath])!;
            if (upgrade) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
            return card;
        }
        async Task Play(OrbCombat arena, CardModel card)
        {
            await CardPileCmd.Add(card, PileType.Hand);
            await CardCmd.AutoPlay(Choice, card, card.TargetType == TargetType.AnyEnemy ? arena.Enemy : null);
        }

        var rows = (Array)AccessTools.Field(bridge.GetType("NinjaSlayer.AutoAnthony.NinjaComponentSources", true)!, "Sources").GetValue(null)!;
        Require(rows.Length == 84, "Component sources must cover 80 reward cards plus four starting models.");
        using (var arena = new OrbCombat())
        {
            foreach (var row in rows)
            {
                Type type = (Type)row!.GetType().GetProperty("Card")!.GetValue(row)!;
                var source = ModelDb.GetById<CardModel>(ModelDb.GetId(type));
                foreach (bool upgrade in new[] { false, true })
                {
                    var card = Create(arena, source, upgrade);
                    _ = card.DynamicVars;
                    _ = card.Keywords;
                    _ = card.ToSerializable();
                }
            }
        }
        GD.Print("PASS all 84 source definitions: base/upgraded construction and native card serialization.");

        using (var arena = new OrbCombat())
        {
            await Play(arena, Create(arena, ModelDb.Card<KarateStraightRedesignV1>()));
            Require(arena.Enemy.CurrentHp == 992 && arena.Player.Creature.GetPowerAmount<KaratePower>() == 4,
                "Generated Straight Punch must deal 8 and grant 4 Karate.");
        }
        using (var arena = new OrbCombat())
        {
            await Play(arena, Create(arena, ModelDb.Card<PreparedShurikenRedesignV1>(), upgrade: true));
            Require(arena.Stock == 3 && arena.Player.Creature.Block == 7, "Generated upgraded Prepared Shuriken lost source values.");
            await Play(arena, Create(arena, ModelDb.Card<GiantShurikenRedesignV1>()));
            Require(arena.Player.Creature.HasPower<StarlessNightRedesignPower>(), "Fixed nonnumeric power was applied with zero amount.");
            await Play(arena, Create(arena, ModelDb.Card<BladeReserveRedesignV1>()));
            Require(arena.Tokens == 1, "Generated stock gain failed to trigger Starless Night once.");
        }
        using (var arena = new OrbCombat())
        {
            await Play(arena, Create(arena, ModelDb.Card<PlaceholderBlueDefense01>(), upgrade: true));
            Require(arena.Player.Creature.Block == 8 && arena.Player.Creature.GetPowerAmount<ThornsPower>() == 4,
                "Generated upgraded Caltrops lost its timed Thorns or Block.");
            Require(arena.Player.Creature.GetPower<CaltropsDurationPower>()?.ThornsAmount == 4,
                "Timed Thorns receipt must match actual upgraded amount.");
        }
        using (var arena = new OrbCombat())
        {
            await Play(arena, Create(arena, ModelDb.Card<Prejudge>(), upgrade: true));
            Require(arena.Player.Creature.Block == 0, "Scry with no cards must not grant Block.");
            await Play(arena, Create(arena, ModelDb.Card<SipTea>(), upgrade: true));
            Require(arena.Player.Creature.GetPowerAmount<SipTeaPower>() == 3
                && PileType.Hand.GetPile(arena.Player).Cards.OfType<ChadoEnergyRedesignV1>().Count() == 1,
                "Generated upgraded Sip Tea must breathe immediately and keep three future turns.");
        }
        GD.Print("PASS generated component gameplay: damage/Karate, stock, fixed power, derivative snapshot, timed Thorns, empty Scry and upgraded Sip Tea.");
        using (var arena = new OrbCombat())
        {
            var source = ModelDb.Card<KarateStraightRedesignV1>().ToMutable();
            _ = MegaCrit.Sts2.Core.Runs.RunState.CreateForTest([arena.Player], seed: "ANTHONY_EDITED_SOURCE");
            source.Owner = arena.Player;
            source.UpgradeInternal();
            source.FinalizeUpgradeInternal();
            source.DynamicVars.Damage.BaseValue += 7;
            source.EnergyCost.SetCustomBaseCost(0);
            CardCmd.ApplyKeyword(source, CardKeyword.Retain);
            object?[] previewArgs = [arena.Player, source, null];
            Require((bool)AccessTools.Method(nativeApi, "TryCreateProfilePreview").Invoke(null, previewArgs)!,
                "Native-card editor adapter refused a supported modified source.");
            var preview = (CardModel)previewArgs[2]!;
            preview = arena.State.CloneCard(preview);
            Require(preview.CurrentUpgradeLevel == 1 && preview.EnergyCost.GetWithModifiers(CostModifiers.Local) == 0
                && preview.Keywords.Contains(CardKeyword.Retain), "External cost, upgrade or keyword modification was lost.");
            await Play(arena, preview);
            Require(arena.Enemy.CurrentHp == 982 && arena.Player.Creature.GetPowerAmount<KaratePower>() == 5,
                "External source modifications were dropped or applied twice during decomposition.");
        }
        using (var arena = new OrbCombat())
        {
            CardModel returning = Create(arena, ModelDb.Card<ChopStrikeRedesignV1>());
            for (int play = 1; play <= 4; play++)
            {
                await Play(arena, returning);
                Require(returning.Pile?.Type == (play <= 3 ? PileType.Hand : PileType.Discard),
                    $"Generated Strike Strike had the wrong destination on play {play}.");
            }
            CardCmd.ApplyKeyword(returning, CardKeyword.Exhaust);
            await Play(arena, returning);
            Require(returning.Pile?.Type == PileType.Exhaust, "Generated intrinsic return overrode Exhaust.");
        }
        GD.Print("PASS native decomposition keeps edited damage/cost/keywords/upgrade; generated return first three/fourth and Exhaust priority.");
        var savedRun = SeedRun("NINJA_ANTHONY_SAVE");
        foreach (var act in savedRun.Acts)
            act.GenerateRooms(savedRun.Rng.UpFront, MegaCrit.Sts2.Core.Unlocks.UnlockState.all);
        AccessTools.Method(run, "Generate").Invoke(null, ["NINJA_ANTHONY_SAVE", settings]);
        string savedPool = Snapshot();
        var deckCard = savedRun.CreateCard(generated[0], savedRun.Players[0]);
        deckCard.UpgradeInternal();
        deckCard.FinalizeUpgradeInternal();
        savedRun.Players[0].Deck.AddInternal(deckCard, -1, silent: true);
        var snapshot = MegaCrit.Sts2.Core.Runs.RunManager.Instance.ToSave(null);
        Require(Snapshot() == savedPool, "Saving changed the generated definitions.");
        var files = new MegaCrit.Sts2.Core.Saves.Test.MockGodotFileIo("user://anthony-contract");
        var saves = new MegaCrit.Sts2.Core.Saves.Managers.RunSaveManager(1, files,
            new MegaCrit.Sts2.Core.Saves.Migrations.MigrationManager(files), forceSynchronous: true);
        await saves.SaveRun(snapshot, false);
        var loaded = saves.LoadRunSave();
        Require(loaded.Success, "Native run save did not load.");
        Require(files.ReadFile(MegaCrit.Sts2.Core.Saves.Managers.RunSaveManager.GetRunSavePath(1,
            MegaCrit.Sts2.Core.Saves.Managers.RunSaveManager.runSaveFileName))!.Contains("autoanthony_pool"),
            "Ritsu did not write the external component pool into the native save.");
        AccessTools.Method(run, "Generate").Invoke(null, ["DIFFERENT_POOL", settings]);
        Require(Snapshot() != savedPool, "Reload fixture did not change the current pool.");
        var restored = MegaCrit.Sts2.Core.Runs.RunState.FromSerializable(loaded.SaveData!);
        string restoredPool = Snapshot();
        Require(restoredPool == savedPool, $"Quick reload failed to restore the saved pool (before {savedPool.Length}, after {restoredPool.Length}).");
        Require(restored.Players[0].Deck.Cards.Any(c => c.Id == deckCard.Id && c.CurrentUpgradeLevel == 1),
            "Generated card upgrade was lost during native save/load.");
        GD.Print("PASS component pool restored before card deserialization through native save/load; upgraded generated card retained.");
    }
}
