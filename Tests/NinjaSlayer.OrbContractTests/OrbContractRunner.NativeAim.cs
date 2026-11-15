using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Powers;
#if NINJASLAYER_CHANNEL_STABLE
using AimPower = NinjaSlayer.Powers.BladeSweepPower;
#endif

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyNativeAimVolleys()
    {
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var owner = combat.Player.Creature;
            var second = combat.AddEnemy();
            for (int i = 0; i < 4; i++) AddCard<DefendIronclad>(combat, PileType.Draw);
            await CardCmd.AutoPlay(Choice, AddCard<Zanshin>(combat), null);
            await CardCmd.AutoPlay(Choice, AddCard<BladeSweep>(combat, upgraded: upgraded), null);
            int aim = upgraded ? 6 : 4, stock = upgraded ? 3 : 2;
            Require(combat.Stock == stock && owner.GetPowerAmount<AimPower>() == aim
                && PileType.Hand.GetPile(combat.Player).Cards.Count == 1,
                "Blade Sweep must grant 2/3 stock and 4/6 Aim, triggering Zanshin once.");
            await EndGuardSide(combat.State, CombatSide.Player, owner);
            Require(owner.GetPowerAmount<AimPower>() == aim, "Unspent Aim must survive turn end.");
            using var selector = UseCardSelector(new SelectCards(options => options));
            await ScryCmd.Execute(Choice, combat.Player, 3);
            Require(combat.Stock == 0 && !owner.HasPower<AimPower>()
                && 2000 - combat.Enemy.CurrentHp - second.CurrentHp == stock * (6 + aim),
                "Every stock fired by one Scry must receive Aim exactly once, without the retired AoE effect.");
        }
        foreach (int stock in new[] { 0, 3 })
        foreach (int selected in new[] { 0, 3 })
        {
            using var combat = new OrbCombat();
            var owner = combat.Player.Creature;
            await PowerCmd.Apply<AimPower>(Choice, owner, 4, owner, null);
            await AddStock(combat.Player, stock);
            for (int i = 0; i < 3; i++) AddCard<DefendIronclad>(combat, PileType.Draw);
            using var selector = UseCardSelector(new SelectCards(options => options.Take(selected).ToArray()));
            await ScryCmd.Execute(Choice, combat.Player, 3);
            bool fired = stock > 0 && selected > 0;
            Require(combat.Enemy.CurrentHp == (fired ? 970 : 1000)
                && owner.GetPowerAmount<AimPower>() == (fired ? 0 : 4),
                "Scry must spend Aim only if stock actually fires, and boost all three selected-card shots.");
        }
        foreach (string trigger in new[] { "evoke", "moonsault", "shuffle" })
        {
            using var combat = new OrbCombat();
            var owner = combat.Player.Creature;
            await AddStock(combat.Player, 3);
            await PowerCmd.Apply<AimPower>(Choice, owner, 4, owner, null);
            Require(combat.Orb!.EvokeVal == 10, "Stock preview must include current Aim.");
            if (trigger == "evoke") await OrbCmd.EvokeNext(Choice, combat.Player);
            else if (trigger == "moonsault") await CardCmd.AutoPlay(Choice, AddCard<Moonsault>(combat), null);
            else
            {
                await PowerCmd.Apply<BladeCyclePower>(Choice, owner, 2, owner, null);
                await Hook.AfterShuffle(combat.State, Choice, combat.Player);
            }
            Require(combat.Enemy.CurrentHp == 1000 - (trigger == "moonsault" ? 60 : 30)
                && !owner.HasPower<AimPower>(), "A whole evoke, doubled consumed volley or shuffle must share one Aim snapshot.");
            int before = combat.Enemy.CurrentHp;
            await AddStock(combat.Player, 1);
            int nextStock = combat.Stock;
            await OrbCmd.EvokeNext(Choice, combat.Player);
            Require(before - combat.Enemy.CurrentHp == 6 * nextStock, "A later volley must not reuse spent Aim.");
        }
        using (var combat = new OrbCombat())
        {
            var owner = combat.Player.Creature;
            await AddStock(combat.Player, 3);
            await PowerCmd.Apply<AimPower>(Choice, owner, 4, owner, null);
            owner.GetPower<EvokeObserver>()!.AfterStockShot =
                () => PowerCmd.Apply<AimPower>(Choice, owner, 2, owner, null);
            await CardCmd.AutoPlay(Choice, AddCard<Moonsault>(combat), null);
            Require(combat.Enemy.CurrentHp == 940 && owner.GetPowerAmount<AimPower>() == 2,
                "Aim gained during a volley must not alter its snapshot or be consumed with the original Aim.");
            await AddStock(combat.Player, 1);
            await OrbCmd.EvokeNext(Choice, combat.Player);
            Require(combat.Enemy.CurrentHp == 932 && !owner.HasPower<AimPower>(),
                "The next volley must receive and consume Aim gained during the previous volley.");
        }
        using (var combat = new OrbCombat())
        {
            var owner = combat.Player.Creature;
            await AddStock(combat.Player, 2);
            await PowerCmd.Apply<AimPower>(Choice, owner, 4, owner, null);
            combat.Enemy.SetCurrentHpInternal(0);
            await OrbCmd.EvokeNext(Choice, combat.Player);
            Require(owner.GetPowerAmount<AimPower>() == 4, "No living target must not spend Aim.");
        }
        using (var combat = new OrbCombat())
        {
            var owner = combat.Player.Creature;
            var marked = combat.AddEnemy();
            await PowerCmd.Apply<AimPower>(Choice, owner, 4, owner, null);
            await PowerCmd.Apply<ShurikenAimPower>(Choice, marked, 3, owner, null);
            await AddStock(combat.Player, 3);
            marked.SetCurrentHpInternal(1);
            await OrbCmd.EvokeNext(Choice, combat.Player);
            Require(marked.IsDead && combat.Enemy.CurrentHp == 980 && !owner.HasPower<AimPower>(),
                "Aimed stock must retarget after a marked enemy dies without losing the remaining volley bonus.");
        }
        foreach (bool upgraded in new[] { false, true })
        foreach (bool generated in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var owner = combat.Player.Creature;
            await PowerCmd.Apply<FocusPower>(Choice, owner, 2, owner, null);
            await PowerCmd.Apply<AimPower>(Choice, owner, 4, owner, null);
            StrongShuriken strong;
            if (generated)
            {
                await PowerCmd.Apply<StarlessNightPower>(Choice, owner, 1, owner, null);
                await AddStock(combat.Player, 1);
                strong = PileType.Hand.GetPile(combat.Player).Cards.OfType<StrongShuriken>().Single();
                Require(strong.SnapshotDamage == 8, "Generated Strong Shuriken must not permanently snapshot Aim.");
                if (upgraded) strong.UpgradeInternal();
            }
            else strong = AddCard<StrongShuriken>(combat, upgraded: upgraded);
            await PowerCmd.Apply<ShurikenAimPower>(Choice, combat.Enemy, 3, owner, null);
            await PowerCmd.Apply<VulnerablePower>(Choice, combat.Enemy, 2, owner, null);
            decimal damage = (12 + (upgraded ? 4 : 0)) * 1.5m * 1.5m;
            strong.UpdateDynamicVarPreview(CardPreviewMode.Normal, combat.Enemy, strong.DynamicVars);
            Require(strong.DynamicVars.Damage.PreviewValue == damage,
                "Strong Shuriken preview must add Aim before Marked/Vulnerable, with Focus or a generated snapshot.");
            await CardCmd.AutoPlay(Choice, strong, combat.Enemy);
            Require(combat.Enemy.CurrentHp == 1000 - (int)damage && !owner.HasPower<AimPower>(),
                "Strong Shuriken must match its Aim preview and consume Aim after attacking.");
        }
        GD.Print("PASS Aim volleys: Blade Sweep Stock2/3 Aim4/6, Scry batches, no-shot preservation, full/double evokes, shuffle, mid-volley gains, dead-target retargeting, Strong Shuriken preview/snapshots/Marked/Vulnerable.");
    }
}
