using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyShurikenAim()
    {
        using (var combat = new OrbCombat())
        {
            var other = combat.AddEnemy();
            var owner = combat.Player.Creature;
            await PowerCmd.Apply<ShurikenAimPower>(Choice, other, 2, owner, null);
            await PowerCmd.Apply<FocusPower>(Choice, owner, 2, owner, null);
            await AddStock(combat.Player, 4);
            await OrbCmd.EvokeNext(Choice, combat.Player);
            Require(combat.Enemy.CurrentHp == 1000 && other.CurrentHp == 952
                && other.GetPowerAmount<ShurikenAimPower>() == 2,
                "All four focused 8-damage stock shots must prioritize Aim, deal 12 each and preserve duration.");
            var strong = AddCard<StrongShuriken>(combat);
            strong.UpdateDynamicVarPreview(CardPreviewMode.Normal, other, strong.DynamicVars);
            Require(strong.DynamicVars.Damage.PreviewValue == 12, "Strong Shuriken must preview Focus and Aim together.");
            await CardCmd.AutoPlay(Choice, strong, other);
            Require(other.CurrentHp == 940, "Strong Shuriken must deal the same damage as its Aim preview.");
            await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), other);
            Require(other.CurrentHp == 934, "Aim must not amplify ordinary attacks.");
            var aim = other.GetPower<ShurikenAimPower>()!;
            await aim.AfterSideTurnEnd(Choice, CombatSide.Player, [owner]);
            Require(aim.Amount == 2, "The player's turn end must not consume Aim duration.");
            await aim.AfterSideTurnEnd(Choice, CombatSide.Enemy, [combat.Enemy, other]);
            Require(aim.Amount == 1, "Enemy turn end must consume exactly one Aim duration.");
            await aim.AfterSideTurnEnd(Choice, CombatSide.Enemy, [combat.Enemy, other]);
            Require(!other.HasPower<ShurikenAimPower>(), "Aim must expire after its second enemy turn.");
        }
        using (var combat = new OrbCombat())
        {
            var marked = combat.AddEnemy();
            var owner = combat.Player.Creature;
            await PowerCmd.Apply<ShurikenAimPower>(Choice, marked, 3, owner, null);
            await PowerCmd.Apply<BladeSweepPower>(Choice, owner, 1, owner, null);
            await AddStock(combat.Player, 2);
            await OrbCmd.EvokeNext(Choice, combat.Player);
            Require(combat.Enemy.CurrentHp == 988 && marked.CurrentHp == 982,
                "Blade Sweep must still hit every enemy; only marked targets take 50% extra damage.");
        }
        foreach (bool upgraded in new[] { false, true })
        foreach (int snapshot in new[] { -1, 7 })
        {
            using var combat = new OrbCombat();
            var owner = combat.Player.Creature;
            var strong = AddCard<StrongShuriken>(combat, upgraded: upgraded);
            if (snapshot >= 0) strong.SnapshotDamage = snapshot;
            await PowerCmd.Apply<FocusPower>(Choice, owner, 2, owner, null);
            await PowerCmd.Apply<ShurikenAimPower>(Choice, combat.Enemy, 3, owner, null);
            await PowerCmd.Apply<VulnerablePower>(Choice, combat.Enemy, 2, owner, null);
            decimal baseDamage = (snapshot >= 0 ? snapshot : 8) + (upgraded ? 4 : 0);
            decimal expected = baseDamage * 1.5m * 1.5m;
            strong.UpdateDynamicVarPreview(CardPreviewMode.Normal, combat.Enemy, strong.DynamicVars);
            Require(strong.DynamicVars.Damage.PreviewValue == expected,
                "Strong Shuriken must multiply Aim and Vulnerable independently, including Focus, snapshots and upgrades.");
            await CardCmd.AutoPlay(Choice, strong, combat.Enemy);
            Require(combat.Enemy.CurrentHp == 1000 - (int)expected
                && combat.Enemy.GetPowerAmount<ShurikenAimPower>() == 3,
                "Strong Shuriken must round only after multiplying both debuffs, without spending Aim duration.");
        }
        using (var combat = new OrbCombat())
        {
            var second = combat.AddEnemy();
            var untouched = combat.AddEnemy();
            var owner = combat.Player.Creature;
            await PowerCmd.Apply<ShurikenAimPower>(Choice, combat.Enemy, 2, owner, null);
            await PowerCmd.Apply<ShurikenAimPower>(Choice, second, 3, owner, null);
            await AddStock(combat.Player, 10);
            await OrbCmd.EvokeNext(Choice, combat.Player);
            Require(untouched.CurrentHp == 1000 && combat.Enemy.CurrentHp + second.CurrentHp == 1910,
                "Multiple marked enemies must share the random target pool without leaking shots to unmarked enemies.");
            combat.Enemy.SetCurrentHpInternal(0);
            second.SetCurrentHpInternal(0);
            await AddStock(combat.Player, 1);
            await OrbCmd.EvokeNext(Choice, combat.Player);
            Require(untouched.CurrentHp == 994, "Dead marked enemies must not prevent shots from reaching a live unmarked enemy.");
        }
        using (var combat = new OrbCombat())
        {
            await PowerCmd.Apply<ArtifactPower>(Choice, combat.Enemy, 1, combat.Enemy, null);
            await CardCmd.AutoPlay(Choice, AddCard<FocusedBlows>(combat), combat.Enemy);
            Require(combat.Enemy.CurrentHp == 994 && !combat.Enemy.HasPower<ShurikenAimPower>()
                && !combat.Enemy.HasPower<ArtifactPower>(), "Artifact must block Aim without preventing the initial attack.");
        }
        GD.Print("PASS Aim: stock/Strong Shuriken +50%, multiplicative Vulnerable/Focus/snapshots/upgrades and preview, priority/multiple/dead targets, Blade Sweep, duration and Artifact.");
    }
}
