using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Odds;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class SingleplayerEventCardSeedPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_singleplayer_event_card_seed";
    public static string Description => "Apply CFC event and ancient card creation seeds.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(CardCreationOptions), nameof(CardCreationOptions.ForNonCombatWithUniformOdds),
            [typeof(IEnumerable<CardPoolModel>), typeof(Func<CardModel, bool>)]),
        new(typeof(CardCreationOptions), nameof(CardCreationOptions.ForNonCombatWithDefaultOdds),
            [typeof(IEnumerable<CardPoolModel>), typeof(Func<CardModel, bool>)])
    ];
    public static void Postfix(CardCreationOptions __result)
    {
        if (SingleplayerSeedRules.ActiveRun() is not { } run) return;
        var state = SingleplayerSeedRules.Data.Get(run);
        int count = run.CurrentRoom is EventRoom { CanonicalEvent: AncientEventModel }
            ? state.AncientVisited : state.EventEntered;
        __result.WithRngOverride(SingleplayerSeedRules.CreateRng(run.Rng.Seed + unchecked((ulong)((count + state.CardRngIndex) * 5284L))));
        state.CardRngIndex++;
    }
}

public sealed class SingleplayerRoomCardSeedPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_singleplayer_room_card_seed";
    public static string Description => "Apply CFC room card creation sequence seeds.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(CardCreationOptions), nameof(CardCreationOptions.ForRoom), [typeof(Player), typeof(RoomType)])];
    public static void Postfix(Player player, CardCreationOptions __result)
    {
        if (SingleplayerSeedRules.ActiveRun(player.RunState) is not { } run) return;
        var state = SingleplayerSeedRules.Data.Get(run);
        RoomType type = run.CurrentRoom!.RoomType;
        if (type == RoomType.Event) throw new InvalidOperationException("ForRoom should not be used in event rooms");
        int count = SingleplayerSeedRules.RoomCount(state, type);
        __result.WithRngOverride(SingleplayerSeedRules.CreateRng(run.Rng.Seed + unchecked((ulong)(count + state.CardRngIndex) * 5284UL)));
        state.CardRngIndex++;
    }
}

public sealed class SingleplayerCardRewardSeedPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_singleplayer_card_reward_seed";
    public static string Description => "Separate CFC card rewards and rerolls from other reward randomness.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(RewardsSet), nameof(RewardsSet.WithRewardsFromRoom), [typeof(AbstractRoom)])];
    private static readonly Func<CardReward, CardCreationOptions> Options = AccessTools.PropertyGetter(typeof(CardReward), "Options")
        .CreateDelegate<Func<CardReward, CardCreationOptions>>();
    private static readonly Func<CardReward, CardCreationOptions?> RerollOptions = AccessTools.PropertyGetter(typeof(CardReward), "RerollOptions")
        .CreateDelegate<Func<CardReward, CardCreationOptions?>>();

    public static void Postfix(RewardsSet __instance, AbstractRoom room) => Apply(__instance, __instance.Rewards, room, false);

    internal static void Apply(RewardsSet rewards, IEnumerable<Reward> source, AbstractRoom room, bool eventRewards)
    {
        if (SingleplayerSeedRules.ActiveRun(rewards.Player.RunState) is not { } run) return;
        var state = SingleplayerSeedRules.Data.Get(run);
        ulong offset;
        if (eventRewards && room is EventRoom evt)
            offset = evt.CanonicalEvent is AncientEventModel
                ? unchecked((ulong)(state.AncientVisited * 2585L)) : unchecked((ulong)(state.EventEntered * 2584L));
        else
        {
            int multiplier = room.RoomType switch
            {
                RoomType.Monster => 2580, RoomType.Elite => 2581, RoomType.Boss => 2582, RoomType.Shop => 2583, _ => 0
            };
            if (multiplier == 0) return; // CFC leaves other room reward types unchanged.
            offset = unchecked((ulong)(SingleplayerSeedRules.RoomCount(state, room.RoomType) * multiplier));
        }
        uint index = 0;
        foreach (CardReward card in source.OfType<CardReward>())
        {
            ulong seed = run.Rng.Seed + index + offset;
            Options(card).WithRngOverride(SingleplayerSeedRules.CreateRng(seed));
            RerollOptions(card)?.WithRngOverride(SingleplayerSeedRules.CreateRng(seed + 1290));
            index++;
        }
    }
}

public sealed class SingleplayerCustomRewardSeedPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_singleplayer_custom_reward_seed";
    public static string Description => "Use CFC event/ancient reward seeds for custom card rewards.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(RewardsSet), nameof(RewardsSet.WithCustomRewards), [typeof(List<Reward>)])];
    public static void Postfix(RewardsSet __instance, List<Reward> rewards)
    {
        if (SingleplayerSeedRules.ActiveRun(__instance.Player.RunState) is not { } run || !rewards.OfType<CardReward>().Any()) return;
        SingleplayerCardRewardSeedPatch.Apply(__instance,
            run.CurrentRoom is EventRoom ? rewards : __instance.Rewards, run.CurrentRoom!, true);
    }
}

public sealed class SingleplayerCardRarityPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_singleplayer_card_rarity";
    public static string Description => "Keep CFC rarity pity independently for each room category.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(CardRarityOdds), nameof(CardRarityOdds.Roll), [typeof(CardRarityOddsType)])];
    public static void Prefix(CardRarityOdds __instance, CardRarityOddsType type) => SetOdds(__instance, type);
    internal static void SetOdds(CardRarityOdds odds, CardRarityOddsType type)
    {
        if (SingleplayerSeedRules.ActiveRun() is not { } run) return;
        var state = SingleplayerSeedRules.Data.Get(run);
        odds.OverrideCurrentValue(type switch
        {
            CardRarityOddsType.EliteEncounter => state.EliteRarityOdds,
            CardRarityOddsType.BossEncounter => state.BossRarityOdds,
            CardRarityOddsType.Shop => state.ShopRarityOdds,
            _ => state.MonsterRarityOdds
        });
    }
    public static void Postfix(CardRarityOdds __instance, CardRarityOddsType type, CardRarity __result)
    {
        if (SingleplayerSeedRules.ActiveRun() is not { } run) return;
        SingleplayerSeedRules.Data.Modify(run, state =>
        {
            float Next(float old) => __result == CardRarity.Rare ? -0.05f : Math.Min(old + __instance.RarityGrowth, 0.4f);
            switch (type)
            {
                case CardRarityOddsType.RegularEncounter:
                case CardRarityOddsType.Uniform: state.MonsterRarityOdds = Next(state.MonsterRarityOdds); break;
                case CardRarityOddsType.EliteEncounter: state.EliteRarityOdds = Next(state.EliteRarityOdds); break;
                case CardRarityOddsType.BossEncounter: state.BossRarityOdds = Next(state.BossRarityOdds); break;
                case CardRarityOddsType.Shop: state.ShopRarityOdds = Next(state.ShopRarityOdds); break;
            }
        });
    }
}

public sealed class SingleplayerCardRarityPreviewPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_singleplayer_card_rarity_preview";
    public static string Description => "Supply CFC pity to non-mutating rarity rolls.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(CardRarityOdds), nameof(CardRarityOdds.RollWithoutChangingFutureOdds), [typeof(CardRarityOddsType)])];
    public static void Prefix(CardRarityOdds __instance, CardRarityOddsType oddsType) => SingleplayerCardRarityPatch.SetOdds(__instance, oddsType);
}

public sealed class SingleplayerCardRaritySeedPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_singleplayer_card_rarity_seed";
    public static string Description => "Reset the CFC rarity stream once per room at the first rarity roll.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(CardRarityOdds), nameof(CardRarityOdds.RollWithoutChangingFutureOdds), [typeof(CardRarityOddsType), typeof(float)])];
    private static readonly FieldInfo OddsRng = AccessTools.Field(typeof(AbstractOdds), "_rng");
