using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Odds;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Content;
using STS2RitsuLib;
using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.RunData;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static readonly Type SeedRules = typeof(NinjaSlayerRunData).Assembly.GetType("NinjaSlayer.Content.SingleplayerSeedRules", true)!;
    private static object SeedHandle => AccessTools.Property(SeedRules, "Data").GetValue(null)!;
    private static object SeedData(RunState run) => AccessTools.Method(SeedHandle.GetType(), "Get").Invoke(SeedHandle, [run])!;
    private static object SeedValue(object state, string name) => state.GetType().GetProperty(name)!.GetValue(state)!;
    private static string SeedJson(object state) => JsonSerializer.Serialize(state, state.GetType());
    private static void SetSeedData(RunState run, object state) => AccessTools.Method(SeedHandle.GetType(), "Set").Invoke(SeedHandle, [run, state]);

    private async Task VerifySingleplayerSeeds()
    {
        SaveManager.Instance.InitSettingsDataForTest();
        SaveManager.Instance.InitPrefsDataForTest();
        MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
        var patcher = RitsuLibFramework.CreatePatcher("NinjaSlayer.SeedContracts", "Product");
        // Exercise the actual candidate patch targets, rather than linking the implementation into the test.
        foreach (Type type in typeof(SingleplayerRoomSeedPatch).Assembly.GetTypes()
            .Where(type => type.Namespace == "NinjaSlayer.Code.Patches" && type.Name.StartsWith("Singleplayer", StringComparison.Ordinal)))
            typeof(ModPatcherExtensions).GetMethod("RegisterPatch")!.MakeGenericMethod(type).Invoke(null, [patcher]);
        Require(patcher.PatchAll(), "Seed patches did not install on this host.");
        var sceneBoundary = new Harmony("NinjaSlayer.SeedContracts.SceneBoundary");
        foreach (Type room in new[] { typeof(EventRoom), typeof(MerchantRoom), typeof(CombatRoom) })
            sceneBoundary.Patch(AccessTools.Method(room, nameof(AbstractRoom.EnterInternal)),
                prefix: new HarmonyMethod(GetType(), nameof(SkipSeedRoomPresentation)) { priority = Priority.Last });
        sceneBoundary.Patch(AccessTools.Method(typeof(CombatRoom), nameof(CombatRoom.OfferRoomEndRewards)),
            prefix: new HarmonyMethod(GetType(), nameof(SkipSeedRoomPresentation)) { priority = Priority.Last });
        var results = new Dictionary<string, string>();
        foreach (string seed in new[] { "CFC00", "NINJA", "ZZZZZZZZ" })
        {
            results[seed] = await SeedTranscript(seed);
            Require(results[seed] == await SeedTranscript(seed), "Identical single-player run changed its seed transcript.");
        }
        GD.Print("PASS single-player deterministic encounters, shuffle, rewards/rerolls, pity, potions and all 400 relic rarities");
        await VerifySeedExclusions();
        GD.Print("PASS native characters, one-player Host/Client, two-player runs and original CFC ownership are untouched");
#if !NINJASLAYER_CHANNEL_STABLE
        string? oraclePath = System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_CFC_ORACLE");
        if (oraclePath is not null)
        {
            patcher.UnpatchAll();
            await CompareCfcOracle(oraclePath, results);
        }
        else GD.Print("NOT RUN: CFC binary differential (no oracle DLL supplied).");
#else
        GD.Print("NOT RUN: CFC binary differential on stable (CFC 1.1.2 references the preview 64-bit RNG API).");
#endif
        sceneBoundary.UnpatchAll(sceneBoundary.Id);
        patcher.UnpatchAll();
    }

    private static bool SkipSeedRoomPresentation(ref Task __result) { __result = Task.CompletedTask; return false; }

    private static RunState SeedRun(string seed, bool ninja = true, NetGameType mode = NetGameType.Singleplayer, int playerCount = 1)
    {
        var player = Player.CreateForNewRun(ninja ? ModelDb.Character<NinjaSlayerCharacter>() : ModelDb.Character<Ironclad>(), UnlockState.all, 1);
        List<Player> players = [player];
        if (playerCount == 2) players.Add(Player.CreateForNewRun<Ironclad>(UnlockState.all, 2));
        var run = RunState.CreateForTest(players, seed: seed);
        // Only native random factories are under test; no UI, disk saves or network connections are started.
        AccessTools.Property(typeof(RunManager), "State").SetValue(RunManager.Instance, run);
        INetGameService service;
        if (mode == NetGameType.Singleplayer) service = new NetSingleplayerGameService();
        else
        {
            service = DispatchProxy.Create<INetGameService, SeedNetworkMode>();
            ((SeedNetworkMode)(object)service).Mode = mode;
        }
        AccessTools.Property(typeof(RunManager), "NetService").SetValue(RunManager.Instance, service);
        AccessTools.Property(typeof(RunManager), "AscensionManager").SetValue(RunManager.Instance, new MegaCrit.Sts2.Core.Entities.Ascension.AscensionManager(0));
        LocalContext.NetId = 1;
        AccessTools.Method(typeof(RitsuLibFramework), "PublishLifecycleEvent").MakeGenericMethod(typeof(RunStartedEvent))
            .Invoke(null, [new RunStartedEvent(run, false, false, DateTimeOffset.UtcNow), "RunStartedEvent"]);
        return run;
    }

    private static string SampleSeedRng(Rng rng) => string.Join(',', Enumerable.Range(0, 8).Select(_ => rng.NextInt(1000000)));

    private static async Task<string> SeedTranscript(string seed, bool oracle = false)
    {
        RunState run = SeedRun(seed);
        Player player = run.Players[0];
        if (oracle) InitializeOracleState(run);
        var transcript = new List<string>();
        var relicRng = new Rng(2468);
        for (int i = 0; i < 400; i++) transcript.Add(RelicFactory.RollRarity(relicRng).ToString());
        Require(SampleSeedRng(relicRng) == SampleSeedRng(new Rng(2468)), "Relic rarity consumed the caller RNG.");

        AbstractRoom[] rooms =
        [
            new EventRoom(ModelDb.Event<Neow>()), new MerchantRoom(),
            new CombatRoom(ModelDb.AllEncounters.First(e => e.RoomType == RoomType.Monster).ToMutable(), run),
            new CombatRoom(ModelDb.AllEncounters.First(e => e.RoomType == RoomType.Elite).ToMutable(), run),
            new EventRoom(ModelDb.AllEvents.First(e => e is not AncientEventModel)), new MerchantRoom(),
            new CombatRoom(ModelDb.AllEncounters.First(e => e.RoomType == RoomType.Boss).ToMutable(), run),
            new EventRoom(ModelDb.Event<Neow>())
        ];
        foreach (AbstractRoom room in rooms)
        {
            run.PushRoom(room);
            await room.Enter(run, false);
            transcript.Add(SampleSeedRng(player.PlayerRng.Rewards));
            if (room is CombatRoom combat)
            {
                combat.Encounter.GenerateMonstersWithSlots(run);
                transcript.Add(string.Join(',', combat.Encounter.MonstersWithSlots.Select(pair => pair.Item1.Id.Entry + ":" + pair.Item2)));
                player.ResetCombatState();
                player.PopulateCombatState(new Rng(77), combat.CombatState);
                transcript.Add(string.Join(',', player.PlayerCombatState!.DrawPile.Cards.Select(card => card.Id.Entry)));
                transcript.Add(SampleSeedRng(run.Rng.Shuffle));
                await combat.OfferRoomEndRewards();
            }
            if (room is EventRoom)
            {
                foreach (var options in new[]
                {
                    CardCreationOptions.ForNonCombatWithUniformOdds([player.Character.CardPool]),
                    CardCreationOptions.ForNonCombatWithDefaultOdds([player.Character.CardPool])
                }) transcript.Add(SampleSeedRng(options.RngOverride!));
                var cards = new List<Reward> { new CardReward(CardCreationOptions.ForNonCombatWithDefaultOdds([player.Character.CardPool]), 3, player) };
                var rewards = new RewardsSet(player).WithCustomRewards(cards);
                foreach (var card in rewards.Rewards.OfType<CardReward>()) RecordCardRewardSeeds(card, transcript);
            }
            else
            {
                var options = CardCreationOptions.ForRoom(player, room.RoomType);
                transcript.Add(SampleSeedRng(options.RngOverride!));
                if (room is CombatRoom)
                {
                    var rewards = new RewardsSet(player).WithRewardsFromRoom(room);
                    foreach (var card in rewards.Rewards.OfType<CardReward>()) RecordCardRewardSeeds(card, transcript);
                }
            }
            foreach (CardRarityOddsType type in new[] { CardRarityOddsType.RegularEncounter, CardRarityOddsType.EliteEncounter,
                CardRarityOddsType.BossEncounter, CardRarityOddsType.Shop, CardRarityOddsType.Uniform })
                for (int i = 0; i < 9; i++) transcript.Add(player.PlayerOdds.CardRarity.Roll(type).ToString());
            transcript.Add(player.PlayerOdds.CardRarity.RollWithoutChangingFutureOdds(CardRarityOddsType.Shop).ToString());
            for (int i = 0; i < 3; i++)
            {
#if NINJASLAYER_CHANNEL_STABLE
                transcript.Add(player.PlayerOdds.PotionReward.Roll(player, RunManager.Instance.AscensionManager, RoomType.Monster).ToString());
#else
                transcript.Add(player.PlayerOdds.PotionReward.Roll(player, RoomType.Monster).ToString());
#endif
                transcript.Add(PotionFactory.CreateRandomPotionInCombat(player, new Rng(5)).Id.Entry);
                transcript.Add(string.Join(',', PotionFactory.CreateRandomPotionsOutOfCombat(player, 3, new Rng(9)).Select(p => p.Id.Entry)));
            }
            // Mirrors CFC's entry callback on a restored parent event, not a custom deduplication policy.
            if (room is EventRoom) await room.Enter(run, true);
        }
        if (!oracle)
        {
            object saved = SeedData(run);
            string json = SeedJson(saved);
            Require(!json.Contains("CardRngIndex", StringComparison.Ordinal) && !json.Contains("ResetRarityRng", StringComparison.Ordinal),
                "Room-local reset markers leaked into the save.");
            object restored = JsonSerializer.Deserialize(json, saved.GetType())!;
            SetSeedData(run, restored);
            Require(SeedJson(SeedData(run)) == json, "Saved seed counters or pity values changed on serialization roundtrip.");
            Require((int)SeedValue(restored, "MonsterKilled") == 1 && (int)SeedValue(restored, "EliteKilled") == 1
                && (int)SeedValue(restored, "BossKilled") == 1 && (int)SeedValue(restored, "ShopEntered") == 2
                && (int)SeedValue(restored, "EventEntered") == 6 && (int)SeedValue(restored, "AncientVisited") == 4,
                "Room entry, ancient restoration or reward count differs from CFC.");
        }
        return string.Join('|', transcript);
    }

    private static void RecordCardRewardSeeds(CardReward card, List<string> transcript)
    {
        foreach (string name in new[] { "Options", "RerollOptions" })
            transcript.Add(SampleSeedRng(((CardCreationOptions)AccessTools.Property(typeof(CardReward), name).GetValue(card)!).RngOverride!));
        card.Populate();
        transcript.Add(string.Join(',', card.Cards.Select(c => c.Id.Entry + ":" + c.IsUpgraded)));
    }

    private static async Task VerifySeedExclusions()
    {
        foreach (var test in new[] { (false, NetGameType.Singleplayer, 1), (true, NetGameType.Host, 1),
            (true, NetGameType.Client, 1), (true, NetGameType.Singleplayer, 2) })
        {
            RunState run = SeedRun("EXCLUDED", test.Item1, test.Item2, test.Item3);
            Require(((List<RelicRarity>)SeedValue(SeedData(run), "RelicRarities")).Count == 0,
                "Excluded run initialized the single-player relic sequence.");
            string before = SeedJson(SeedData(run));
            Rng original = run.Players[0].PlayerRng.Rewards;
            var room = new MerchantRoom(); run.PushRoom(room); await room.Enter(run, false);
            var options = CardCreationOptions.ForRoom(run.Players[0], RoomType.Shop);
            var supplied = new Rng(88); var native = new Rng(88);
            float roll = native.NextFloat();
            RelicRarity expected = roll < 0.5f ? RelicRarity.Common : roll < 0.83f ? RelicRarity.Uncommon : RelicRarity.Rare;
            Require(options.RngOverride is null && ReferenceEquals(original, run.Players[0].PlayerRng.Rewards)
                && RelicFactory.RollRarity(supplied) == expected && SampleSeedRng(supplied) == SampleSeedRng(native)
                && before == SeedJson(SeedData(run)), "Excluded run was modified by singleplayer seed rules.");
        }
        var originalOwner = new Harmony("CFCRacingMod");
        var target = AccessTools.Method(typeof(RelicFactory), nameof(RelicFactory.RollRarity), [typeof(Rng)]);
        originalOwner.Patch(target, prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(SeedOriginalOwnerMarker)));
        try
        {
            RunState single = SeedRun("CFC-COEXIST");
            Require(((List<RelicRarity>)SeedValue(SeedData(single), "RelicRarities")).Count == 0,
                "Original CFC owner did not suppress single-player initialization.");
            string before = SeedJson(SeedData(single));
            var room = new MerchantRoom(); single.PushRoom(room); await room.Enter(single, false);
            Require(before == SeedJson(SeedData(single)), "Original CFC owner did not suppress the complete NinjaSlayer seed rule set.");
        }
        finally { originalOwner.UnpatchAll(originalOwner.Id); }
    }

    private static void SeedOriginalOwnerMarker() { }
    public class SeedNetworkMode : DispatchProxy
    {
        public NetGameType Mode;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name == "get_Type"
            ? Mode : throw new InvalidOperationException("Unexpected network call in RNG exclusion contract.");
    }

    private static Assembly? _cfcOracle;
    private static object? _cfcHandle;
    private static void RegisterCfcOracleData<T>() where T : class, new()
    {
        using (RitsuLibFramework.BeginModDataRegistration("NinjaSlayer.CfcOracle"))
            _cfcHandle = RitsuLibFramework.GetRunSavedDataStore("NinjaSlayer.CfcOracle").Register("oracle", () => new T());
        _cfcOracle!.GetType("Sts2RacingMod.Sts2RacingMod.CFCRacingMod", true)!.GetField("RunSave")!.SetValue(null, _cfcHandle);
    }
    private static void InitializeOracleState(RunState run)
    {
        object data = AccessTools.Method(_cfcHandle!.GetType(), "Get").Invoke(_cfcHandle, [run])!;
        var rarities = (List<RelicRarity>)data.GetType().GetProperty("relicRarityList")!.GetValue(data)!;
        var rng = new Rng(run.Rng.Seed, "RelicRarityRng");
        for (int i = 0; i < 400; i++) { float roll = rng.NextFloat(); rarities.Add(roll < 0.5 ? RelicRarity.Common : roll < 0.83 ? RelicRarity.Uncommon : RelicRarity.Rare); }
    }
    private static async Task CompareCfcOracle(string path, Dictionary<string, string> expected)
    {
        byte[] bytes = System.IO.File.ReadAllBytes(path);
        Require(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) == "032894C8D157E9B667CE80BEFDC50DD0DE2E37189B5BD80C5A4F42A0B1D5014F", "CFC oracle DLL changed; re-audit its rules.");
        // The reference mod requests RitsuLib 0.5.18; these callbacks use the same run-data API in our pinned SDK.
        // Bind only that reference to the active contract runtime; missing API members must still fail the test.
        ResolveEventHandler resolve = (_, args) => new AssemblyName(args.Name).Name switch
        {
            "STS2-RitsuLib" => typeof(RitsuLibFramework).Assembly,
            "0Harmony" => typeof(Harmony).Assembly,
            "sts2" => typeof(Player).Assembly,
            _ => null
        };
        AppDomain.CurrentDomain.AssemblyResolve += resolve;
        var harmony = new Harmony("NinjaSlayer.SeedContracts.CfcOracle");
        string[] names = ["RngFix.PlayerPatch", "RngFix.EncounterModelPatch", "RngFix.CombatRoomPatch", "EventRoomPatch", "MerchantRoomPatch",
            "RngFix.CardRarityOddsPatch", "RngFix.CardCreationOptionsPatch", "RngFix.RewardsSetPatch", "RngFix.PotionRewardOddsPatch", "RngFix.PotionFactoryPatch", "RngFix.RelicFactoryPatch"];
        try
        {
            _cfcOracle = Assembly.LoadFrom(path);
            AccessTools.Method(typeof(OrbContractRunner), nameof(RegisterCfcOracleData)).MakeGenericMethod(
                _cfcOracle.GetType("Sts2RacingMod.Sts2RacingMod.Saves.RaceRunSave", true)!).Invoke(null, null);
            foreach (string name in names)
                foreach (Type type in _cfcOracle.GetType("Sts2RacingMod.Sts2RacingMod.Patches." + name, true)!.GetNestedTypes())
                    harmony.CreateClassProcessor(type).Patch();
            foreach (var pair in expected)
                Require(pair.Value == await SeedTranscript(pair.Key, oracle: true), $"Candidate differs from CFC 1.1.2 for seed {pair.Key}.");
            GD.Print("PASS direct CFC 1.1.2 binary differential: three complete single-player RNG transcripts match");
        }
        finally { harmony.UnpatchAll(harmony.Id); AppDomain.CurrentDomain.AssemblyResolve -= resolve; }
    }
}
