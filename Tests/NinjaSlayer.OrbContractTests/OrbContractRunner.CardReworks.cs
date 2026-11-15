using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Cards;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;
using STS2RitsuLib;
using STS2RitsuLib.Patching.Core;
#if NINJASLAYER_CHANNEL_STABLE
using AimPower = NinjaSlayer.Powers.BladeSweepPower;
#endif

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyTileBreakingPunch()
    {
        Type[] buffs = [typeof(StrengthPower), typeof(DexterityPower), typeof(VigorPower), typeof(KaratePower),
            typeof(FocusPower), typeof(ArtifactPower), typeof(BufferPower), typeof(IntangiblePower),
            typeof(ThornsPower), typeof(PlatingPower), typeof(RegenPower), typeof(EvasionPower),
            typeof(NarakuLifePower), typeof(StretchPower), typeof(SoarPower), typeof(AimPower)];
        foreach (bool upgraded in new[] { false, true })
        {
            using (var combat = new OrbCombat())
            {
                var untouched = AddCard<DefendIronclad>(combat, PileType.Draw);
                var card = AddCard<TileBreakingPunch>(combat, upgraded: upgraded);
                await CardCmd.AutoPlay(Choice, card, combat.Enemy);
                Require(combat.Enemy.CurrentHp == 1000 - (upgraded ? 38 : 30)
                    && card.Pile?.Type == PileType.Discard && untouched.Pile?.Type == PileType.Draw
                    && !combat.Player.Creature.Powers.Any(power => buffs.Contains(power.GetType())),
                    "Tile-Breaking Punch must deal 30/38 damage, discard normally, and not create absent buffs or exhaust the deck.");
            }
            foreach (Type type in buffs)
            {
                using var combat = new OrbCombat();
                var owner = combat.Player.Creature;
                var canonical = ModelDb.GetById<PowerModel>(ModelDb.GetId(type));
                await PowerCmd.Apply(Choice, canonical.ToMutable(), owner, 5, owner, null);
                await CardCmd.AutoPlay(Choice, AddCard<TileBreakingPunch>(combat, upgraded: upgraded), combat.Enemy);
                int expected = type == typeof(VigorPower) ? 0 : 5 + (upgraded ? 3 : 2) - (type == typeof(KaratePower) ? 1 : 0);
                Require((owner.GetPower(canonical.Id)?.Amount ?? 0) == expected,
                    $"Tile-Breaking Punch must increase existing {type.Name} after resolving the attack; consumed effects stay consumed.");
            }
            using (var combat = new OrbCombat())
            {
                var owner = combat.Player.Creature;
                await PowerCmd.Apply<StrengthPower>(Choice, owner, -2, combat.Enemy, null);
                await PowerCmd.Apply<WeakPower>(Choice, owner, 2, combat.Enemy, null);
                await PowerCmd.Apply<DexterityPower>(Choice, combat.Enemy, 3, combat.Enemy, null);
                await PowerCmd.Apply<KaratePower>(Choice, owner, 1, owner, null);
                await PowerCmd.Apply<ZanshinPower>(Choice, owner, 1, owner, null);
                var undrawn = AddCard<DefendIronclad>(combat, PileType.Draw);
                await CardCmd.AutoPlay(Choice, AddCard<TileBreakingPunch>(combat, upgraded: upgraded), combat.Enemy);
                Require(owner.GetPowerAmount<StrengthPower>() == -2 && owner.GetPowerAmount<WeakPower>() == 2
                    && owner.GetPowerAmount<ZanshinPower>() == 1 && owner.GetPowerAmount<KaratePower>() == 0
                    && combat.Enemy.GetPowerAmount<DexterityPower>() == 3 && undrawn.Pile?.Type == PileType.Draw,
                    "Buff amplification must not change negative stats, debuffs, ability powers, other creatures or restore spent Karate.");
            }
            using (var combat = new OrbCombat())
            {
                var owner = combat.Player.Creature;
                await PowerCmd.Apply<DexterityPower>(Choice, owner, 2, owner, null);
                await PowerCmd.Apply<FocusPower>(Choice, owner, 3, owner, null);
                await PowerCmd.Apply<ZanshinPower>(Choice, owner, 1, owner, null);
                var undrawn = AddCard<DefendIronclad>(combat, PileType.Draw);
                await CardCmd.AutoPlay(Choice, AddCard<TileBreakingPunch>(combat, upgraded: upgraded), combat.Enemy);
                Require(owner.GetPowerAmount<DexterityPower>() == (upgraded ? 5 : 4)
                    && owner.GetPowerAmount<FocusPower>() == (upgraded ? 6 : 5)
                    && undrawn.Pile?.Type == PileType.Draw,
                    "All existing buffs must increase without retriggering Zanshin for extra stacks.");
            }
        }
        GD.Print("PASS Tile-Breaking Punch: 30/38 damage, all 16 current buffs +2/3, consumed effects stay gone, exclusions and Zanshin isolation.");
    }

    private static TaskCompletionSource? _hammerReplayGate;
    private static (float Fast, float Normal) _hammerReplayWait;
    private static bool HoldHammerReplayWait(float fastSeconds, float standardSeconds, ref Task __result)
    {
        if (_hammerReplayGate is null)
            return true;
        _hammerReplayWait = (fastSeconds, standardSeconds);
        __result = _hammerReplayGate.Task;
        return false;
    }

    private async Task VerifyHammerReplayPacing()
    {
        var product = typeof(ShurikenOrb).Assembly;
        Type context = product.GetType("NinjaSlayer.Code.Lifecycle.RapidCardPresentationContext", true)!;
        var patcher = RitsuLibFramework.CreatePatcher("NinjaSlayer.OrbContracts", "HammerReplay");
        typeof(ModPatcherExtensions).GetMethod("RegisterPatch")!.MakeGenericMethod(
            product.GetType("NinjaSlayer.Code.Patches.RapidMultiCardPlayPatch", true)!).Invoke(null, [patcher]);
        Require(patcher.PatchAll(), "Native replay presentation patch failed to install.");
        var harmony = new Harmony("NinjaSlayer.OrbContracts.HammerReplayWait");
        harmony.Patch(AccessTools.Method(typeof(Cmd), nameof(Cmd.CustomScaledWait)),
            prefix: new HarmonyMethod(GetType(), nameof(HoldHammerReplayWait)));
        harmony.Patch(AccessTools.Method(typeof(NCard), "UpdateVisuals"),
            prefix: new HarmonyMethod(GetType(), nameof(FeedbackSkipPortrait)));
        try
        {
            foreach (bool ninja in new[] { false, true })
            foreach (bool hammer in new[] { false, true })
            {
                using var combat = new OrbCombat(ninjaSlayer: ninja);
                var parent = hammer ? (CardModel)AddCard<NavyHammer>(combat) : AddCard<DefendIronclad>(combat);
                var attack = combat.Card();
                var node = new FeedbackCard();
                AccessTools.Field(typeof(NCard), "_model").SetValue(node, attack);
                AddChild(node);
                object outer = AccessTools.Method(context, "Begin").Invoke(null, [parent])!;
                object inner = AccessTools.Method(context, "Begin").Invoke(null, [attack])!;
                _hammerReplayWait = default;
                _hammerReplayGate = new TaskCompletionSource();
                try
                {
                    Task replay = node.AnimMultiCardPlay();
                    Require(_hammerReplayWait == (.4f, .5f), "Replay must use the host's 0.4/0.5-second gate.");
                    Require(replay.IsCompleted == (ninja && !hammer),
                        "Hammer replay must await the native animation; ordinary Ninja Slayer rapid plays retain their pacing.");
                    _hammerReplayGate.SetResult();
                    await replay;
                }
                finally
                {
                    _hammerReplayGate.TrySetResult();
                    _hammerReplayGate = null;
                    AccessTools.Method(inner.GetType(), "RestoreCallerContext").Invoke(inner, null);
                    AccessTools.Method(outer.GetType(), "RestoreCallerContext").Invoke(outer, null);
                    node.PlayPileTween?.Kill();
                    node.Free();
                }
            }
        }
        finally
        {
            _hammerReplayGate = null;
            harmony.UnpatchAll(harmony.Id);
            patcher.UnpatchAll();
        }
        GD.Print("PASS Navy Hammer native replay gate: host 0.4/0.5s animation awaited, ordinary rapid plays and other characters preserved.");
    }
}
