using System.Reflection;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using global::AutoAnthony;
using ChaosCardGenerator;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;
using STS2RitsuLib;
using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;
using STS2RitsuLib.RunData;

namespace NinjaSlayer.AutoAnthony;

internal static class NinjaAnthonyRun
{
    private const string SaveKey = "autoanthony_pool";
    private static RunSavedData<NinjaAnthonyState> Data = null!;
    private static NinjaAnthonyState? Current;
    internal static bool Active => Current?.Settings is { Enabled: true, AddGeneratedCards: true };
    private static readonly MethodInfo ResetCards = AccessTools.Method(typeof(ChaosRunDefinitions), "ResetCanonicalCardCaches", [typeof(IEnumerable<Type>)])
        ?? throw new MissingMethodException(typeof(ChaosRunDefinitions).FullName, "ResetCanonicalCardCaches");

    internal static void Register(ModPatcher patcher)
    {
        using (RitsuLibFramework.BeginModDataRegistration(NinjaSlayerIds.ModId))
            Data = RitsuLibFramework.GetRunSavedDataStore(NinjaSlayerIds.ModId).Register(SaveKey,
                () => new NinjaAnthonyState(), new() { WritePolicy = RunSavedDataWritePolicy.WhenNonDefault });
        patcher.RegisterPatch<SingleplayerStart>();
        patcher.RegisterPatch<MultiplayerStart>();
        patcher.RegisterPatch<RestorePool>();
        patcher.RegisterPatch<SavePool>();
        patcher.RegisterPatch<StartingDeck>();
        patcher.RegisterPatch<RewardPool>();
        RitsuLibFramework.SubscribeLifecycle<RunSavedDataPreparingEvent>(evt => CapturePool(evt.RunState));
        RitsuLibFramework.SubscribeLifecycle<RunLoadedEvent>(evt =>
        {
            Current = Data.Get((RunState)evt.RunState);
            if (Active) Install(Current);
        });
        RitsuLibFramework.SubscribeLifecycle<RunEndedEvent>(_ => Current = null);
    }

    private static void CapturePool(RunState run)
    {
        if (Current is null || !run.Players.Any(p => p.Character is INinjaSlayerCharacter)) return;
        if (Active)
        {
            var definitions = ExternalComponentCharacterApi.GetDefinitions(NinjaComponentSources.ProfileId);
            Current.Cards = definitions.Select(definition => CardTinkeringApi.SerializeCard(definition.Card)).ToList();
            Current.Portraits = definitions.Select(definition => new NinjaAnthonyPortrait(definition.PortraitPath,
                definition.PortraitSourceId!, definition.PortraitVariantId, definition.PortraitVariantPath)).ToList();
        }
        Data.Set(run, Current);
    }

    internal static void InstallLibraryPreview()
    {
        // A preview definition is required by the native card library even before a run is active.
        Generate("NinjaSlayer/AutoAnthony/library", new(true, true, false, true, false, true, false, false, ComponentSurpriseMode.Disabled));
        Current = null;
    }

