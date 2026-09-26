using System.Reflection;
using System.Security.Cryptography;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Powers;
using NinjaSlayer.Relics;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyLoadoutDamage(Assembly loadout)
    {
        GD.Print($"Loadout oracle {loadout.Location}; SHA256 {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(loadout.Location)))}");
        Type state = loadout.GetType("Loadout.Services.TildeKey.TildeKeyStateService", true)!;
        Type hook = loadout.GetType("Loadout.Patches.TildeKey.TildeKeyGodmodeLoseHpPatch", true)!;
        MethodInfo setToggle = AccessTools.Method(state, "SetSavedToggle");
        AccessTools.Method(state, "EnsureLoaded").Invoke(null, null);
        var harmony = new Harmony("Loadout");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(Creature), nameof(Creature.LoseHpInternal)),
                prefix: new HarmonyMethod(AccessTools.Method(hook, "Prefix")));
            Type protection = typeof(NarakuLifeDamagePatch).Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherProtectionService", true)!;
            object?[] args = [null];
            Require((bool)AccessTools.Method(protection, "CanProtectLethalDamage").Invoke(null, args)!,
                "Installing Loadout disabled finishers with godmode off: " + args[0]);
            foreach (bool godmode in new[] { false, true })
            {
                using var combat = new OrbCombat();
                Creature player = combat.Player.Creature;
                player.SetCurrentHpInternal(10);
                setToggle.Invoke(null, [combat.Player.NetId, "godmode", godmode]);
                await PowerCmd.Apply<NarakuLifePower>(Choice, player, 3, player, null);
                DamageResult result = (await CreatureCmd.Damage(Choice, player, 5, ValueProp.Unpowered, combat.Enemy)).Single();
                int absorbed = (int)AccessTools.Method(typeof(NarakuLifeDamagePatch), "AbsorbedBy").Invoke(null, [result])!;
                Require(player.CurrentHp == (godmode ? 10 : 8), "Loadout godmode did not retain its native HP semantics.");
                Require(player.GetPowerAmount<NarakuLifePower>() == (godmode ? 3 : 0),
                    "Loadout godmode consumed Naraku life before blocking HP loss.");
                Require(absorbed == (godmode ? 0 : 3) && !result.WasTargetKilled,
                    "Loadout godmode produced a false life-loss receipt or death.");
            }
        }
        finally
        {
            setToggle.Invoke(null, [1UL, "godmode", false]);
            harmony.UnpatchAll(harmony.Id);
        }
        GD.Print("PASS actual Loadout prefix: installation permits finishers; godmode blocks both real and Naraku HP loss without false receipts.");
    }

    private static async Task VerifyHextechDamage(Assembly hextech)
    {
        GD.Print($"Hextech oracle {hextech.Location}; SHA256 {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(hextech.Location)))}");
        Type runeType = hextech.GetType("HextechRunes.NearDeathFeastRune", true)!;
        MethodInfo losingHp = AccessTools.Method(typeof(Creature), nameof(Creature.LoseHpInternal));
        Type hook = hextech.GetType("HextechRunes.HextechCombatHooks+NearDeathFeastLoseHpPatch", true)!;
        var harmony = new Harmony("NinjaSlayer.OrbContracts.Hextech");
        var unknown = new Harmony("NinjaSlayer.OrbContracts.UnknownDamageOwner");
        Type protection = typeof(NarakuLifeDamagePatch).Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherProtectionService", true)!;
        MethodInfo canProtect = AccessTools.Method(protection, "CanProtectLethalDamage");
        try
        {
            harmony.Patch(losingHp, prefix: new HarmonyMethod(AccessTools.Method(hook, "Prefix")));
            object?[] args = [null];
            Require((bool)canProtect.Invoke(null, args)!, "The verified Hextech prefix disabled the finisher contract: " + args[0]);
            unknown.Patch(losingHp, prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(UnknownHpPrefix)));
            Require(!(bool)canProtect.Invoke(null, args)!, "An unknown skipping prefix bypassed the finisher safety boundary.");
            unknown.UnpatchAll(unknown.Id);
            harmony.Patch(losingHp,
                prefix: new HarmonyMethod(typeof(NinjaSlayerFinisherLethalDamagePatch), nameof(NinjaSlayerFinisherLethalDamagePatch.Prefix)),
                postfix: new HarmonyMethod(typeof(NinjaSlayerFinisherLethalDamagePatch), nameof(NinjaSlayerFinisherLethalDamagePatch.Postfix)),
                finalizer: new HarmonyMethod(typeof(NinjaSlayerFinisherLethalDamagePatch), nameof(NinjaSlayerFinisherLethalDamagePatch.Finalizer)));

            foreach (var (stock, damage, expectedHp, expectedDebt, receipt) in new[]
            {
                (10, 5, 3, 0, 5), // Full Naraku absorption: the original method still runs.
                (2, 7, 1, 2, 2),  // Overflow: Hextech supplies the final result after absorption.
                (0, 5, 1, 2, 0),  // Native Hextech behavior, with no Naraku power.
            })
            {
                using var combat = new OrbCombat();
                Creature player = combat.Player.Creature;
                player.SetCurrentHpInternal(3);
                var rune = (RelicModel)ModelDb.GetById<RelicModel>(ModelDb.GetId(runeType)).ToMutable();
                combat.Player.AddRelicInternal(rune);
                var fragment = (BeppinFragmentRelic)ModelDb.Relic<BeppinFragmentRelic>().ToMutable();
                combat.Player.AddRelicInternal(fragment);
                int flashes = 0;
                fragment.Flashed += (_, _) => flashes++;
                if (stock > 0)
                    await PowerCmd.Apply<NarakuLifePower>(Choice, player, stock, player, null);
                DamageResult result = (await CreatureCmd.Damage(Choice, player, damage, ValueProp.Unpowered, combat.Enemy)).Single();
                int actualReceipt = (int)AccessTools.Method(typeof(NarakuLifeDamagePatch), "AbsorbedBy").Invoke(null, [result])!;
                Require(actualReceipt == receipt, $"Naraku receipt mismatch with Hextech: expected {receipt}, actual {actualReceipt}.");
                Require(player.CurrentHp == expectedHp && !result.WasTargetKilled,
                    "Hextech's surviving near-death result became confirmed death.");
                Require((int)AccessTools.Property(runeType, "SavedNearDeathDebt").GetValue(rune)! == expectedDebt,
                    "Naraku and real HP loss were charged twice to the Hextech debt.");
                Require(flashes == 1 && player.GetPowerAmount<KaratePower>() == 7,
                    "One damage result must trigger the life-loss relic only once.");
            }
            using (var combat = new OrbCombat())
            {
                Type modifierType = hextech.GetType("HextechRunes.HextechMayhemModifier", true)!;
                var modifier = (ModifierModel)ModelDb.GetById<ModifierModel>(ModelDb.GetId(modifierType)).ToMutable();
                var run = MegaCrit.Sts2.Core.Runs.RunState.CreateForTest([combat.Player], modifiers: [modifier], seed: "HEXTECH_ENEMY");
                AccessTools.Field(typeof(MegaCrit.Sts2.Core.Combat.CombatState), "<RunState>k__BackingField").SetValue(combat.State, run);
                modifier.OnRunCreated(run);
                object nearDeath = Enum.Parse(hextech.GetType("HextechRunes.MonsterHexKind", true)!, "NearDeathFeast");
                object rarity = Enum.Parse(hextech.GetType("HextechRunes.HextechRarityTier", true)!, "Silver");
                AccessTools.Method(modifierType, "DebugSetOnlyMonsterHex").Invoke(modifier, [0, nearDeath, rarity]);
                Require((bool)AccessTools.Method(modifierType, "HasActiveMonsterHex").Invoke(modifier, [nearDeath])!,
                    "Enemy near-death fixture was not active.");
                combat.Enemy.SetCurrentHpInternal(3);
                DamageResult survived = combat.Enemy.LoseHpInternal(5, ValueProp.Unpowered);
                Require(combat.Enemy.CurrentHp == 1 && !survived.WasTargetKilled,
                    "Enemy near-death survival became confirmed death.");
                DamageResult died = combat.Enemy.LoseHpInternal(60, ValueProp.Unpowered);
                Require(combat.Enemy.CurrentHp == 0 && died.WasTargetKilled,
                    "Enemy near-death threshold no longer allowed native death.");
            }
        }
        finally
        {
            unknown.UnpatchAll(unknown.Id);
            harmony.UnpatchAll(harmony.Id);
        }
        GD.Print("PASS actual Hextech prefix: full Naraku absorption, overflow receipt, player/enemy near-death survival and enemy death threshold, one relic trigger and finisher patch eligibility.");
    }

    private static bool UnknownHpPrefix() => true;
}
