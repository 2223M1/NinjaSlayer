using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using NinjaSlayer.Orbs;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static float _cadenceClock;
    private static float _cadenceScale = 1f;
    private static int _extraCadenceHit;
    private static List<float>? _throwTimes;

    private static async Task VerifyAttackCadence()
    {
        using var combat = new OrbCombat(ninjaSlayer: true);
        Assembly product = typeof(ShurikenOrb).Assembly;
        Type execution = product.GetType("NinjaSlayer.Code.Combat.NinjaSlayerAttackExecution", true)!;
        Type rapid = product.GetType("NinjaSlayer.Code.Lifecycle.RapidCardPresentationContext", true)!;
        Type pacing = product.GetType("NinjaSlayer.Code.Patches.CombatPresentationPacingPatch", true)!;
        var harmony = new Harmony("NinjaSlayer.OrbContracts.AttackCadence");
        var throwAnimation = AccessTools.Method(product.GetType("NinjaSlayer.Cards.ShurikenCombat", true)!, "PlayStockThrowAnimation");
        var skipThrow = AccessTools.Method(typeof(OrbContractRunner), nameof(SkipThrowAnimation));
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
                                        .FromCard(card, null)
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
            _cadenceScale = 1f;
            _extraCadenceHit = 0;
            harmony.Unpatch(throwAnimation, skipThrow);
            foreach (int kind in new[] { 0, 1, 2 })
            {
                await AddStock(combat.Player, 3);
                _throwTimes = [];
                if (kind == 0)
                {
                    var power = await PowerCmd.Apply<HellTornadoPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
                    await power!.AfterAutoPrePlayPhaseEnteredEarly(Choice, combat.Player);
                }
                else
                {
                    var card = kind == 1 ? AddCard<Moonsault>(combat) : (MegaCrit.Sts2.Core.Models.CardModel)AddCard<SpiralJump>(combat);
                    object lease = AccessTools.Method(rapid, "Begin").Invoke(null, [card])!;
                    try { await CardCmd.AutoPlay(Choice, card, null); }
                    finally { AccessTools.Method(lease.GetType(), "RestoreCallerContext").Invoke(lease, null); }
                }
                Require(_throwTimes.Count == (kind == 1 ? 6 : 3)
                    && _throwTimes.Zip(_throwTimes.Skip(1)).All(pair => Math.Abs(pair.Second - pair.First - .15f) < .0001f),
                    $"Volley {kind} changed shot count or added recovery between releases: {string.Join(", ", _throwTimes)}");
            }
            GD.Print("PASS three-stock volley release intervals: Hell Tornado 3, Moonsault 6, Spiral Jump 3; 150ms each.");
        }
        finally
        {
            if (_throwTimes != null)
                new Harmony("NinjaSlayer.OrbContracts.Presentation").Patch(throwAnimation, prefix: new HarmonyMethod(skipThrow));
            _throwTimes = null;
            harmony.UnpatchAll(harmony.Id);
        }
    }

    private static bool RecordCadenceWait(float __0, float __1, ref Task __result)
    {
        if (__0 == .15f && __1 == .15f) _throwTimes?.Add(_cadenceClock);
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
