using System.Reflection;
using System.Security.Cryptography;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyMinionDamage(Assembly minion)
    {
        GD.Print($"MinionLib oracle {minion.Location}; SHA256 {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(minion.Location)))}");
        var harmony = new Harmony("MinionLib");
        Type suppression = minion.GetType("MinionLib.Powers.Patches.MinionGuardianOwnerDamageSuppressPatch", true)!;
        Type overflow = minion.GetType("MinionLib.Powers.Patches.MinionGuardianOverkillPatch", true)!;
        object scope = AccessTools.Field(overflow, "SuppressedOwner").GetValue(null)!;
        PropertyInfo suppressed = scope.GetType().GetProperty("Value")!;
        Type protection = typeof(NarakuLifeDamagePatch).Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherProtectionService", true)!;
        MethodInfo canProtect = AccessTools.Method(protection, "CanProtectLethalDamage");
        MethodInfo losingHp = AccessTools.Method(typeof(Creature), nameof(Creature.LoseHpInternal));
        MethodInfo predict = AccessTools.Method(protection.Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherAttackCommandAdapter", true)!,
            "PredictReverseVictim", [typeof(AttackCommand), typeof(IEnumerable<Creature>), typeof(decimal), typeof(int)]);
        try
        {
            if (System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_MINION_FIRST") != "1")
                harmony.CreateClassProcessor(suppression).Patch();
            object?[] reason = [null];
            Require((bool)canProtect.Invoke(null, reason)!, "Official MinionLib suppression aborted initialization: " + reason[0]);
            foreach (bool blocked in new[] { true, false })
            {
                using var combat = new OrbCombat(true);
                Creature owner = combat.Player.Creature;
                owner.SetCurrentHpInternal(10);
                await PowerCmd.Apply<NarakuLifePower>(Choice, owner, 3, owner, null);
                var attack = DamageCmd.Attack(10).FromMonster(combat.Enemy.Monster!);
                Require(predict.Invoke(null, [attack, new[] { owner }, 10m, 1]) == null,
                    "Reverse prediction ignored Naraku HP before any guardian intervention.");
                suppressed.SetValue(scope, blocked ? owner : null);
                DamageResult result = owner.LoseHpInternal(5, ValueProp.Move);
                suppressed.SetValue(scope, null);
                Require(owner.CurrentHp == (blocked ? 10 : 8), "Guardian suppression changed real HP loss.");
                Require(owner.GetPowerAmount<NarakuLifePower>() == (blocked ? 3 : 0), "Temporary suppressed loss consumed Naraku life.");
                Require((int)AccessTools.Method(typeof(NarakuLifeDamagePatch), "AbsorbedBy").Invoke(null, [result])! == (blocked ? 0 : 3),
                    "Guardian suppression produced the wrong absorption receipt.");
                Require(!result.WasTargetKilled, "Guardian suppression became a confirmed death.");
            }
            // Install the real overflow implementation, including its original damage callback.
            harmony.CreateClassProcessor(overflow).Patch();
            Type guardian = minion.GetType("MinionLib.Powers.MinionGuardianPower", true)!;
            foreach (var (petHp, damage, naraku, expectedHp, expectedNaraku) in new[]
            {
                (10, 5, 3, 10, 3), (2, 6, 3, 10, 1), (2, 10, 3, 7, 0), (2, 30, 3, 0, 0)
            })
            {
                using var combat = new OrbCombat(true);
                var owner = combat.Player.Creature; owner.SetCurrentHpInternal(10);
                await PowerCmd.Apply<NarakuLifePower>(Choice, owner, naraku, owner, null);
                for (int i = 0; i < 2; i++)
                {
                    var pet = await PlayerCmd.AddPet<DampCultist>(combat.Player);
                    pet.SetMaxHpInternal(petHp); pet.SetCurrentHpInternal(petHp);
                    var power = (PowerModel)ModelDb.GetById<PowerModel>(ModelDb.GetId(guardian)).ToMutable();
                    await PowerCmd.Apply(Choice, power, pet, 1, owner, null);
                }
                var redirected = DamageCmd.Attack(500).FromMonster(combat.Enemy.Monster!);
                Require(predict.Invoke(null, [redirected, new[] { owner }, 500m, 1]) == null,
                    "Redirected damage prematurely reserved a reverse finisher.");
                var results = (await CreatureCmd.Damage(Choice, owner, damage, ValueProp.Move, combat.Enemy)).ToList();
                Require(owner.CurrentHp == expectedHp && owner.GetPowerAmount<NarakuLifePower>() == expectedNaraku,
                    $"Guardian overflow mismatch: damage {damage}, owner {owner.CurrentHp}, Naraku {owner.GetPowerAmount<NarakuLifePower>()}.");
                Require(results.Count(result => result.Receiver == owner && result.WasTargetKilled) == (expectedHp == 0 ? 1 : 0),
                    "Only the final actual owner loss may report death.");
            }
        }
        finally { suppressed.SetValue(scope, null); harmony.UnpatchAll(harmony.Id); }
        GD.Print("PASS official MinionLib HP suppression, native guardian overflow, Naraku receipts and final death.");
    }
}
