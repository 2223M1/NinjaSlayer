using System.Diagnostics;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Monsters;
using NinjaSlayer.Powers;
using NinjaSlayer.Relics;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private const string MacheteSoundB = "event:/sfx/enemy/enemy_attacks/vantom/vantom_dismember";
    internal bool IsBladeFeedbackPreview => _configuration.PreviewFormFinisher == "BladeFeedbackB";

    internal void ObserveBladeCommand(MegaCrit.Sts2.Core.Commands.Builders.AttackCommand command)
    {
        if (IsBladeFeedbackPreview)
            _checkpoints.Write("blade.command", data: new JsonObject
            {
                ["card"] = command.ModelSource?.Id.ToString(), ["sfx"] = command.HitSfx,
                ["tmp"] = command.TmpHitSfx, ["vfx"] = command.HitVfx
            });
    }

    private async Task VerifyBladeFeedback(string directory, Func<bool, Task<SawatariMonster>> replace)
    {
        var combat = CombatManager.Instance.DebugOnlyGetState()!;
        var player = LocalContext.GetMe(combat.RunState)!;
        var choice = new BlockingPlayerChoiceContext();
        var room = NCombatRoom.Instance!;
        var sections = new JsonArray();
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Fast;
        await PowerCmd.Remove<EvasionPower>(player.Creature);
        await PowerCmd.Remove<KaratePower>(player.Creature);
        foreach (var card in PileType.Hand.GetPile(player).Cards.ToArray())
            await CardPileCmd.Add(card, PileType.Discard);
        var model = await replace(false);
        var relic = await RelicCmd.Obtain<YamotoKokiCuteRelic>(player);
        await relic.BeforeCombatStart();
        Creature koki = player.PlayerCombatState!.Pets.Single(p => p.Monster is YamotoKokiMonster);
        await WaitFrames(60);

        void Section(string name)
        {
            sections.Add(new JsonObject { ["name"] = name, ["qpc"] = Stopwatch.GetTimestamp() });
            _checkpoints.Write("blade.feedback." + name);
        }
        Task Weapon(string method, Creature target) => (Task)AccessTools.Method(typeof(SawatariMonster), method)
            .Invoke(model, method == "PlayAttack" || method == "PlayDualAttack"
                ? [target] : [new Creature[] { target }])!;
        async Task SoundOnce(string sound, Func<Task> action)
        {
            var heard = new List<string>();
            SawatariSoundObserver = (path, _) => heard.Add(path);
            try
            {
                await action();
                Require(heard.Count(path => path == sound) == 1 && !heard.Contains(TmpSfx.slashAttack),
                    $"Expected one {sound} and no legacy slash sound: {string.Join(", ", heard)}");
            }
            finally { SawatariSoundObserver = null; }
        }
        try
        {
            Section("audio-preroll");
            for (int i = 0; i < 2; i++)
            {
                await CardCmd.AutoPlay(choice, combat.CreateCard<KarateStraightRedesignV1>(player), model.Creature);
                await WaitFrames(45);
            }
            Section("bamboo");
            await VerifySawatariFeedback(BambooFeedback, model.Creature, player.Creature,
                () => Weapon("PlayAttack", player.Creature), 4);
            await WaitFrames(35);
            Section("arrow");
            await VerifySawatariFeedback(ArrowFeedback, model.Creature, player.Creature,
                () => Weapon("ArrowMove", player.Creature), 1);
            await WaitFrames(45);
            model = await replace(true);
            Section("dual-iron-wave");
            for (int i = 0; i < 2; i++)
            {
                await VerifySawatariFeedback(DualFeedback, model.Creature, player.Creature,
                    () => Weapon("PlayDualAttack", player.Creature), 2);
                await WaitFrames(30);
            }
            const string macheteSound = MacheteSoundB;
            for (int i = 0; i < 2; i++)
            {
                Section("machete-throw-" + (i + 1));
                await VerifySawatariFeedback(ThrowFeedback with { HitSound = macheteSound }, model.Creature, player.Creature,
                    () => Weapon("ThrowMove", player.Creature), 1);
                await WaitFrames(30);
                Section("machete-return-" + (i + 1));
                await SoundOnce(macheteSound, () => CardCmd.AutoPlay(choice,
                    PileType.Hand.GetPile(player).Cards.OfType<SawatariMachete>().Single(), model.Creature));
                await WaitFrames(30);
            }
            Section("koki-iai");
            var iai = (MoveState)koki.Monster!.MoveStateMachine!.States[YamotoKokiMonster.IaiSlashMoveId];
            for (int i = 0; i < 2; i++)
            {
                await SoundOnce(TmpSfx.heavyAttack, () => iai.PerformMove([model.Creature]));
                await WaitFrames(30);
            }
            Vector2 position = model.Creature.GetCreatureNode()!.Position;
            var dark = (DarkNinjaMonster)ModelDb.Monster<DarkNinjaMonster>().ToMutable();
            Creature enemy = combat.CreateCreature(dark, CombatSide.Enemy, null);
            await CreatureCmd.Add(enemy);
            var old = model.Creature.GetCreatureNode()!;
            room.RemoveCreatureNode(old);
            old.QueueFree();
            CombatManager.Instance.RemoveCreature(model.Creature);
            if (combat.ContainsCreature(model.Creature)) combat.RemoveCreature(model.Creature);
            enemy.GetCreatureNode()!.Position = position;
            enemy.SetMaxHpInternal(1000);
            await CreatureCmd.SetCurrentHp(enemy, 1000);
            dark.RollMove(combat.PlayerCreatures);
            var stance = (MoveState)dark.MoveStateMachine!.States[DarkNinjaMonster.CounterStanceMoveId];
            await stance.PerformMove([player.Creature]);
            await PowerCmd.Remove<EvasionPower>(enemy);
            await WaitFrames(40);
            Section("dark-counter");
            for (int i = 0; i < 2; i++)
            {
                await PowerCmd.Remove<IaiPower>(enemy);
                await PowerCmd.Apply<IaiPower>(choice, enemy, 1, enemy, null);
                await SoundOnce("event:/sfx/characters/ironclad/ironclad_attack", () => CardCmd.AutoPlay(choice,
                    combat.CreateCard<StrikeNinjaSlayerRedesignV1>(player), enemy));
                await WaitFrames(35);
            }
            Section("completed");
            await WaitFrames(45);
        }
        finally
        {
            File.WriteAllText(Path.Combine(directory, "blade-sections.json"), sections.ToJsonString());
        }
    }
}