    private static void Generate(string seed, ComponentRunSettings settings)
    {
        Current = new() { Settings = settings };
        if (!Active) return;
        int numericSeed = BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes("NinjaSlayer/AutoAnthony/v1/" + seed))) & int.MaxValue;
        var generator = new RandomCardGenerator(new ComponentProfileRequest(NinjaComponentSources.ProfileId,
            GeneratedCharacter.Ironclad, settings.UltimateChaos), numericSeed,
            balancedValues: settings.NumericBalanceOptimization, randomizeNumericValues: settings.NumericRandomMode);
        var artRandom = new Random(numericSeed);
        GeneratedRarity[] rarities = Enumerable.Range(0, Bridge.CardCount).Select(slot => slot < 10 ? GeneratedRarity.Basic
            : slot < 30 ? GeneratedRarity.Common : slot < 65 ? GeneratedRarity.Uncommon : GeneratedRarity.Rare).ToArray();
        var cards = rarities.Select(generator.Generate).ToArray();
        // These are Anthony's public pool repair routines and character-pool thresholds.
        // Keep Sly/discard, Osty/summon and derivative producers paired before installing slots.
        var repairRandom = new Random(numericSeed);
        string failure = string.Empty;
        bool valid = false;
        for (int attempt = 0; attempt < 4 && !valid; attempt++)
        {
            if (attempt > 0) cards = rarities.Select(generator.Generate).ToArray();
            if (settings.ReplaceStartingCards)
            {
                var starting = cards.Take(10).ToArray();
                if (!StartingPoolConstraintResolver.TryRepair(starting, generator, repairRandom, 4, 4, 20000, out failure)) continue;
                starting.CopyTo(cards, 0);
            }
            valid = OstyPoolConstraintResolver.TryResolve(cards, rarities, generator, repairRandom, 20000, out failure)
                && SlyPoolConstraintResolver.TryResolve(cards, rarities, generator, repairRandom, 20000, out failure)
                && DerivativePoolConstraintResolver.TryRepairAndResolve(cards, rarities, generator, repairRandom, 20000, out failure);
        }
        if (!valid) throw new InvalidOperationException("Cannot generate a supported NinjaSlayer component pool: " + failure);
        OstyPoolConstraintResolver.Audit(cards);
        SlyPoolConstraintResolver.Audit(cards);
        DerivativePoolConstraintResolver.Audit(cards);
        for (int slot = 0; slot < Bridge.CardCount; slot++)
        {
            GeneratedCard card = cards[slot];
            Current.Cards.Add(CardTinkeringApi.SerializeCard(card));
            CardModel art = ModelDb.GetById<CardModel>(ModelDb.GetId(NinjaComponentSources.Sources[slot % NinjaComponentSources.Sources.Length].Card));
            string? variantId = null, variantPath = null;
            if (settings.RandomCardArt)
            {
                // Anthony exposes external card definitions publicly, but its replacement-art
                // enumeration is internal. Use that exact boundary rather than a second registry.
                Type portraits = typeof(ExternalComponentCharacterApi).Assembly.GetType("AutoAnthony.ChaosPortraitCompatibility", true)!;
                object[] variants = ((IEnumerable)AccessTools.Method(portraits, "GetAvailableVariants")
                    .Invoke(null, [art, art.PortraitPath])!).Cast<object>().ToArray();
                object variant = variants[artRandom.Next(variants.Length)];
                variantId = (string)AccessTools.Property(variant.GetType(), "Id").GetValue(variant)!;
                variantPath = (string?)AccessTools.Property(variant.GetType(), "Path").GetValue(variant);
            }
            Current.Portraits.Add(new(art.PortraitPath, art.Id.ToString(), variantId, variantPath));
        }
        Install(Current);
    }

    private static void Install(NinjaAnthonyState state)
    {
        if (state.Cards.Count != Bridge.CardCount || state.Portraits.Count != Bridge.CardCount)
            throw new InvalidDataException("Incomplete NinjaSlayer component pool in run save.");
        ExternalComponentCharacterApi.InstallDefinitions(NinjaComponentSources.ProfileId, state.Cards.Select((payload, slot) =>
        {
            GeneratedCard generated = CardTinkeringApi.DeserializeCard(payload);
            var art = state.Portraits[slot];
            return new ChaosCardDefinition(slot, generated, art.Path, "blunt", generated.Type == GeneratedCardType.Attack ? "Attack" : "Cast",
                "res://images/powers/strength.png", "res://images/powers/big/strength.png",
                generated.Operations.Select(OperationRuntimeSpecCompiler.RequireStructured).ToArray(),
                generated.Upgrade?.Effects.Select(effect => effect.ValueSlotId).ToArray() ?? [],
                art.SourceId, art.VariantId, art.VariantPath);
        }));
        ResetCards.Invoke(null, [Bridge.CardTypes]);
    }

    private static IEnumerable<CardModel> Slots(int start, int count) => Bridge.CardTypes.Skip(start).Take(count)
        .Select(type => ModelDb.GetById<CardModel>(ModelDb.GetId(type)));

    // Ritsu 0.6.2 imports RunSavedData after Player.FromSerializable. Read this one slot through
    // its own decoder before cards are constructed; the normal Ritsu import still owns the run data.
    private static void RestoreBeforeCards(SerializableRun save)
    {
        Type runtime = typeof(RitsuLibFramework).Assembly.GetType("STS2RitsuLib.RunData.RunSavedDataRuntime", true)!;
        object?[] documentArgs = [save, null];
        Current = null;
        if (!(bool)AccessTools.Method(runtime, "TryGetDocument").Invoke(null, documentArgs)!) return;
        object document = documentArgs[1]!;
        object?[] entryArgs = [NinjaSlayerIds.ModId, SaveKey, null];
        if (!(bool)AccessTools.Method(document.GetType(), "TryGetRaw").Invoke(document, entryArgs)!) return;
        object slot = AccessTools.Field(Data.GetType(), "_slot").GetValue(Data)!;
        object?[] dataArgs = [entryArgs[2], null];
        if (!(bool)AccessTools.Method(slot.GetType(), "TryReadData").Invoke(slot, dataArgs)!)
            throw new InvalidDataException("Cannot decode the saved NinjaSlayer component pool.");
        Current = (NinjaAnthonyState)dataArgs[1]!;
        if (Active) Install(Current);
    }

    private sealed class SingleplayerStart : IPatchMethod
    {
        public static string PatchId => "ninjaslayer_anthony_singleplayer";
        public static string Description => "Prepare the optional component pool before native starting inventory.";
        public static bool IsCritical => true;
        public static ModPatchTarget[] GetTargets() => [new(typeof(NGame), nameof(NGame.StartNewSingleplayerRun))];
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(CharacterModel character, string seed, bool __runOriginal)
        {
            if (!__runOriginal) return;
            Current = null;
            if (character is INinjaSlayerCharacter) Generate(seed, ComponentRunSettingsApi.Local);
        }
    }

    private sealed class MultiplayerStart : IPatchMethod
    {
        public static string PatchId => "ninjaslayer_anthony_multiplayer";
        public static string Description => "Use Anthony's authoritative lobby settings and native seed.";
        public static bool IsCritical => true;
        public static ModPatchTarget[] GetTargets() => [new(typeof(NGame), nameof(NGame.StartNewMultiplayerRun))];
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(StartRunLobby lobby, IReadOnlyList<ModifierModel> modifiers, string seed, bool __runOriginal)
        {
            if (!__runOriginal) return;
            Current = null;
            if (!lobby.Players.Any(p => p.character is INinjaSlayerCharacter)) return;
            if (!ComponentRunSettingsApi.TryResolveMultiplayer(modifiers, out var settings))
                throw new InvalidOperationException("Anthony did not provide host component settings for the multiplayer run.");
            Generate(seed, settings);
        }
    }

    private sealed class RestorePool : IPatchMethod
    {
        public static string PatchId => "ninjaslayer_anthony_restore";
        public static string Description => "Restore external definitions before native card deserialization.";
        public static bool IsCritical => true;
        public static ModPatchTarget[] GetTargets() => [new(typeof(RunState), nameof(RunState.FromSerializable), [typeof(SerializableRun)])];
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(SerializableRun save) => RestoreBeforeCards(save);
    }

    private sealed class SavePool : IPatchMethod
    {
        public static string PatchId => "ninjaslayer_anthony_save";
        public static string Description => "Capture edited component definitions before Ritsu exports the native save.";
        public static bool IsCritical => true;
        public static ModPatchTarget[] GetTargets() => [new(typeof(RunManager), nameof(RunManager.ToSave),
            [typeof(MegaCrit.Sts2.Core.Rooms.AbstractRoom)])];
        public static void Prefix(RunManager __instance) => CapturePool(__instance.DebugOnlyGetState()!);
    }

    private sealed class StartingDeck : IPatchMethod
    {
        public static string PatchId => "ninjaslayer_anthony_starting_deck";
        public static string Description => "Replace NinjaSlayer's native starting-deck query when enabled.";
        public static bool IsCritical => true;
        public static ModPatchTarget[] GetTargets() => [new(typeof(NinjaSlayerCharacter), "get_StartingDeck")];
        public static void Postfix(CharacterModel __instance, ref IEnumerable<CardModel> __result)
        {
            if (__instance is INinjaSlayerCharacter && Active && Current!.Settings!.ReplaceStartingCards)
                __result = Slots(0, 10);
        }
    }

    private sealed class RewardPool : IPatchMethod
    {
        public static string PatchId => "ninjaslayer_anthony_reward_pool";
        public static string Description => "Use the saved component pool and preserve original cards only when requested.";
        public static bool IsCritical => true;
        public static ModPatchTarget[] GetTargets() => [new(typeof(NinjaSlayerCardPool), "get_AllCards")];
        public static void Postfix(ref IEnumerable<CardModel> __result)
        {
            if (Active) __result = Current!.Settings!.PreserveOriginalCards ? __result.Concat(Slots(10, 80)) : Slots(10, 80);
        }
    }

}

public sealed class NinjaAnthonyState
{
    public ComponentRunSettings? Settings { get; set; }
    public List<string> Cards { get; set; } = [];
    public List<NinjaAnthonyPortrait> Portraits { get; set; } = [];
}

public sealed record NinjaAnthonyPortrait(string Path, string SourceId, string? VariantId, string? VariantPath);
