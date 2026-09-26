using System.Reflection;
using System.Text.Json.Serialization;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib;
using STS2RitsuLib.RunData;

namespace NinjaSlayer.Content;

// Single-player seed semantics from CFCRacingMod 1.1.2; no racing UI or qualification rules.
internal static class SingleplayerSeedRules
{
    internal static RunSavedData<SingleplayerSeedState> Data { get; private set; } = null!;
    private static readonly MethodInfo RelicRoll = AccessTools.Method(
        typeof(RelicFactory), nameof(RelicFactory.RollRarity), [typeof(Rng)]);

    internal static void Register(string modId)
    {
        Data = RitsuLibFramework.GetRunSavedDataStore(modId).Register(
            "cfc_singleplayer_rng", () => new SingleplayerSeedState(),
            new RunSavedDataOptions { WritePolicy = RunSavedDataWritePolicy.WhenNonDefault });
        RitsuLibFramework.SubscribeLifecycle<RunStartedEvent>(evt =>
        {
            if (ActiveRun(evt.RunState) is not { } run) return;
            Data.Modify(run, state =>
            {
                var rng = new Rng(run.Rng.Seed, "RelicRarityRng");
                while (state.RelicRarities.Count < 400)
                {
                    float roll = rng.NextFloat();
                    state.RelicRarities.Add(roll < 0.5 ? RelicRarity.Common
                        : roll < 0.83 ? RelicRarity.Uncommon : RelicRarity.Rare);
                }
            });
        });
    }

    internal static RunState? ActiveRun(IRunState? candidate = null)
    {
        // A one-player network lobby is still multiplayer. Do not touch its RNGs or saved counters.
        if (RunManager.Instance.NetService?.Type != NetGameType.Singleplayer) return null;
        RunState? run = (candidate ?? RunManager.Instance.DebugOnlyGetState()) as RunState;
        if (run is null || run.Players.Count != 1 || !NinjaSlayerContentAccess.HasNinjaSlayer(run)) return null;
        // The original mod owns its whole rule set when both mods are loaded.
        if (Harmony.GetPatchInfo(RelicRoll)?.Owners.Contains("CFCRacingMod") == true) return null;
        return run;
    }

    internal static int RoomCount(SingleplayerSeedState state, RoomType type) => type switch
    {
        RoomType.Elite => state.EliteKilled,
        RoomType.Boss => state.BossKilled,
        RoomType.Shop => state.ShopEntered,
        _ => state.MonsterKilled
    };

    internal static Rng CreateRng(ulong seed) =>
#if NINJASLAYER_CHANNEL_STABLE
        new(unchecked((uint)seed));
#else
        new(seed);
#endif
}

internal sealed class SingleplayerSeedState
{
    public int MonsterKilled { get; set; }
    public int EliteKilled { get; set; }
    public int BossKilled { get; set; }
    public int EventEntered { get; set; }
    public int AncientVisited { get; set; }
    public int ShopEntered { get; set; }
    public int PotionDropCalls { get; set; }
    public int PotionGenerationCalls { get; set; }
    public float MonsterRarityOdds { get; set; }
    public float EliteRarityOdds { get; set; }
    public float BossRarityOdds { get; set; }
    public float ShopRarityOdds { get; set; }
    public List<RelicRarity> RelicRarities { get; set; } = [];

    [JsonIgnore] public bool ResetRarityRng { get; set; }
    [JsonIgnore] public int CardRngIndex { get; set; }
}
