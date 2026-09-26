using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Migrations;
using MegaCrit.Sts2.Core.Saves.Test;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;
using STS2RitsuLib;
using STS2RitsuLib.Interop;
using STS2RitsuLib.Scaffolding.Content;
using STS2RitsuLib.Patching.Core;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner : Node
{
    private static int _evoked;
    private static bool _hasPresentationResources;
    private static readonly BlockingPlayerChoiceContext Choice = new();

    public override async void _Ready()
    {
        GD.Print("Starting orb product contracts.");
        try
        {
            Task run = RunContracts();
            await run;
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }


    private async Task RunContracts()
    {
        try
        {
            Assembly product = typeof(ShurikenOrb).Assembly;
            string? expected = System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_EXPECTED_SOURCE_REVISION");
            Require(expected?.Length == 40 && Metadata(product, "NinjaSlayerSourceRevision") == expected,
                "Candidate source revision must match the requested full SHA.");
            Require(Metadata(product, "NinjaSlayerHostChannel") == Metadata(GetType().Assembly, "NinjaSlayerHostChannel"),
                "Product and contract host channels differ.");
            string productPath = System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_PRODUCT_ASSEMBLY")
                ?? throw new InvalidOperationException("Specify the candidate DLL path.");
            byte[] productBytes = System.IO.File.ReadAllBytes(productPath);
            using (var pe = new PEReader(new MemoryStream(productBytes)))
            {
                MetadataReader metadata = pe.GetMetadataReader();
                Require(metadata.GetGuid(metadata.GetModuleDefinition().Mvid) == product.ManifestModule.ModuleVersionId,
                    "The loaded product is not the requested candidate DLL.");
            }
            Require(typeof(Player).Assembly.ManifestModule.ModuleVersionId.ToString() ==
                System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_HOST_MVID"), "Loaded host MVID differs from the requested host.");
            GD.Print($"Candidate {expected}: {productPath}; SHA256 {Convert.ToHexString(SHA256.HashData(productBytes))}; host MVID {typeof(Player).Assembly.ManifestModule.ModuleVersionId}");
            if (System.Environment.GetEnvironmentVariable("NINJASLAYER_MULTIPLAYER_GREETING_ONLY") != "1")
                await VerifyUploadTransport();
            if (System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_ONLY_UPLOADS") == "1")
            {
                GD.Print("NinjaSlayer upload product contracts passed.");
                GetTree().Quit(0);
                return;
            }
            TestMode.IsOn = true;
            AccessTools.Property(typeof(ModManager), nameof(ModManager.State)).SetValue(null, ModManagerState.Initialized);
            var productMod = new Mod
            {
                path = "res://", manifest = new ModManifest { id = "NinjaSlayer" },
                state = ModLoadState.Loaded,
#if NINJASLAYER_CHANNEL_STABLE
                assembly = product
#else
                assemblies = [product]
#endif
            };
            ((List<Mod>)AccessTools.Field(typeof(ModManager), "_mods").GetValue(null)!).Add(productMod);
#if !NINJASLAYER_CHANNEL_STABLE
            AssemblyInfo.Init();
            AssemblyInfo.ModMap![product] = productMod;
            AssemblyInfo.ModMap[GetType().Assembly] = new Mod { path = "res://", manifest = new ModManifest { id = "NinjaSlayer.OrbContracts" } };
#endif
            _ = MegaCrit.Sts2.Core.Saves.SaveManager.Instance;
            string hostPack = System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_HOST_PACK")
                ?? throw new InvalidOperationException("Product gameplay contracts require the host resource pack for native selection prompts.");
            Require(ProjectSettings.LoadResourcePack(hostPack, replaceFiles: false), "Could not mount the host resource pack.");
            string? productPack = System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_PRODUCT_PACK");
            if (productPack is not null)
            {
                Require(ProjectSettings.LoadResourcePack(productPack, replaceFiles: true), "Could not mount the product resource pack.");
                _hasPresentationResources = true;
            }
            else
                GD.Print("NOT RUN: tooltip and native event presentation contracts (no product resource pack supplied).");
            // Reproduce a mod patching AutoPlay before RitsuLib/NinjaSlayer install wrapper hooks.
            if (System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_EARLY_AUTOPLAY") == "1")
                PatchAutoplayBeforeFramework();
            RitsuLibFramework.Initialize();
            using (RitsuLibFramework.BeginModDataRegistration("NinjaSlayer.OrbContracts"))
            {
                NinjaSlayerRunData.Register("NinjaSlayer.OrbContracts");
                AccessTools.Method(typeof(ShurikenOrb), "RegisterSavedData").Invoke(null, ["NinjaSlayer.OrbContracts"]);
                AccessTools.Method(product.GetType("NinjaSlayer.Code.Nodes.NinjaSlayerFreeControl"), "RegisterSavedData")
                    .Invoke(null, ["NinjaSlayer.OrbContracts"]);
                AccessTools.Method(typeof(StrongShurikenTokenRedesignV1), "RegisterSavedData").Invoke(null, ["NinjaSlayer.OrbContracts"]);
            }
            ModTypeDiscoveryHub.RegisterModAssembly("NinjaSlayer", product);
            var configureDeck = AccessTools.Method(typeof(Entry), "ConfigureStartingDeck")
                .MakeGenericMethod(typeof(NinjaSlayerCharacter))
                .CreateDelegate<Action<CharacterRegistrationEntry<NinjaSlayerCharacter>>>();
            RitsuLibFramework.CreateContentPack("NinjaSlayer")
                .Character(configureDeck)
                .Card<NinjaSlayerCardPool, NinjaSlayer.Cards.OneBodyOneSoul>()
                .Card<MegaCrit.Sts2.Core.Models.CardPools.EventCardPool, NinjaSlayer.Cards.ZazenDrink>()
                .Apply();
            RitsuLibFramework.RegisterArchaicToothTranscendenceMapping<KarateStraightRedesignV1, NinjaSlayer.Cards.CollapseFistRedesignV1>();
            // Run the framework's post-mod-load discovery without loading menu/localization assets.
            AccessTools.Method(typeof(RitsuLibFramework).Assembly.GetType("STS2RitsuLib.Interop.Patches.ModTypeDiscoveryPatch", true), "Prefix")
                .Invoke(null, null);
            ModelDb.Init();
            ModelDb.Inject(typeof(EvokeObserver));
            MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache.Init();

            var patcher = RitsuLibFramework.CreatePatcher("NinjaSlayer.OrbContracts", "Product");
            patcher.RegisterPatch<ShurikenOrbChannelPatch>();
            patcher.RegisterPatch<ShurikenOrbAddVisualPatch>();
            patcher.RegisterPatch<ShurikenOrbRemoveSlotPatch>();
            patcher.RegisterPatch<ShurikenOrbRemoveVisualPatch>();
            patcher.RegisterPatch<ShurikenOrbPreviewPatch>();
            patcher.RegisterPatch<ShurikenOrbLayoutPatch>();
            patcher.RegisterPatch<ShurikenMultiCastPreviewPatch>();
            patcher.RegisterPatch<StarlessNightDiscardBatchPatch>();
            patcher.RegisterPatch<NarakuLifeDamagePatch>();
            patcher.RegisterPatch<NarakuCentennialPuzzlePatch>();
            patcher.RegisterPatch<SawatariDuelRewardsPatch>();
            patcher.RegisterPatch<SawatariTurnEndPatch>();
            patcher.RegisterPatch<NinjaSlayerSwipePowerStealPatch>();
            patcher.RegisterPatch<KarateDamageWavePatch>();
            patcher.RegisterPatch<NinjaSlayerRunSavePatch>();
            foreach (string name in new[] { "CardPlayResolutionBeforePatch", "CardPlayResolutionAfterPatch", "CardResolutionCleanupPatch", "AttackEvasionDamagePatch" })
                typeof(ModPatcherExtensions).GetMethod("RegisterPatch")!
                    .MakeGenericMethod(product.GetType("NinjaSlayer.Code.Patches." + name, true)!).Invoke(null, [patcher]);
            Require(patcher.PatchAll(), "Orb patches failed to install.");
            var presentation = new Harmony("NinjaSlayer.OrbContracts.Presentation");
            presentation.Patch(AccessTools.Method(product.GetType("NinjaSlayer.Cards.ShurikenCombat", true), "PlayStockThrowAnimation"),
                prefix: new HarmonyMethod(GetType(), nameof(SkipThrowAnimation)));
            string? networkRole = System.Environment.GetEnvironmentVariable("NINJASLAYER_MULTIPLAYER_ROLE");
            if (networkRole is not null)
            {
                await VerifyMultiplayer(networkRole);
                GD.Print("NinjaSlayer multiplayer product contracts passed.");
                GetTree().Quit(0);
                return;
            }
            if (System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_ONLY_ORB_SLOTS") == "1")
            {
                SaveManager.Instance.InitSettingsDataForTest();
                SaveManager.Instance.InitPrefsDataForTest();
                MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
                await VerifyLifecycle();
                await VerifyOrbSlotSemantics();
                await VerifyDiscards();
                await VerifyMultipleEvoke();
                await VerifyShuffleAndReplacement();
                await VerifyVolleyAndSave();
                await VerifyRunSaves();
                await VerifyCurrentCardInteractions();
                GD.Print("NinjaSlayer orb product contracts passed.");
                GetTree().Quit(0);
                return;
            }
            if (System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_ONLY_V024") == "1")
            {
                await VerifyV024();
                GD.Print("NinjaSlayer orb product contracts passed.");
                GetTree().Quit(0);
                return;
            }
            if (System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_ONLY_GAMEPLAY") == "1")
            {
                VerifyCardMetadata();
                await VerifyCurrentCardInteractions();
                await VerifyV020();
                await VerifyV17();
                await VerifyV023();
                await VerifyV024();
                GD.Print("NinjaSlayer orb product contracts passed.");
                GetTree().Quit(0);
                return;
            }
            await VerifyIaiLifeBoundary();
            await VerifySawatariWeapons();
            if (System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_ONLY_SAWATARI") == "1")
            {
                GD.Print("NinjaSlayer orb product contracts passed.");
                GetTree().Quit(0);
                return;
            }
            await VerifyDarkStrikeTheft();
            if (System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_ONLY_DARK_STRIKE") == "1")
            {
                GD.Print("NinjaSlayer orb product contracts passed.");
                GetTree().Quit(0);
                return;
            }
            await VerifyFreeControl();
            if (System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_ONLY_FREE_CONTROL") == "1")
            {
                GD.Print("NinjaSlayer orb product contracts passed.");
                GetTree().Quit(0);
                return;
            }
            await VerifyLifecycle();
            await VerifyOrbSlotSemantics();
            await VerifyDiscards();
            await VerifyMultipleEvoke();
            await VerifyShuffleAndReplacement();
            await VerifyVolleyAndSave();
            await VerifyRunSaves();
            await VerifyAttackCadence();
            await VerifyAimPose();
            await VerifyEntangledSpinExposure();
            await VerifyGroundShadows();
            VerifyCardMetadata();
            await VerifyCurrentCardInteractions();
            await VerifyV020();
            await VerifyEventRelics();
            await VerifyV17();
            await VerifyV023();
            await VerifyV024();
            await VerifyMaintenanceRefactor();
            VerifyTelemetryConsent();
            await VerifyBalanceTelemetry();
            await VerifyReplayJournal();
            string? successMarker = System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_SUCCESS_MARKER");
            if (!string.IsNullOrWhiteSpace(successMarker))
                System.IO.File.WriteAllText(successMarker, "passed\n");
            GD.Print("NinjaSlayer orb product contracts passed.");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private static async Task VerifyRunSaves()
    {
        CardModel[] catalog = ModelDb.CardPool<NinjaSlayerCardPool>().AllCards.Concat(new CardModel[]
        {
            ModelDb.Card<ChadoEnergyRedesignV1>(), ModelDb.Card<StraightKiRedesignV1>(),
            ModelDb.Card<BlackFlameRedesignV1>(), ModelDb.Card<StrongShurikenTokenRedesignV1>(),
            ModelDb.Card<NinjaSlayer.Cards.BusyLine>(), ModelDb.Card<NinjaSlayer.Cards.SawatariMachete>(),
            ModelDb.Card<NinjaSlayer.Cards.ZazenDrink>()
        }).Distinct().ToArray();
        Require(catalog.Length == 93 && ModelDb.AllCharacters.Count(character => character is INinjaSlayerCharacter) == 1,
            "Current content must have 93 cards and one Ninja Slayer character.");
        CardModel[] rewards = ModelDb.CardPool<NinjaSlayerCardPool>()
            .GetUnlockedCards(UnlockState.all, CardMultiplayerConstraint.SingleplayerOnly).ToArray();
        foreach (var (rarity, expected) in new[]
        {
            (CardRarity.Common, RedesignV1Rules.CommonRewardCardIds),
            (CardRarity.Uncommon, RedesignV1Rules.UncommonRewardCardIds),
            (CardRarity.Rare, RedesignV1Rules.RareRewardCardIds)
        })
            Require(rewards.Where(card => card.Rarity == rarity).Select(card => card.GetType().Name).SequenceEqual(expected),
                $"Unlocked {rarity} reward order differs from the approved board.");
        Player player = Player.CreateForNewRun<NinjaSlayerCharacter>(UnlockState.all, 1);
        player.InitializeSeed("save-contract");
        Require(player.Deck.Cards.Count == 10
            && player.Deck.Cards.Count(card => card is StrikeNinjaSlayerRedesignV1) == 4
            && player.Deck.Cards.Count(card => card is DefendNinjaSlayerRedesignV1) == 4
            && player.Deck.Cards.Count(card => card is KarateStraightRedesignV1) == 1
            && player.Deck.Cards.Count(card => card is Prejudge) == 1, "New run must use the 4/4/1/1 starting deck.");
        var store = new MockGodotFileIo("user://orb-contract-saves");
        var saves = new RunSaveManager(1, store, new MigrationManager(store), forceSynchronous: true);
        var run = new SerializableRun { SchemaVersion = saves.SchemaVersion, Players = [player.ToSerializable()] };
        await saves.SaveRun(run, false);
        var loaded = saves.LoadRunSave();
        Require(loaded.Success, "New run save could not be read.");
        Player restored = Player.FromSerializable(loaded.SaveData!.Players.Single());
        Require(restored.Character is NinjaSlayerCharacter
            && restored.Deck.Cards.Select(card => card.Id).SequenceEqual(player.Deck.Cards.Select(card => card.Id))
            && restored.Relics.Select(relic => relic.Id).SequenceEqual(player.Relics.Select(relic => relic.Id)),
            "New run reload changed character, deck or relics.");

        // Synthetic removed-model inputs test failure preservation, not historical-save compatibility.
        Action<SerializablePlayer>[] removedModels =
        [
            save => save.CharacterId = new ModelId("CHARACTER", "NINJA_SLAYER_CHARACTER_NINJA_SLAYER_REDESIGN_CHARACTER"),
            save => save.Relics[0].Id = new ModelId("RELIC", "NINJA_SLAYER_RELIC_REDESIGN_V1_CHADO_BREATHING_RELIC"),
            save => save.Relics[0].Id = new ModelId("RELIC", "NINJA_SLAYER_RELIC_REDESIGN_V1_DEEP_CHADO_BREATHING_RELIC")
        ];
        foreach (Action<SerializablePlayer> remove in removedModels)
        {
            run.Players = [player.ToSerializable()];
            remove(run.Players[0]);
            await saves.SaveRun(run, false);
            string path = RunSaveManager.GetRunSavePath(1, RunSaveManager.runSaveFileName);
            string original = store.ReadFile(path) ?? throw new InvalidOperationException("Run fixture was not written.");
            store.Calls.Clear();
            loaded = saves.LoadRunSave();
            Require(loaded.Success, "An unknown model ID must remain readable as serialized data.");
            bool rejected = false;
            try { Player.FromSerializable(loaded.SaveData!.Players.Single()); }
            catch (MegaCrit.Sts2.Core.Models.Exceptions.ModelNotFoundException) { rejected = true; }
            Require(rejected, "A removed model must not silently load as a replacement model.");
            Require(store.ReadFile(path) == original && store.Calls.All(call => call.Method is not
                (MockGodotFileIo.Methods.writeFile or MockGodotFileIo.Methods.writeFileAsync
                or MockGodotFileIo.Methods.deleteFile or MockGodotFileIo.Methods.renameFile)),
                "Unsupported run loading must preserve the original file without writes or deletion.");
        }
        GD.Print("PASS new character inventory save/reload and removed-model file preservation");
    }

    private static async Task VerifyLifecycle()
    {
        using var combat = new OrbCombat(ninjaSlayer: true);
        await AddStock(combat.Player, 1);
        Require(combat.Stock == 1 && combat.Capacity == 0, "Dedicated Shuriken must not allocate a normal slot.");
        await OrbCmd.AddSlots(combat.Player, 1);
        await OrbCmd.Channel<LightningOrb>(Choice, combat.Player);
        await OrbCmd.EvokeNext(Choice, combat.Player);
        Require(combat.Stock == 1 && combat.Capacity == 1 && combat.Queue.Orbs.Single() is ShurikenOrb,
            "Native front evocation must prioritize a normal orb.");
        await OrbCmd.EvokeNext(Choice, combat.Player);
        Require(combat.Stock == 0 && combat.Capacity == 1 && combat.Queue.Orbs.Count == 0,
            "Dedicated depletion must leave normal capacity unchanged.");
        GD.Print("PASS dedicated stock, normal-orb priority and capacity preservation");
    }

    private static async Task VerifyDiscards()
    {
        foreach (int stock in new[] { 0, 1, 4 })
        {
            using var combat = new OrbCombat();
            await AddStock(combat.Player, stock);
            await PowerCmd.Apply<RecycledBladesPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
            int hp = combat.Enemy.CurrentHp;
            await CardCmd.Discard(Choice, new[] { combat.Card(), combat.Card(), combat.Card() });
            Require(combat.Stock == Math.Max(stock - 3, 0), $"Discard must consume existing stock at initial {stock}.");
            Require(hp - combat.Enemy.CurrentHp == Math.Min(stock, 3) * 6, "Recycled Blades must not replenish stock on discard.");
        }
        using (var combat = new OrbCombat())
        {
            await AddStock(combat.Player, 2);
            int before = _evoked;
            await CardCmd.Discard(Choice, combat.Card());
            Require(_evoked == before + 1, "Discard firing must dispatch the host AfterOrbEvoked hook.");
        }
        GD.Print("PASS discard sequence and recycling at zero, one and several stock");
    }

    private static async Task VerifyMultipleEvoke()
    {
        foreach (int shots in new[] { 2, 4 })
        {
            using var combat = new OrbCombat();
            await PowerCmd.Apply<StarlessNightRedesignPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
            for (int chain = 0; chain < 2; chain++)
            {
                await AddStock(combat.Player, 3);
                int hp = combat.Enemy.CurrentHp;
                int before = _evoked;
                for (int i = 0; i < shots; i++)
                    await OrbCmd.EvokeNext(Choice, combat.Player, dequeue: i == shots - 1);
                Require(hp - combat.Enemy.CurrentHp == shots * 3 * 6, "Every native evoke must fire the entire stock.");
                Require(_evoked == before + shots, "Native evokes must emit one host event per invocation, not per projectile.");
                Require(combat.Stock == 0, "Multi-evoke must remove the orb after its last repetition.");
                Require(combat.Tokens == (chain + 1) * shots * 3, "Starless Night must convert every projectile in independent chains.");
            }
        }
        GD.Print("PASS double and quadruple evokes and independent token chains");
    }

    private static async Task VerifyShuffleAndReplacement()
    {
        using (var combat = new OrbCombat())
        {
            await AddStock(combat.Player, 3);
            await PowerCmd.Apply<BladeCyclePower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
            await PowerCmd.Apply<StarlessNightRedesignPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
            await PowerCmd.Apply<BladeSweepPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
            Creature second = combat.AddEnemy();
            int hp = combat.Enemy.CurrentHp;
            int before = _evoked;
            await Hook.AfterShuffle(combat.State, Choice, combat.Player);
            Require(2 * hp - combat.Enemy.CurrentHp - second.CurrentHp == 36 && combat.Stock == 2 && combat.Tokens == 3,
                "Upgraded shuffle must convert all stock, consume one and produce three tokens.");
            Require(second.CurrentHp == hp - 18 && combat.Enemy.CurrentHp == hp - 18, "Starless AOE must damage both targets.");
            Require(_evoked == before + 3, "Shuffle shots must dispatch the host evoke hook.");
        }
        using (var combat = new OrbCombat())
        {
            await AddStock(combat.Player, 3);
            int hp = combat.Enemy.CurrentHp;
            await OrbCmd.Channel<LightningOrb>(Choice, combat.Player);
            Require(hp - combat.Enemy.CurrentHp == 18, "Replacement must fire the pre-replacement stock.");
            Require(combat.Capacity == 1 && combat.Queue.Orbs.Single() is LightningOrb,
                "Other characters must preserve their normal slot when replacing Shuriken.");
        }
        GD.Print("PASS shuffle and full-slot replacement");
    }

    private static async Task VerifyVolleyAndSave()
    {
        using var combat = new OrbCombat();
        await AddStock(combat.Player, 3);
        ShurikenOrb orb = combat.Orb!;
        SavedProperties saved = SavedProperties.From(orb)!;
        var jsonOptions = new JsonSerializerOptions { IncludeFields = true };
        string json = JsonSerializer.Serialize(saved, jsonOptions);
        var restored = (ShurikenOrb)ModelDb.Orb<ShurikenOrb>().ToMutable();
        JsonSerializer.Deserialize<SavedProperties>(json, jsonOptions)!.Fill(restored);
        Require(restored.StackCount == 3, "Orb model data must round-trip stock without a second slot-capacity owner.");

        await PowerCmd.Apply<StarlessNightRedesignPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
        int hp = combat.Enemy.CurrentHp;
        await (Task)AccessTools.Method(typeof(ShurikenOrb), "FireConsumedVolley").Invoke(orb, [Choice, 1, null])!;
        Require(hp - combat.Enemy.CurrentHp == 18 && combat.Stock == 0 && combat.Capacity == 1 && combat.Tokens == 3,
            "Hell Tornado must consume all stock, preserve ordinary slots and generate three tokens.");
        await AddStock(combat.Player, 3);
        await PowerCmd.Remove<StarlessNightRedesignPower>(combat.Player.Creature);
        combat.Enemy.SetCurrentHpInternal(1);
        await (Task)AccessTools.Method(typeof(ShurikenOrb), "FireConsumedVolley").Invoke(combat.Orb, [Choice, 1, null])!;
        Require(combat.Enemy.CurrentHp == 0 && combat.Stock == 0, "Lethal volley must stop and remove the empty orb.");
        combat.Player.AfterCombatEnd();
        combat.Player.ResetCombatState();
        Require(combat.Capacity == 0 && combat.Queue.Orbs.Count == 0, "The next combat must start without temporary orb state.");
        GD.Print("PASS saved model data, Hell Tornado volley and last-enemy cleanup");
    }

    private static Task AddStock(Player player, int amount) =>
        (Task)AccessTools.Method(typeof(ShurikenOrb), "AddStock").Invoke(null, [Choice, player, amount])!;

    private static void VerifyCardMetadata()
    {
        CardModel[] catalog = ModelDb.CardPool<NinjaSlayerCardPool>().AllCards.Concat(new CardModel[]
        {
            ModelDb.Card<ChadoEnergyRedesignV1>(), ModelDb.Card<StraightKiRedesignV1>(),
            ModelDb.Card<BlackFlameRedesignV1>(), ModelDb.Card<StrongShurikenTokenRedesignV1>(),
            ModelDb.Card<NinjaSlayer.Cards.BusyLine>(), ModelDb.Card<NinjaSlayer.Cards.SawatariMachete>(),
            ModelDb.Card<NinjaSlayer.Cards.ZazenDrink>()
        }).Distinct().OrderBy(card => card.Id.ToString(), StringComparer.Ordinal).ToArray();
        var states = catalog.SelectMany(canonical => (canonical.MaxUpgradeLevel == 0 ? new[] { false } : new[] { false, true }).Select(upgraded =>
        {
            CardModel card = canonical.ToMutable();
            if (upgraded) card.UpgradeInternal();
            return new
            {
                Id = card.Id.ToString(), Upgraded = upgraded,
                Cost = card.EnergyCost.GetWithModifiers(CostModifiers.Local),
                X = card.EnergyCost.CostsX, Type = card.Type.ToString(), Rarity = card.Rarity.ToString(),
                Target = card.TargetType.ToString(), card.CanBeGeneratedInCombat, card.CanBeGeneratedByModifiers,
                Keywords = card.Keywords.Select(value => value.ToString()).Order(StringComparer.Ordinal).ToArray(),
                Tags = card.Tags.Select(value => value.ToString()).Order(StringComparer.Ordinal).ToArray(),
                Portrait = ((ModCardTemplate)card).AssetProfile.PortraitPath,
                Vars = card.DynamicVars.Keys.Order(StringComparer.Ordinal).ToDictionary(key => key, key => card.DynamicVars[key].BaseValue)
            };
        })).ToArray();
        string actual = JsonSerializer.Serialize(states, new JsonSerializerOptions { WriteIndented = true });
        string? snapshot = System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_CARD_SNAPSHOT");
        if (snapshot is not null)
        {
            System.IO.File.WriteAllText(snapshot, actual);
            GD.Print($"Card metadata snapshot: {snapshot}");
        }
        else
        {
            string expected = System.IO.File.ReadAllText(ProjectSettings.GlobalizePath("res://card-metadata.json"));
            JsonElement[] orderedExpected = JsonSerializer.Deserialize<JsonElement[]>(expected)!
                .OrderBy(item => item.GetProperty("Id").GetString(), StringComparer.Ordinal)
                .ThenBy(item => item.GetProperty("Upgraded").GetBoolean()).ToArray();
            Require(System.Text.Json.Nodes.JsonNode.DeepEquals(
                JsonSerializer.SerializeToNode(orderedExpected), System.Text.Json.Nodes.JsonNode.Parse(actual)),
                "Current card metadata differs from the approved baseline.");
            GD.Print("PASS 93 cards: IDs, costs, types, rarity, targets, generation, keywords, tags, art and base/upgraded values");
        }
    }

    private static bool SkipThrowAnimation(Action beforeThrow, ref Task __result)
    {
        beforeThrow();
        __result = Task.CompletedTask;
        return false;
    }

    private static string Metadata(Assembly assembly, string key) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(value => value.Key == key).Value!;
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public sealed class EvokeObserver : PowerModel
    {
        public int Discarded { get; private set; }
        public bool DrawOnDiscard { get; set; }
        public override async Task AfterCardDiscarded(PlayerChoiceContext choiceContext, CardModel card)
        {
            if (card.Owner != Owner.Player) return;
            Discarded++;
            if (DrawOnDiscard) await CardPileCmd.Draw(choiceContext, 1, card.Owner);
        }
        public CardModel? NestedFlameAttack { get; set; }
        public override Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result,
            MegaCrit.Sts2.Core.ValueProps.ValueProp props, Creature? dealer, CardModel? cardSource)
        {
            if (cardSource is BlackFlameRedesignV1 && NestedFlameAttack is { } attack)
            {
                NestedFlameAttack = null;
                return CardCmd.AutoPlay(choiceContext, attack, target);
            }
            return Task.CompletedTask;
        }
        public int Deaths { get; private set; }
        public decimal Healing { get; private set; }
        public override PowerType Type => PowerType.Buff;
        public override PowerStackType StackType => PowerStackType.Single;
        public override Task AfterOrbEvoked(PlayerChoiceContext choiceContext, OrbModel orb, IEnumerable<Creature> targets)
        {
            _evoked++;
            return Task.CompletedTask;
        }
        public override Task AfterCurrentHpChanged(Creature creature, decimal delta)
        {
            if (creature == Owner && delta > 0) Healing += delta;
            return Task.CompletedTask;
        }
        public override Task AfterDeath(PlayerChoiceContext choiceContext, Creature creature, bool wasRemovalPrevented, float deathAnimLength)
        {
            if (creature == Owner && !wasRemovalPrevented) Deaths++;
            return Task.CompletedTask;
        }
    }

    private sealed class OrbCombat : IDisposable
    {
        public CombatState State { get; } = new();
        public Player Player { get; }
        public Creature Enemy { get; }
        public MegaCrit.Sts2.Core.Entities.Orbs.OrbQueue Queue => Player.PlayerCombatState!.OrbQueue;
        public ShurikenOrb? Orb => Queue.Orbs.OfType<ShurikenOrb>().SingleOrDefault();
        public int Stock => Orb?.StackCount ?? 0;
        public int Capacity => Queue.Capacity;
        public int Tokens => Player.Piles.SelectMany(pile => pile.Cards).Count(card => card is StrongShurikenTokenRedesignV1);

        public OrbCombat(bool ninjaSlayer = false, CharacterModel? character = null)
        {
            CombatManager.Instance.History.Clear();
            Player = Player.CreateForNewRun(character ?? (ninjaSlayer
                ? ModelDb.Character<NinjaSlayerCharacter>() : ModelDb.Character<Ironclad>()), UnlockState.all, 1);
            Player.InitializeSeed("orb-contract");
            State.AddPlayer(Player);
            Player.ResetCombatState();
            Enemy = AddEnemy();
#if NINJASLAYER_CHANNEL_STABLE
            AccessTools.Field(typeof(CombatManager), "_pendingLoss").SetValue(CombatManager.Instance, null);
            AccessTools.Field(typeof(CombatManager), "_state").SetValue(CombatManager.Instance, State);
            AccessTools.Property(typeof(CombatManager), nameof(CombatManager.IsInProgress)).SetValue(CombatManager.Instance, true);
#else
            FieldInfo field = AccessTools.Field(typeof(CombatManager), "_turnState");
            object turn = Activator.CreateInstance(field.FieldType, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, [State], null)!;
            field.FieldType.GetProperty("IsInProgress")!.SetValue(turn, true);
            field.SetValue(CombatManager.Instance, turn);
#endif
            PowerCmd.Apply<EvokeObserver>(Choice, Player.Creature, 1, Player.Creature, null).GetAwaiter().GetResult();
        }

        public Creature AddEnemy()
        {
            var enemy = new Creature(ModelDb.Monster<DampCultist>().ToMutable(), CombatSide.Enemy, null) { CombatState = State };
            AccessTools.Method(typeof(CombatState), "AttachCreature").Invoke(State, [enemy]);
            State.AddCreature(enemy);
            enemy.SetMaxHpInternal(1000);
            enemy.SetCurrentHpInternal(1000);
            return enemy;
        }

        public CardModel Card()
        {
            CardModel card = State.CreateCard<StrikeIronclad>(Player);
            PileType.Hand.GetPile(Player).AddInternal(card, -1, silent: true);
            return card;
        }

        public void Dispose() => Player.PlayerCombatState!.AfterCombatEnd();
    }
}
