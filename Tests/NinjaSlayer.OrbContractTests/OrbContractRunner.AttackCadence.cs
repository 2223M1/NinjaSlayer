using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static float _cadenceClock;
    private static float _cadenceScale = 1f;
    private static int _extraCadenceHit;

    private static async Task VerifyAttackCadence()
    {
        using var combat = new OrbCombat(ninjaSlayer: true);
        Assembly product = typeof(ShurikenOrb).Assembly;
        Type execution = product.GetType("NinjaSlayer.Code.Combat.NinjaSlayerAttackExecution", true)!;
        Type rapid = product.GetType("NinjaSlayer.Code.Lifecycle.RapidCardPresentationContext", true)!;
        Type pacing = product.GetType("NinjaSlayer.Code.Patches.CombatPresentationPacingPatch", true)!;
        var harmony = new Harmony("NinjaSlayer.OrbContracts.AttackCadence");
        foreach (string name in new[] { "NinjaSlayerAttackExecutionPatch", "NinjaSlayerAttackHitCountPatch" })
        {
            Type patch = product.GetType("NinjaSlayer.Code.Patches." + name, true)!;
            MethodBase target = name.EndsWith("HitCountPatch", StringComparison.Ordinal)
                ? AccessTools.Method(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "ModifyAttackHitCount")
                : AccessTools.Method(typeof(AttackCommand), "Execute");
            harmony.Patch(target,
                prefix: AccessTools.Method(patch, "Prefix") is { } prefix ? new HarmonyMethod(prefix) : null,
                postfix: new HarmonyMethod(AccessTools.Method(patch, "Postfix")));
        }
        var patches = (STS2RitsuLib.Patching.Models.DynamicPatchInfo[])AccessTools.Method(pacing, "CreateDynamicPatches").Invoke(null, null)!;
        foreach (var patch in patches)
            harmony.Patch(patch.OriginalMethod, transpiler: new HarmonyMethod(AccessTools.Method(pacing, "Transpiler")));
        harmony.Patch(AccessTools.Method(typeof(Cmd), nameof(Cmd.CustomScaledWait)),
            prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(RecordCadenceWait)));
        harmony.Patch(AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.TriggerAnim)),
            prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(RecordCadenceAnimation)));
        harmony.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "ModifyAttackHitCount"),
            postfix: new HarmonyMethod(typeof(OrbContractRunner), nameof(AddCadenceHit)) { priority = Priority.First });
        try
        {
            foreach (float scale in new[] { 1f, 0.5f, 0f })
                foreach (float threshold in new[] { 0.15f, 0.2f })
                    foreach (int mode in new[] { 0, 1, 2 })
                    {
                        _cadenceClock = 0f;
                        _cadenceScale = scale;
                        _extraCadenceHit = mode == 1 ? 1 : 0;
                        var card = combat.Card();
                        object lease = AccessTools.Method(rapid, "Begin").Invoke(null, [card])!;
                        var hits = new List<float>();
                        try
                        {
                            object? sequence = mode == 2 ? AccessTools.Method(execution, "EnterSequence").Invoke(null, [3]) : null;
                            try
                            {
                                for (int step = 0; step < (mode == 2 ? 3 : 1); step++)
                                {
                                    if (sequence != null) AccessTools.Method(sequence.GetType(), "SetHit").Invoke(sequence, [step]);
                                    var attack = DamageCmd.Attack(1)
#if NINJASLAYER_CHANNEL_STABLE
                        .FromCard(card)
#else
                                        .FromCard(card, null)
#endif
                                        .Targeting(combat.Enemy)
                                        .WithHitCount(mode == 2 ? 1 : 3).WithAttackerAnim("Attack", threshold)
                                        .BeforeDamage(() => { hits.Add(_cadenceClock); return Task.CompletedTask; });
                                    await attack.Execute(Choice);
                                    Require(attack.Results.Count() == (mode == 2 ? 1 : 3 + _extraCadenceHit), "Native multi-hit result count changed.");
                                }
                            }
                            finally { (sequence as IDisposable)?.Dispose(); }
                        }
                        finally { AccessTools.Method(lease.GetType(), "RestoreCallerContext").Invoke(lease, null); }
                        float[] expected = Enumerable.Range(0, 3 + _extraCadenceHit).Select(i => ((i + 1) * threshold + i * 0.2f) * scale).ToArray();
                        Require(hits.Count == expected.Length && hits.Zip(expected).All(pair => Math.Abs(pair.First - pair.Second) < 0.0001f),
                            $"Native multi-hit cadence collapsed: gate={threshold}, scale={scale}, hits={string.Join(',', hits)}");
                        Require(!(bool)AccessTools.Property(execution, "NeedsDamageRecovery").GetValue(null)!,
                            "Attack execution leaked its recovery context into the caller.");
                    }
            GD.Print("PASS native AttackCommand cadence: Attack/Slow, Normal/Fast/Instant, WithHitCount, hook-added hits, explicit sequences, damage/results and async context cleanup.");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    private static bool RecordCadenceWait(float __0, float __1, ref Task __result)
    {
        _cadenceClock += _cadenceScale == 0.5f ? __0 : __1 * _cadenceScale;
        __result = YieldCadence();
        return false;
    }

    private static bool RecordCadenceAnimation(float __2, ref Task __result)
    {
        _cadenceClock += __2 * _cadenceScale;
        __result = YieldCadence();
        return false;
    }

    private static async Task YieldCadence() => await Task.Yield();

    private static void AddCadenceHit(ref decimal __result) => __result += _extraCadenceHit;
}
