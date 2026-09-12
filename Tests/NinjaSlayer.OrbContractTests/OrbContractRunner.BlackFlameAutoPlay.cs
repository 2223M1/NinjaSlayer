using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;
using MegaCrit.Sts2.Core.Models.Cards;
using STS2RitsuLib;
using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static void PatchAutoplayBeforeFramework()
    {
        // JIT-compiling this caller before wrapper hooks are installed can inline OnPlayWrapper.
        // An identity transpiler reproduces the loading-order effect without another mod dependency.
        var state = AccessTools.Method(typeof(CardCmd), nameof(CardCmd.AutoPlay))
            .GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType;
        new Harmony("NinjaSlayer.OrbContracts.EarlyAutoplay").Patch(
            AccessTools.Method(state, "MoveNext"),
            transpiler: new HarmonyMethod(typeof(OrbContractRunner), nameof(ObserveEarlyAutoplay)));
    }

    private static IEnumerable<CodeInstruction> ObserveEarlyAutoplay(IEnumerable<CodeInstruction> instructions) => instructions;
    private static void DelayPlayCount(ref Task<int> __result) => __result = YieldPlayCount(__result);
    private static async Task<int> YieldPlayCount(Task<int> original)
    {
        await Task.Yield();
        return await original;
    }

    private static async Task VerifyBlackFlameAutoPlay()
    {
        var product = typeof(ShurikenOrb).Assembly;
        var asyncAutoplay = new Harmony("NinjaSlayer.OrbContracts.AsyncAutoplay");
        asyncAutoplay.Patch(
            AccessTools.Method(typeof(CardModel), "GeneratePlayCount"),
            postfix: new HarmonyMethod(typeof(OrbContractRunner), nameof(DelayPlayCount)));
        var patcher = RitsuLibFramework.CreatePatcher("NinjaSlayer.OrbContracts", "AutoplayPresentation");
        try
        {
            foreach (string name in new[] { "RapidCardResolutionScopePatch", "RapidPowerCardFlyPatch", "RapidMultiCardPlayPatch", "NinjaSlayerFinisherAfterCardPlayedPatch", "NinjaSlayerFinisherCardPlayCleanupPatch" })
                typeof(ModPatcherExtensions).GetMethod("RegisterPatch")!
                    .MakeGenericMethod(product.GetType("NinjaSlayer.Code.Patches." + name, true)!).Invoke(null, [patcher]);
            Require(patcher.PatchAll(), "Autoplay presentation patches failed to install.");
            var dynamicPatches = (DynamicPatchInfo[])AccessTools.Method(product.GetType("NinjaSlayer.Code.Patches.RapidCardResolutionStateMachinePatch", true), "CreateDynamicPatches").Invoke(null, null)!;
            Require(patcher.ApplyDynamicPatches(dynamicPatches, rollbackOnCriticalFailure: true), "Autoplay state-machine patches failed to install.");
            foreach (int flameCount in new[] { 1, 2, 3 })
            foreach (bool upgraded in new[] { false, true })
            foreach (bool beatDown in new[] { false, true })
            {
                using var combat = new OrbCombat(ninjaSlayer: true);
                var second = combat.AddEnemy();
                for (int i = 0; i < flameCount; i++) AddCard<BlackFlameRedesignV1>(combat);
                await PowerCmd.Apply<BurnBurnBurnPower>(Choice, combat.Player.Creature, 3, combat.Player.Creature, null);
                if (beatDown)
                {
                    for (int i = 0; i < (upgraded ? 4 : 3); i++) AddCard<TwinStrike>(combat, PileType.Discard);
                    await CardCmd.AutoPlay(Choice, AddCard<BeatDown>(combat, upgraded: upgraded), null);
                }
                else
                {
                    AddCard<TwinStrike>(combat, PileType.Draw);
                    await CardCmd.AutoPlay(Choice, AddCard<WasshoiRedesignV1>(combat, upgraded: upgraded), null);
                }
                int expectedPlays = beatDown ? (upgraded ? 4 : 3) : (upgraded ? 3 : 2);
                foreach (var enemy in new[] { combat.Enemy, second })
                {
                    var burns = CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>()
                        .Where(e => e.Receiver == enemy && e.CardSource is BlackFlameRedesignV1).ToArray();
                    Require(burns.Length == expectedPlays && burns.All(e => e.Result.TotalDamage == flameCount * 4 + 3),
                        $"Nested autoplay must complete with one merged, once-amplified burn per actual play: BeatDown={beatDown}, upgraded={upgraded}, flames={flameCount}.");
                }
            }
        }
        finally
        {
            patcher.UnpatchAll();
            asyncAutoplay.UnpatchAll(asyncAutoplay.Id);
        }
        GD.Print("PASS Black Flame nested autoplay: Naval Warhammer/BeatDown, upgrades, multi-hit, merged flames and amplification.");
    }
}
