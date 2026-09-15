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
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Monsters;
using NinjaSlayer.Powers;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    internal Action<string, float>? SawatariSoundObserver;
    internal Action<Vector2, string?>? SawatariVfxObserver;
    internal Action<Creature?, Creature?>? SawatariDamageObserver;
    private readonly JsonArray _sawatariFeedbackSamples = [];
    private sealed record WeaponFeedback(string Name, string Vfx, string? HitSound,
        string? ReleaseSound = null, bool SwingOnMiss = false);
    private static readonly WeaponFeedback BambooFeedback = new("bamboo", VfxCmd.dramaticStabPath, TmpSfx.bluntAttack);
    private static readonly WeaponFeedback ArrowFeedback = new("arrow", VfxCmd.slashPath, null,
        "event:/sfx/enemy/enemy_attacks/crossbow_ruby_raider/crossbow_ruby_raider_attack");
    private static readonly WeaponFeedback DualFeedback = new("dual", VfxCmd.flyingSlashPath, TmpSfx.heavyAttack, SwingOnMiss: true);
    private static readonly WeaponFeedback ThrowFeedback = new("throw", VfxCmd.slashPath, MacheteSoundB, TmpSfx.daggerThrow);

    private async Task VerifySawatariFeedback(WeaponFeedback expected, Creature source, Creature target,
        Func<Task> action, int hits, int misses = 0, bool cancelled = false)
    {
        var damage = new List<ulong>();
        var effects = new List<ulong>();
        var sounds = new List<(string Path, ulong Frame)>();
        var launches = new List<ulong>();
        var samples = new JsonArray();
        ulong start = Time.GetTicksUsec();
        var room = NCombatRoom.Instance!;
        bool atCore = true, arrived = true, defaultVolume = true;
        Sprite2D? projectile = null;
        void Record(string kind, string? path = null) => samples.Add(new JsonObject
        {
            ["kind"] = kind, ["path"] = path, ["frame"] = Engine.GetProcessFrames(),
            ["seconds"] = (Time.GetTicksUsec() - start) / 1e6
        });
        void Launch(Node node)
        {
            if (node.SceneFilePath == "res://scenes/vfx/vfx_flying_slash.tscn")
            {
                var effect = (Node2D)node;
                Vector2 direction = (target.GetCreatureNode()!.VfxSpawnPosition - source.GetCreatureNode()!.VfxSpawnPosition).Normalized();
                Require(effect.GlobalTransform.X.Dot(direction) > .999f
                    && Mathf.IsEqualApprox(effect.GlobalTransform.X.Length(), 1f)
                    && Mathf.IsEqualApprox(effect.GlobalTransform.Y.Length(), 1f),
                    "Dual slash must face from the actual attacker toward its target without scaling its artwork.");
                SawatariVfxObserver?.Invoke(effect.GlobalPosition, VfxCmd.flyingSlashPath);
            }
            if (node.SceneFilePath == "res://scenes/vfx/vfx_dramatic_stab.tscn")
            {
                Require(!node.HasNode("slash") && node.HasNode("Flash") && node.HasNode("Sparks"),
                    "Bamboo must retain flash/sparks and remove the long line before entering the tree.");
                SawatariVfxObserver?.Invoke(((Node2D)node).GlobalPosition, VfxCmd.dramaticStabPath);
            }
            if (node is not Sprite2D sprite || sprite.Texture?.ResourcePath is not { } path
                || !(path.EndsWith("/arrow.png") || path.EndsWith("/machete.png"))) return;
            projectile = sprite;
            launches.Add(Engine.GetProcessFrames());
            Record("release", path);
        }
        SawatariSoundObserver = (path, volume) =>
        {
            if (path != expected.HitSound && path != expected.ReleaseSound) return;
            sounds.Add((path, Engine.GetProcessFrames()));
            defaultVolume &= Mathf.IsEqualApprox(volume, 1f);
            Record("sound", path);
        };
        SawatariVfxObserver = (position, path) =>
        {
            if (path != expected.Vfx) return;
            effects.Add(Engine.GetProcessFrames());
            atCore &= position.DistanceTo(target.GetCreatureNode()!.VfxSpawnPosition) < .5f;
            if (expected.ReleaseSound != null)
            {
                arrived &= GodotObject.IsInstanceValid(projectile)
                    && projectile!.GetParent() != room.CombatVfxContainer
                    || GodotObject.IsInstanceValid(projectile) && projectile!.GlobalPosition.DistanceTo(position) < .5f;
            }
            Record("vfx", path);
        };
        SawatariDamageObserver = (victim, dealer) =>
        {
            if (victim != target || dealer != source) return;
            damage.Add(Engine.GetProcessFrames());
            Record("damage");
        };
        room.CombatVfxContainer.ChildEnteredTree += Launch;
        try
        {
            await action();
            int contacts = cancelled ? 0 : hits - misses;
            Require(damage.Count == contacts, $"{expected.Name}: expected {contacts} damage hooks, got {damage.Count}.");
            Require(effects.SequenceEqual(damage), $"{expected.Name}: contact VFX frames differ from damage hooks.");
            Require(atCore && arrived, $"{expected.Name}: feedback missed the core or preceded projectile arrival.");
            Require(defaultVolume, $"{expected.Name}: weapon sound changed the native volume.");
            if (expected.HitSound != null)
            {
                ulong[] hitSounds = sounds.Where(s => s.Path == expected.HitSound).Select(s => s.Frame).ToArray();
                int count = expected.SwingOnMiss && !cancelled ? hits : contacts;
                Require(hitSounds.Length == count, $"{expected.Name}: expected {count} hit sounds, got {hitSounds.Length}.");
                if (misses == 0) Require(hitSounds.SequenceEqual(damage), $"{expected.Name}: hit sound did not share the damage frame.");
            }
            if (expected.ReleaseSound != null)
            {
                ulong[] releaseSounds = sounds.Where(s => s.Path == expected.ReleaseSound).Select(s => s.Frame).ToArray();
                Require(launches.Count == 1 && releaseSounds.SequenceEqual(launches),
                    $"{expected.Name}: release sound did not coincide with the one real projectile.");
            }
            _checkpoints.Write($"sawatari.feedback.{source.Side}.{expected.Name}.{SaveManager.Instance.PrefsSave.FastMode}.{misses}.{cancelled}");
        }
        finally
        {
            room.CombatVfxContainer.ChildEnteredTree -= Launch;
            SawatariSoundObserver = null;
            SawatariVfxObserver = null;
            SawatariDamageObserver = null;
            _sawatariFeedbackSamples.Add(new JsonObject { ["action"] = expected.Name,
                ["side"] = source.Side.ToString(), ["mode"] = SaveManager.Instance.PrefsSave.FastMode.ToString(),
                ["misses"] = misses, ["cancelled"] = cancelled, ["events"] = samples });
            File.WriteAllText(Path.Combine(_configuration.ActionPreviewDirectory!, "sawatari-feedback.json"),
                _sawatariFeedbackSamples.ToJsonString());
        }
    }

    private async Task VerifySawatariFeedbackDefenses(SawatariMonster enemy)
    {
        var combat = CombatManager.Instance.DebugOnlyGetState()!;
        var player = LocalContext.GetMe(combat.RunState)!;
        var choice = new BlockingPlayerChoiceContext();
        Task Dual(SawatariMonster model, Creature target) => (Task)AccessTools.Method(typeof(SawatariMonster), "PlayDualAttack").Invoke(model, [target])!;
        Task Arrow(SawatariMonster model, Creature target) => (Task)AccessTools.Method(typeof(SawatariMonster), "ArrowMove").Invoke(model, [new Creature[] { target }])!;
        Task Bamboo(SawatariMonster model, Creature target) => (Task)AccessTools.Method(typeof(SawatariMonster), "PlayAttack").Invoke(model, [target])!;
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Fast;
        await CreatureCmd.GainBlock(player.Creature, 100, ValueProp.Unpowered, null);
        int hp = player.Creature.CurrentHp;
        await VerifySawatariFeedback(DualFeedback, enemy.Creature, player.Creature, () => Dual(enemy, player.Creature), 2);
        Require(player.Creature.CurrentHp == hp, "Blocked dual attack changed HP.");
        await RemoveSmokeBlock(player.Creature);
        await PowerCmd.Apply<EvasionPower>(choice, player.Creature, 4, player.Creature, null);
        await VerifySawatariFeedback(BambooFeedback, enemy.Creature, player.Creature, () => Bamboo(enemy, player.Creature), 4, 4);
        await PowerCmd.Apply<EvasionPower>(choice, player.Creature, 2, player.Creature, null);
        await VerifySawatariFeedback(DualFeedback, enemy.Creature, player.Creature, () => Dual(enemy, player.Creature), 2, 2);
        await PowerCmd.Apply<EvasionPower>(choice, player.Creature, 1, player.Creature, null);
        await VerifySawatariFeedback(ArrowFeedback, enemy.Creature, player.Creature, () => Arrow(enemy, player.Creature), 1, 1);
        await PowerCmd.Apply<EvasionPower>(choice, player.Creature, 1, player.Creature, null);
        await VerifySawatariFeedback(ThrowFeedback, enemy.Creature, player.Creature,
            () => (Task)AccessTools.Method(typeof(SawatariMonster), "ThrowMove").Invoke(enemy, [new Creature[] { player.Creature }])!, 1, 1);
        Require(player.Creature.CurrentHp == hp, "Evaded weapon attacks changed HP.");
        await CardCmd.AutoPlay(choice, PileType.Hand.GetPile(player).Cards.OfType<SawatariMachete>().Single(), enemy.Creature);

        var ally = (SawatariMonster)ModelDb.Monster<SawatariMonster>().ToMutable();
        ally.ActThree = true;
        Creature friend = combat.CreateCreature(ally, CombatSide.Player, null);
        friend.PetOwner = player;
        await CreatureCmd.Add(friend);
        ally.RollMove(combat.HittableEnemies.ToArray());
        await ally.AfterAddedToRoom();
        Require(friend.GetCreatureNode()!.Visuals.HasNode("SawatariWeapons"), "Allied Sawatari has no weapon rig.");
        friend.GetCreatureNode()!.Position = player.Creature.GetCreatureNode()!.Position + new Vector2(210, 0);
        await WaitFrames(25);
        try
        {
            await VerifySawatariFeedback(DualFeedback, friend, enemy.Creature, () => Dual(ally, enemy.Creature), 2);
            await VerifySawatariFeedback(BambooFeedback, friend, enemy.Creature, () => Bamboo(ally, enemy.Creature), 4);
            await VerifySawatariFeedback(ArrowFeedback, friend, enemy.Creature, () => Arrow(ally, enemy.Creature), 1);
            var disappearing = (SawatariMonster)ModelDb.Monster<SawatariMonster>().ToMutable();
            Creature victim = combat.CreateCreature(disappearing, CombatSide.Enemy, null);
            await CreatureCmd.Add(victim);
            disappearing.RollMove(combat.PlayerCreatures);
            await VerifySawatariFeedback(ArrowFeedback, friend, victim, async () =>
            {
                Task shot = Arrow(ally, victim);
                await Cmd.Wait(.25f);
                var actor = victim.GetCreatureNode()!;
                NCombatRoom.Instance!.RemoveCreatureNode(actor);
                actor.QueueFree();
                CombatManager.Instance.RemoveCreature(victim);
                if (combat.ContainsCreature(victim)) combat.RemoveCreature(victim);
                await shot;
                await WaitFrames(5);
            }, 1, cancelled: true);
            var frail = (SawatariMonster)ModelDb.Monster<SawatariMonster>().ToMutable();
            Creature dying = combat.CreateCreature(frail, CombatSide.Enemy, null);
            await CreatureCmd.Add(dying);
            frail.RollMove(combat.PlayerCreatures);
            await CreatureCmd.SetCurrentHp(dying, 1);
            await VerifySawatariFeedback(DualFeedback, friend, dying, () => Dual(ally, dying), 1);
            Require(dying.IsDead, "Lethal dual did not stop after its first hit.");
        }
        finally
        {
            var actor = friend.GetCreatureNode()!;
            NCombatRoom.Instance!.RemoveCreatureNode(actor);
            actor.QueueFree();
            CombatManager.Instance.RemoveCreature(friend);
            if (combat.ContainsCreature(friend)) combat.RemoveCreature(friend);
        }
    }
}
