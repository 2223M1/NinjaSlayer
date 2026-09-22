using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Powers;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private sealed partial class TheaterRuntime
    {
        private async Task BloodCheck(string mode)
        {
            Creature actor = Actor("ninja"), enemy = Actor("enemy");
            Node? Effect() => _room.CombatVfxContainer.GetChildren()
                .FirstOrDefault(node => node.Name.ToString().StartsWith("MouthBlood_",StringComparison.Ordinal)
                    && !node.IsQueuedForDeletion());
            async Task Hit(int amount = 1, ValueProp props = ValueProp.Move, Creature? dealer = null)
            {
                await CreatureCmd.Damage(_choice,[actor],amount,props,dealer ?? enemy);
                await _driver.WaitFrames(1);
            }
            await RemoveSmokeBlock(actor);
            await RemovePower<EvasionPower>(actor);
            await RemovePower<NarakuLifePower>(actor);
            actor.SetMaxHpInternal(100);
            if (mode == "boundary")
            {
                await CreatureCmd.SetCurrentHp(actor,27);
                await Hit();
                Require(Effect() == null,"Blood emitted above 25%.");
                await Wait(.5);
                await Hit();
                Require(actor.CurrentHp == 25 && Effect() != null,"Blood did not emit at exactly 25%.");
                Node first = Effect()!;
                await Wait(.22);
                await Hit();
                Require(ReferenceEquals(first,Effect()),"Combo stacked blood stream instances.");
                await Wait(1.2);
                Require(Effect() == null,"Blood tail did not release.");
                await CreatureCmd.GainBlock(actor,10,ValueProp.Unpowered,null);
                await Hit(3);
                Require(Effect() == null,"Fully blocked hit emitted blood.");
                await RemoveSmokeBlock(actor);
                await Hit(1,ValueProp.Unpowered);
                Require(Effect() == null,"Non-attack HP loss emitted blood.");
                await Hit(1,ValueProp.Move,actor);
                Require(Effect() == null,"Self-damage emitted blood.");
                await PowerCmd.Apply<EvasionPower>(_choice,actor,1,actor,null);
                await Hit(2);
                Require(Effect() == null,"Evaded hit emitted blood.");
                await RemovePower<EvasionPower>(actor);
                Cover("blood-threshold-block-self-dot-dodge-single-instance");
            }
            else if (mode == "lifecycle")
            {
                FastModeType previous = SaveManager.Instance.PrefsSave.FastMode;
                SceneTree tree = _room.GetTree();
                try
                {
                    SaveManager.Instance.PrefsSave.FastMode = FastModeType.Instant;
                    await CreatureCmd.SetCurrentHp(actor,25);
                    await Hit();
                    Require(Effect() == null,"Instant mode created a blood effect.");
                    SaveManager.Instance.PrefsSave.FastMode = FastModeType.Fast;
                    await Hit();
                    Node effect = Effect() ?? throw new InvalidOperationException("Fast mode omitted blood.");
                    var elapsed = AccessTools.Field(effect.GetType(), "_elapsed");
                    float before = (float)elapsed.GetValue(effect)!;
                    tree.Paused = true;
                    await Task.Delay(150);
                    Require((float)elapsed.GetValue(effect)! == before,"Paused blood advanced its lifetime.");
                    tree.Paused = false;
                    await Wait(1.3);
                    Require(Effect() == null,"Resumed blood did not release.");
                    await Hit();
                    Require(Effect() != null,"Blood did not restart before death cleanup.");
                    actor.SetCurrentHpInternal(0);
                    await _driver.WaitFrames(3);
                    Require(Effect() == null,"Blood survived its actor's death.");
                    Cover("blood-pause-fast-instant-death-cleanup");
                }
                finally
                {
                    tree.Paused = false;
                    actor.SetCurrentHpInternal(25);
                    SaveManager.Instance.PrefsSave.FastMode = previous;
                }
            }
            else
            {
                await CreatureCmd.SetCurrentHp(actor,25);
                await Wait(.15);
                Require(Effect() == null,"Setting low HP emitted blood without a hit.");
                await Hit(4);
                Require(Effect() != null,"Low HP hit did not emit blood in " + mode);
                if (mode == "moving")
                    await PlayCard(new() { Card = "KarateStraightRedesignV1" });
                await Wait(1.3);
                Require(Effect() == null,"Blood retained nodes after " + mode);
                Cover("blood-"+mode);
            }
        }
    }
}
