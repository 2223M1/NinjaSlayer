using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using NinjaSlayer.Monsters;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyRelease038()
    {
        using var fixture = new DarkStrikeFixture();
        var monster = fixture.Monster;
        monster.SetUpForCombat();
        string[] sequence = [DarkNinjaMonster.CounterStanceMoveId,
            DarkNinjaMonster.DarkStrikeMoveId, DarkNinjaMonster.DeathSlashMoveId,
            DarkNinjaMonster.DarkRobeMoveId, DarkNinjaMonster.DarkStrikeMoveId,
            DarkNinjaMonster.DeathSlashMoveId, DarkNinjaMonster.DarkRobeMoveId];
        var machine = monster.MoveStateMachine!;
        Require(machine.States.Count == 4, "Dark Ninja must have one opener and a three-move loop.");
        foreach (string expected in sequence)
        {
            MoveState move = machine.RollMove([fixture.Target], monster.Creature, fixture.Run.Rng.MonsterAi);
            Require(move.Id == expected, "Dark Ninja skipped or repeated a move in its first two cycles.");
            machine.OnMovePerformed(move);
        }
        foreach (string state in machine.States.Keys)
        {
            var restored = (DarkNinjaMonster)MegaCrit.Sts2.Core.Models.ModelDb.Monster<DarkNinjaMonster>().ToMutable();
            restored.RunRng = fixture.Run.Rng;
            restored.SetUpForCombat();
            var current = (MoveState)restored.MoveStateMachine!.States[state];
            restored.MoveStateMachine.ForceCurrentState(current);
            restored.MoveStateMachine.OnMovePerformed(current);
            var next = restored.MoveStateMachine.RollMove([fixture.Target], monster.Creature, fixture.Run.Rng.MonsterAi);
            Require(next.Id == ((MoveState)machine.States[state]).FollowUpState!.Id,
                "Restoring a native action state must preserve its next intent.");
        }

        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.InitPrefsDataForTest();
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.InitSettingsDataForTest();
        MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
        var dark = monster.Creature;
        var counter = (await PowerCmd.Apply<DarkCounterPower>(Choice, dark, 3, dark, null))!;
        await PowerCmd.Apply<StrengthPower>(Choice, dark, 10, dark, null);
        await PowerCmd.Apply<VigorPower>(Choice, dark, 8, dark, null);
        int targetHp = fixture.Target.CurrentHp;
        var description = counter.Description;
        description.Add("Amount", counter.Amount);
        string preview = description.GetFormattedText();
        Require(preview.Contains("18") && counter.Amount == 3
            && dark.GetPowerAmount<VigorPower>() == 8 && fixture.Target.CurrentHp == targetHp,
            "Counter hover must show attack-modified damage without attacking or consuming Vigor.");
        GD.Print("PASS 0.3.8 Dark Ninja opener/two cycles/native-state restoration and side-effect-free counter preview");
    }
}