#if NINJASLAYER_CHANNEL_STABLE
    private static readonly Action<Rng, int> SetCounter = AccessTools.PropertySetter(typeof(Rng), nameof(Rng.Counter)).CreateDelegate<Action<Rng, int>>();
    private static readonly FieldInfo RngRandom = AccessTools.Field(typeof(Rng), "_random");
#endif
    public static void Prefix(CardRarityOdds __instance, CardRarityOddsType type)
    {
        if (SingleplayerSeedRules.ActiveRun() is not { } run) return;
        var state = SingleplayerSeedRules.Data.Get(run);
        if (!state.ResetRarityRng) return;
        state.ResetRarityRng = false;
        ulong offset = type switch
        {
            CardRarityOddsType.EliteEncounter => unchecked((ulong)(state.EliteKilled * 8521L)),
            CardRarityOddsType.BossEncounter => unchecked((ulong)(state.BossKilled * 8522L)),
            CardRarityOddsType.Shop => unchecked((ulong)(state.ShopEntered * 8523L)),
            _ => unchecked((ulong)(state.MonsterKilled * 8520L))
        };
        ulong seed = run.Rng.Seed + offset;
        if (seed == 0) return;
        var rng = (Rng)OddsRng.GetValue(__instance)!;
#if NINJASLAYER_CHANNEL_STABLE
        SetCounter(rng, 0);
        ((MegaRandom)RngRandom.GetValue(rng)!).Reinitialise(unchecked((uint)seed));
#else
        rng.LoadFromSerializable(SingleplayerSeedRules.CreateRng(seed).ToSerializable());
#endif
    }
}

public sealed class SingleplayerPotionDropSeedPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_singleplayer_potion_drop_seed";
    public static string Description => "Use CFC potion-drop call seeds without altering native potion odds.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(PotionRewardOdds), nameof(PotionRewardOdds.Roll),
#if NINJASLAYER_CHANNEL_STABLE
            [typeof(Player), typeof(AscensionManager), typeof(RoomType)])];
#else
            [typeof(Player), typeof(RoomType)])];
#endif
    private static readonly FieldInfo OddsRng = AccessTools.Field(typeof(AbstractOdds), "_rng");
    public static void Prefix(PotionRewardOdds __instance)
    {
        if (SingleplayerSeedRules.ActiveRun() is not { } run) return;
        var state = SingleplayerSeedRules.Data.Get(run);
        OddsRng.SetValue(__instance, SingleplayerSeedRules.CreateRng(run.Players[0].PlayerRng.Seed + unchecked((ulong)(state.PotionDropCalls * 8520 + 2580))));
        SingleplayerSeedRules.Data.Modify(run, value => value.PotionDropCalls++);
    }
}

public sealed class SingleplayerPotionGenerationSeedPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_singleplayer_potion_generation_seed";
    public static string Description => "Use one CFC seed for each native potion generation batch.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(PotionFactory),
#if NINJASLAYER_CHANNEL_STABLE
            "CreateRandomPotion",
#else
            "CreateRandomPotions",
#endif
            [typeof(IEnumerable<PotionModel>), typeof(int), typeof(Rng)])];
    public static void Prefix(ref Rng rng)
    {
        if (SingleplayerSeedRules.ActiveRun() is not { } run) return;
        var state = SingleplayerSeedRules.Data.Get(run);
        rng = SingleplayerSeedRules.CreateRng(run.Players[0].PlayerRng.Seed + unchecked((ulong)(state.PotionGenerationCalls * 8521 + 2580)));
        SingleplayerSeedRules.Data.Modify(run, value => value.PotionGenerationCalls++);
    }
}

public sealed class SingleplayerRelicRarityPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_singleplayer_relic_rarity";
    public static string Description => "Consume CFC's precomputed relic-rarity sequence.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(RelicFactory), nameof(RelicFactory.RollRarity), [typeof(Rng)])];
    public static bool Prefix(ref RelicRarity __result)
    {
        if (SingleplayerSeedRules.ActiveRun() is not { } run) return true;
        __result = SingleplayerSeedRules.Data.Get(run).RelicRarities[0];
        SingleplayerSeedRules.Data.Modify(run, state => state.RelicRarities.RemoveAt(0));
        return false;
    }
}
