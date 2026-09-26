using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Rngs;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class SingleplayerRoomSeedPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_singleplayer_room_seed";
    public static string Description => "Use CFC room reward seeds in solo Ninja Slayer runs.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(AbstractRoom), nameof(AbstractRoom.Enter), [typeof(IRunState), typeof(bool)])];
    private static readonly FieldInfo PlayerRngs = AccessTools.Field(typeof(PlayerRngSet), "_rngs");

    public static void Prefix(AbstractRoom __instance, IRunState? runState)
    {
        if (SingleplayerSeedRules.ActiveRun(runState) is not { } run) return;
        var state = SingleplayerSeedRules.Data.Get(run);
        state.ResetRarityRng = true;
        state.CardRngIndex = 0;
        int multiplier = __instance.RoomType switch
        {
            RoomType.Elite => 5281, RoomType.Boss => 5282, RoomType.Shop => 5283, _ => 5280
        };
        var rngs = (Dictionary<PlayerRngType, Rng>)PlayerRngs.GetValue(run.Players[0].PlayerRng)!;
        rngs[PlayerRngType.Rewards] = SingleplayerSeedRules.CreateRng(run.Rng.Seed
            + unchecked((ulong)(SingleplayerSeedRules.RoomCount(state, __instance.RoomType) * multiplier)));
    }
}

public sealed class SingleplayerRoomCountPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_singleplayer_room_count";
    public static string Description => "Count CFC event/shop entries and combat reward calls at their native boundaries.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(EventRoom), nameof(EventRoom.EnterInternal), [typeof(IRunState), typeof(bool)]),
        new(typeof(MerchantRoom), nameof(MerchantRoom.EnterInternal), [typeof(IRunState), typeof(bool)]),
        new(typeof(CombatRoom), nameof(CombatRoom.OfferRoomEndRewards), Type.EmptyTypes)
    ];

    public static void Prefix(AbstractRoom __instance)
    {
        if (SingleplayerSeedRules.ActiveRun() is not { } run) return;
        // CFC counts the callbacks themselves, including restored parent event entries.
        SingleplayerSeedRules.Data.Modify(run, state =>
        {
            switch (__instance)
            {
                case EventRoom room:
                    state.EventEntered++;
                    if (room.CanonicalEvent is AncientEventModel)
                    {
                        state.AncientVisited++;
                        state.MonsterRarityOdds = state.EliteRarityOdds = state.BossRarityOdds = state.ShopRarityOdds = -0.05f;
                    }
                    break;
                case MerchantRoom:
                    state.ShopEntered++;
                    break;
                case CombatRoom room:
                    switch (room.RoomType)
                    {
                        case RoomType.Monster: state.MonsterKilled++; break;
                        case RoomType.Elite: state.EliteKilled++; break;
                        case RoomType.Boss: state.BossKilled++; break;
                    }
                    break;
            }
        });
    }
}

public sealed class SingleplayerShuffleSeedPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_singleplayer_shuffle_seed";
    public static string Description => "Use CFC combat shuffle seeds, including encounter-only boss seeds.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(Player), nameof(Player.PopulateCombatState), [typeof(Rng), typeof(CombatState)])];
    private static readonly FieldInfo RunRngs = AccessTools.Field(typeof(RunRngSet), "_rngs");

    public static void Prefix(ref Rng rng, CombatState state)
    {
        if (SingleplayerSeedRules.ActiveRun(state.RunState) is not { } run) return;
        var saved = SingleplayerSeedRules.Data.Get(run);
        switch (run.CurrentRoom!.RoomType)
        {
            case RoomType.Monster: rng = SingleplayerSeedRules.CreateRng(run.Rng.Seed + unchecked((ulong)(saved.MonsterKilled * 5280L))); break;
            case RoomType.Elite: rng = SingleplayerSeedRules.CreateRng(run.Rng.Seed + unchecked((ulong)(saved.EliteKilled * 5281L))); break;
            case RoomType.Boss:
                rng = SingleplayerSeedRules.CreateRng(run.CurrentRoom is CombatRoom combat
                    ? unchecked((ulong)StringHelper.GetDeterministicHashCode(combat.Encounter.Id.Entry))
                    : run.Rng.Seed + unchecked((ulong)(saved.BossKilled * 5282L)));
                break;
        }
        ((Dictionary<RunRngType, Rng>)RunRngs.GetValue(run.Rng)!)[RunRngType.Shuffle] = rng;
    }
}

public sealed class SingleplayerEncounterSeedPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_singleplayer_encounter_seed";
    public static string Description => "Isolate CFC monster generation by encounter and room count.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(EncounterModel), nameof(EncounterModel.GenerateMonstersWithSlots), [typeof(IRunState)])];
    private static readonly FieldInfo EncounterRng = AccessTools.Field(typeof(EncounterModel), "_rng");

    public static void Prefix(EncounterModel __instance, IRunState runState)
    {
        if (SingleplayerSeedRules.ActiveRun(runState) is not { } run) return;
        __instance.AssertMutable();
        var saved = SingleplayerSeedRules.Data.Get(run);
        int count = __instance.RoomType switch
        {
            RoomType.Monster => saved.MonsterKilled,
            RoomType.Elite => saved.EliteKilled,
            RoomType.Boss => saved.BossKilled,
            _ => saved.EventEntered
        };
        EncounterRng.SetValue(__instance, SingleplayerSeedRules.CreateRng(unchecked((ulong)((long)run.Rng.Seed + count))
            + unchecked((ulong)StringHelper.GetDeterministicHashCode(__instance.Id.Entry))));
    }
}
