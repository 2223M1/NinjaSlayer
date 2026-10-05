using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Modding;
using NinjaSlayer.Content;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using NinjaSlayer.Cards.Standard;
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

        foreach (bool upgraded in new[] { false, true })
        {
            using var arena = new OrbCombat();
            var kick = Create(arena, ModelDb.Card<HalfMoonCompassKick>(), upgraded);
            await CardPileCmd.Add(kick, PileType.Hand);
            Require(!(bool)AccessTools.Property(kick.GetType(), "IsPlayable").GetValue(kick)!, "Generated Half-Moon requires hand Chado.");
            using var selector = CardSelectCmd.UseSelector(new SelectCards(options => options.OfType<Chado>().Take(1)));
            AddCard<Chado>(arena);
            await CardCmd.AutoPlay(Choice, kick, null);
            int firstDamage = 1000 - arena.Enemy.CurrentHp;
            Require(firstDamage > 0 && kick.Pile?.Type == PileType.Hand, "Generated Half-Moon must attack and return.");
            AddCard<Chado>(arena);
            await CardCmd.AutoPlay(Choice, kick, null);
            Require(1000 - arena.Enemy.CurrentHp == firstDamage * 3, "Generated Half-Moon must double damage for its second play.");
            var copy = arena.State.CloneCard(kick);
            kick.EndOfTurnCleanup();
            copy.EndOfTurnCleanup();
            AddCard<Chado>(arena);
            await CardCmd.AutoPlay(Choice, kick, null);
            Require(1000 - arena.Enemy.CurrentHp == firstDamage * 4, "Generated Half-Moon must reset on turn cleanup.");
            AddCard<Chado>(arena);
            await Play(arena, copy);
            Require(1000 - arena.Enemy.CurrentHp == firstDamage * 5, "Generated Half-Moon copies must reset their own temporary damage.");
            if (upgraded)
            {
                kick.DowngradeInternal();
                kick.EndOfTurnCleanup();
                var fresh = Create(arena, ModelDb.Card<HalfMoonCompassKick>());
                foreach (var variable in fresh.DynamicVars.Values)
                    Require(kick.DynamicVars[variable.Name].BaseValue == variable.BaseValue,
                        "Generated Half-Moon downgrade and cleanup retained a stale damage adjustment.");
            }
        }
        using (var arena = new OrbCombat())
        {
            var tornado = Create(arena, ModelDb.Card<TornadoFist>());
            arena.Player.PlayerCombatState!.GainEnergy(4);
            await PowerCmd.Apply<VigorPower>(Choice, arena.Player.Creature, 7, arena.Player.Creature, null);
            await Play(arena, tornado);
            Require(arena.Enemy.CurrentHp == 912 && !arena.Enemy.HasPower<VulnerablePower>(),
                "Generated Tornado must use eight native Vigor-enhanced hits without Vulnerable at X=4.");
        }
        GD.Print("PASS v1.18 generated Half-Moon tea/doubling/reset and native Tornado multihit.");

        foreach (bool upgraded in new[] { false, true })
        foreach (bool storm in new[] { false, true })
        {
            using var arena = new OrbCombat();
            var second = arena.AddEnemy();
            AddCard<Chado>(arena, PileType.Exhaust);
            AddCard<Chado>(arena, PileType.Exhaust);
            var card = Create(arena, storm ? ModelDb.Card<StormFist>() : ModelDb.Card<DragonRoundhouseKick>(), upgraded);
            await PowerCmd.Apply<StrengthPower>(Choice, arena.Player.Creature, 3, arena.Player.Creature, null);
            await PowerCmd.Apply<VigorPower>(Choice, arena.Player.Creature, 7, arena.Player.Creature, null);
            await Play(arena, card);
            int damage = storm ? 4 * ((upgraded ? 6 + 2 * 4 : 4 + 2 * 3) + 10) : 2 * ((upgraded ? 9 : 7) + 10);
            Require(arena.Enemy.CurrentHp == 1000 - damage && second.CurrentHp == 1000 - (storm ? 0 : damage)
                && !arena.Player.Creature.HasPower<VigorPower>(),
                "Generated Storm/Dragon base+upgrade must apply Vigor to every hit and target, then consume it once.");
        }
        GD.Print("PASS generated Storm/Dragon base+upgrade: native multihit Vigor, Strength, Chado and target scope.");
        foreach (bool upgraded in new[] { false, true })
        {
            using var arena = new OrbCombat();
            await PowerCmd.Apply<StrengthPower>(Choice, arena.Player.Creature, 3, arena.Player.Creature, null);
            await PowerCmd.Apply<VigorPower>(Choice, arena.Player.Creature, 7, arena.Player.Creature, null);
            await Play(arena, Create(arena, ModelDb.Card<PressTheAttack>(), upgraded));
            Require(arena.Enemy.CurrentHp == 1000 - 3 * (10 + (upgraded ? 6 : 5))
                && !arena.Player.Creature.HasPower<VigorPower>(), "Generated Press the Attack must share Vigor across three hits.");
            await Play(arena, Create(arena, ModelDb.Card<DevourFlame>(), upgraded));
            await Play(arena, Create(arena, ModelDb.Card<Kindle>(), upgraded));
            Require(PileType.Discard.GetPile(arena.Player).Cards.OfType<BlackFlame>().Count() == 1
                && arena.Player.Creature.GetPowerAmount<NarakuLifePower>() == (upgraded ? 3 : 2),
                "Generated Kindle must generate Black Flame in discard and trigger Devour Flame Naraku Life.");
        }
        GD.Print("PASS v1.19 generated Press the Attack three-hit Vigor and Kindle discard/status Naraku Life.");
        foreach (bool upgraded in new[] { false, true })
        {
            using var arena = new OrbCombat();
            await AddStock(arena.Player, 2);
            await Play(arena, Create(arena, ModelDb.Card<SpiralJump>(), upgraded));
            Require(arena.Stock == 0 && arena.Enemy.CurrentHp == 1000 - (upgraded ? 12 : 8) - 12,
                "Generated Spiral Jump must consume one volley, not grant new stock.");
            await AddStock(arena.Player, 2);
            int hp = arena.Enemy.CurrentHp;
            await Play(arena, Create(arena, ModelDb.Card<Moonsault>(), upgraded));
            Require(arena.Stock == 0 && arena.Enemy.CurrentHp == hp - 24,
                "Generated Moonsault must consume two volleys.");
            for (int i = 0; i < 10; i++) AddCard<DefendIronclad>(arena, PileType.Draw);
            await Play(arena, Create(arena, ModelDb.Card<Adapt>(), upgraded));
            Require(PileType.Hand.GetPile(arena.Player).Cards.Count == (upgraded ? 4 : 3),
                "Generated Adapt must draw three/four cards without Sly.");
            var next = PileType.Hand.GetPile(arena.Player).Cards[0];
            await CardCmd.AutoPlay(Choice, next, null);
            Require(PileType.Draw.GetPile(arena.Player).Cards[0] == next && !next.Keywords.Contains(CardKeyword.Sly),
                "Generated Adapt must rebound the next card without Sly.");
            await Play(arena, Create(arena, ModelDb.Card<Resilience>(), upgraded));
            int hand = PileType.Hand.GetPile(arena.Player).Cards.Count;
            await NinjaSlayer.Code.Commands.NinjaSlayerCardCmd.AddGeneratedCard<Wound>(arena.Player, PileType.Discard);
            Require(PileType.Hand.GetPile(arena.Player).Cards.Count == hand + (upgraded ? 2 : 1),
                "Generated Resilience must draw on owned Status generation.");
        }
        GD.Print("PASS 1.0.13 generated Spiral/Moonsault volleys, Adapt draw/rebound and Resilience generation.");
        foreach (bool upgraded in new[] { false, true })
        foreach (bool hasTea in new[] { false, true })
        {
            using var arena = new OrbCombat();
            var tea = hasTea ? AddCard<Chado>(arena) : null;
            var drawnTea = AddCard<Chado>(arena, PileType.Draw);
            for (int i = 0; i < 15; i++) AddCard<MegaCrit.Sts2.Core.Models.Cards.DefendIronclad>(arena, PileType.Draw);
            await Play(arena, Create(arena, ModelDb.Card<DragonFlyingKick>(), upgraded));
            var hand = PileType.Hand.GetPile(arena.Player).Cards;
            var breathedTea = tea ?? hand.OfType<Chado>().Single(c => c != drawnTea);
            Require(hand.Count == CardPile.MaxCardsInHand && breathedTea.Pile?.Type == PileType.Hand
                && breathedTea.DynamicVars.Energy.BaseValue == (upgraded ? 3 : 2) + (hasTea ? 1 : 0)
                && drawnTea.Pile?.Type == PileType.Hand && drawnTea.DynamicVars.Energy.BaseValue == 1,
                "Generated Dragon Flying Kick must breathe before filling the hand, preserving newly drawn Chado.");
        }
        GD.Print("PASS generated Dragon Flying Kick base+upgrade: breath before draw, existing/generated and newly drawn Chado.");

        using (var arena = new OrbCombat())
        {
            await Play(arena, Create(arena, ModelDb.Card<StraightPunch>()));
            Require(arena.Enemy.CurrentHp == 992 && arena.Player.Creature.GetPowerAmount<KaratePower>() == 4,
                "Generated Straight Punch must deal 8 and grant 4 Karate.");
        }
        using (var arena = new OrbCombat())
        {
            await Play(arena, Create(arena, ModelDb.Card<ReadyShuriken>(), upgrade: true));
            Require(arena.Stock == 2 && arena.Player.Creature.Block == 10, "Generated upgraded Ready Shuriken lost source values.");
            await Play(arena, Create(arena, ModelDb.Card<StarlessNight>()));
            Require(arena.Player.Creature.HasPower<StarlessNightPower>(), "Fixed nonnumeric power was applied with zero amount.");
            await Play(arena, Create(arena, ModelDb.Card<BladePrep>()));
            Require(arena.Tokens == 1, "Generated stock gain failed to trigger Starless Night once.");
        }
        using (var arena = new OrbCombat())
        {
            await Play(arena, Create(arena, ModelDb.Card<NinjaCaltrops>(), upgrade: true));
            Require(arena.Player.Creature.Block == 8 && arena.Player.Creature.GetPowerAmount<ThornsPower>() == 3,
                "Generated upgraded NinjaCaltrops lost its permanent Thorns or Block.");
        }
        using (var arena = new OrbCombat())
        {
            await Play(arena, Create(arena, ModelDb.Card<ReadAhead>(), upgrade: true));
            Require(arena.Player.Creature.Block == 0, "Scry with no cards must not grant Block.");
            await Play(arena, Create(arena, ModelDb.Card<Sip>(), upgrade: true));
            Require(arena.Player.Creature.GetPowerAmount<SipPower>() == 3
                && PileType.Hand.GetPile(arena.Player).Cards.OfType<Chado>().Count() == 1,
                "Generated upgraded Sip Tea must breathe immediately and keep three future turns.");
        }
        GD.Print("PASS generated component gameplay: damage/Karate, stock, fixed power, derivative snapshot, permanent Thorns, empty Scry and upgraded Sip Tea.");
        using (var arena = new OrbCombat())
        {
            var source = ModelDb.Card<StraightPunch>().ToMutable();
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
            CardModel sourceStrike = Create(arena, ModelDb.Card<StrikeStrike>());
            await Play(arena, sourceStrike);
            Require(sourceStrike.Pile?.Type == PileType.Exhaust
                && PileType.Hand.GetPile(arena.Player).Cards.OfType<StrikeStrike>().Count() == 1,
                "Generated Strike Strike exhausts and generates a native Strike Strike.");
        }
        GD.Print("PASS native decomposition keeps edited damage/cost/keywords/upgrade and Strike generation.");
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
