using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Ancients;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Badges;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Runs;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

#pragma warning disable CA1707

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerCanonicalCharacterIdPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_redesign_canonical_character_id";
    public static string Description => "Share Ninja Slayer progression and ancient dialogue across rules modes.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(ProgressState), nameof(ProgressState.GetStatsForCharacter), [typeof(ModelId)]),
        new(typeof(ProgressState), nameof(ProgressState.GetOrCreateCharacterStats), [typeof(ModelId)]),
        new(typeof(AncientStats), nameof(AncientStats.GetVisitsAs), [typeof(ModelId)]),
        new(
            typeof(AncientDialogueSet),
            nameof(AncientDialogueSet.GetValidDialogues),
            [typeof(ModelId), typeof(int), typeof(int), typeof(bool)])
    ];

    public static void Prefix(ref ModelId characterId) =>
        characterId = NinjaSlayerCharacterIdentity.Canonicalize(characterId);
}

public sealed class NinjaSlayerRunProgressIdentityPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_redesign_run_progress_identity";
    public static string Description => "Record redesign run outcomes under the visible Ninja Slayer identity.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(ProgressSaveManager),
            nameof(ProgressSaveManager.UpdateWithRunData),
            [typeof(SerializableRun), typeof(bool)])
    ];

    public static void Prefix(
        SerializableRun serializableRun,
        out (SerializablePlayer? Player, ModelId? CharacterId) __state)
    {
        SerializablePlayer? player = serializableRun.Players.Count == 1
            ? serializableRun.Players[0]
            : serializableRun.Players.FirstOrDefault(candidate =>
                candidate.NetId == PlatformUtil.GetLocalPlayerId(serializableRun.PlatformType));
        __state = (player, player?.CharacterId);
        if (player?.CharacterId is { } characterId)
        {
            player.CharacterId = NinjaSlayerCharacterIdentity.Canonicalize(characterId);
        }
    }

    public static void Postfix(
        (SerializablePlayer? Player, ModelId? CharacterId) __state) =>
        Restore(__state);

    public static Exception? Finalizer(
        Exception? __exception,
        (SerializablePlayer? Player, ModelId? CharacterId) __state)
    {
        Restore(__state);
        return __exception;
    }

    private static void Restore(
        (SerializablePlayer? Player, ModelId? CharacterId) state)
    {
        if (state.Player != null)
        {
            state.Player.CharacterId = state.CharacterId;
        }
    }
}

public sealed class NinjaSlayerGameOverBadgeIdentityPatch : IPatchMethod
{
    private static readonly AccessTools.FieldRef<NGameOverScreen, Player> LocalPlayer =
        AccessTools.FieldRefAccess<NGameOverScreen, Player>("_localPlayer");
    private static readonly AccessTools.FieldRef<ProgressState, Dictionary<ModelId, CharacterStats>> CharacterStats =
        AccessTools.FieldRefAccess<ProgressState, Dictionary<ModelId, CharacterStats>>("_characterStats");

    public static string PatchId => "ninjaslayer_redesign_game_over_badge_identity";
    public static string Description => "Write Redesign run badges to the visible Ninja Slayer progress entry.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NGameOverScreen), "SaveBadgesToProgress", [typeof(List<Badge>)])
    ];

    public static void Prefix(NGameOverScreen __instance, out bool __state)
    {
        if (LocalPlayer(__instance).Character is not NinjaSlayerRedesignCharacter)
        {
            __state = false;
            return;
        }

        Dictionary<ModelId, CharacterStats> stats = CharacterStats(SaveManager.Instance.Progress);
        ModelId redesignId = ModelDb.Character<NinjaSlayerRedesignCharacter>().Id;
        __state = !stats.ContainsKey(redesignId);
        if (__state)
        {
            stats.Add(redesignId, stats[ModelDb.Character<NinjaSlayerCharacter>().Id]);
        }
    }

    public static Exception? Finalizer(Exception? __exception, bool __state)
    {
        if (__state)
        {
            CharacterStats(SaveManager.Instance.Progress)
                .Remove(ModelDb.Character<NinjaSlayerRedesignCharacter>().Id);
        }

        return __exception;
    }
}

public sealed class NinjaSlayerCombatProgressIdentityPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_redesign_combat_progress_identity";
    public static string Description => "Merge redesign combat wins into the visible Ninja Slayer identity.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(ProgressSaveManager),
            nameof(ProgressSaveManager.UpdateAfterCombatWon),
            [typeof(Player), typeof(CombatRoom)])
    ];

    public static void Postfix(
        ProgressSaveManager __instance,
        Player localPlayer,
        CombatRoom room)
    {
        if (localPlayer.Character is not NinjaSlayerRedesignCharacter)
        {
            return;
        }

        ProgressState progress = __instance.Progress;
        if (progress.EncounterStats.TryGetValue(room.Encounter.Id, out EncounterStats? encounter))
        {
            Merge(encounter.FightStats);
        }

        foreach (ModelId enemyId in room.Encounter.SpawnedEnemies.Select(enemy => enemy.Id).Distinct())
        {
            if (progress.EnemyStats.TryGetValue(enemyId, out EnemyStats? enemy))
            {
                Merge(enemy.FightStats);
            }
        }
    }

    private static void Merge(List<FightStats> stats)
    {
        ModelId redesignId = ModelDb.Character<NinjaSlayerRedesignCharacter>().Id;
        FightStats? redesign = stats.FirstOrDefault(fight => fight.Character == redesignId);
        if (redesign == null)
        {
            return;
        }

        ModelId canonicalId = ModelDb.Character<NinjaSlayerCharacter>().Id;
        FightStats? canonical = stats.FirstOrDefault(fight => fight.Character == canonicalId);
        if (canonical == null)
        {
            stats.Add(new FightStats
            {
                Character = canonicalId,
                Wins = redesign.Wins,
                Losses = redesign.Losses
            });
        }
        else
        {
            canonical.Wins += redesign.Wins;
            canonical.Losses += redesign.Losses;
        }

        stats.Remove(redesign);
    }
}
