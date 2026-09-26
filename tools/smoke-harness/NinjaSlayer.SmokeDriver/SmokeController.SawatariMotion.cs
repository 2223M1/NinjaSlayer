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
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Monsters;
using NinjaSlayer.Powers;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task VerifySawatariMotion(string directory, Func<bool, Task<SawatariMonster>> replace)
    {
        var combat = CombatManager.Instance.DebugOnlyGetState()!;
        var player = LocalContext.GetMe(combat.RunState)!;
        var choice = new BlockingPlayerChoiceContext();
        var room = NCombatRoom.Instance!;
        var timings = new JsonArray();
        Type weapons = typeof(SawatariMonster).Assembly.GetType("NinjaSlayer.Code.Nodes.SawatariWeaponVisuals", true)!;
        await PowerCmd.Remove<EvasionPower>(player.Creature);
        await PowerCmd.Remove<KaratePower>(player.Creature);
        foreach (var card in PileType.Hand.GetPile(player).Cards.ToArray()) await CardPileCmd.Add(card, PileType.Discard);
        SawatariMonster model = await replace(false);
        async Task AudioReference()
        {
            await CardCmd.AutoPlay(choice, combat.CreateCard<KarateStraightRedesignV1>(player), model.Creature);
            await WaitFrames(80);
        }
        await AudioReference();
        await AudioReference();
        foreach (FastModeType speed in new[] { FastModeType.Normal, FastModeType.Fast })
        {
            SaveManager.Instance.PrefsSave.FastMode = speed;
            var actor = model.Creature.GetCreatureNode()!;
            var body = actor.Visuals.GetNode<Sprite2D>("%Visuals");
            var rig = actor.Visuals.GetNode("SawatariWeapons");
            AccessTools.Method(weapons, "ShowBow").Invoke(rig, [true]);
            await WaitFrames(25);
            var arrow = body.GetNode<Sprite2D>("WeaponRig/BowAndArrow/Arrow");
            float lastX = arrow.Position.X;
            float firstX = lastX;
            ulong started = Time.GetTicksUsec();
            double damageTime = -1;
            void Damage(int oldHp, int hp) { if (hp < oldHp) damageTime = (Time.GetTicksUsec() - started) / 1e6; }
            player.Creature.CurrentHpChanged += Damage;
            try
            {
                Task attack = VerifySawatariFeedback(ArrowFeedback, model.Creature, player.Creature,
                    () => (Task)AccessTools.Method(typeof(SawatariMonster), "ArrowMove")
                        .Invoke(model, [new Creature[] { player.Creature }])!, 1);
                while (!attack.IsCompleted)
                {
                    await WaitFrames(1);
                    if (GodotObject.IsInstanceValid(arrow) && arrow.GetParent() != room.CombatVfxContainer)
                    {
                        Require(arrow.Position.X >= lastX - .1f, "Arrow shoved forward during its draw.");
                        lastX = arrow.Position.X;
                    }
                }
                await attack;
                Require(lastX - firstX > 40f, "Bow did not pull the arrow back by the requested 24 actor pixels.");
                // Draw and flight each start on a render frame; allow their two-frame sampling error.
                Require(Math.Abs(damageTime - .333) <= 2d / 60 + .003, $"Arrow contact time was {damageTime:F4}s.");
                timings.Add(new JsonObject { ["action"] = "arrow", ["mode"] = speed.ToString(), ["damageSeconds"] = damageTime });
                _checkpoints.Write("sawatari.motion.arrow." + speed);
            }
            finally { player.Creature.CurrentHpChanged -= Damage; }
            await WaitFrames(35);
        }
        foreach (FastModeType speed in new[] { FastModeType.Normal, FastModeType.Fast, FastModeType.Instant })
        {
            SaveManager.Instance.PrefsSave.FastMode = speed;
            await VerifySawatariFeedback(BambooFeedback, model.Creature, player.Creature,
                () => (Task)AccessTools.Method(typeof(SawatariMonster), "PlayAttack").Invoke(model, [player.Creature])!, 4);
            await WaitFrames(20);
        }
        await VerifySawatariFeedback(ArrowFeedback, model.Creature, player.Creature,
            () => (Task)AccessTools.Method(typeof(SawatariMonster), "ArrowMove")
                .Invoke(model, [new Creature[] { player.Creature }])!, 1);
        model = await replace(true);
        var enemy = model.Creature.GetCreatureNode()!;
        var anchor = enemy.Visuals.GetNode<Node2D>("AirborneAnchor");
        Transform2D baseline = anchor.Transform;
        Vector2 layout = enemy.Position;
        Vector2 intent = enemy.IntentContainer.Position;
        var visual = enemy.Visuals.GetNode("SawatariWeapons");
        var weaponRoot = enemy.Visuals.GetNode<Sprite2D>("%Visuals").GetNode("WeaponRig");
        Sprite2D[] knives = weaponRoot.GetChildren().OfType<Node2D>().Take(2).Select(n => n.GetChild<Sprite2D>(0)).ToArray();
        float[] sizes = knives.Select(n => n.GlobalTransform.X.Length()).ToArray();
        foreach (FastModeType speed in new[] { FastModeType.Normal, FastModeType.Fast, FastModeType.Instant })
        {
            SaveManager.Instance.PrefsSave.FastMode = speed;
            var impacts = new List<double>();
            ulong start = Time.GetTicksUsec();
            ulong frame = Engine.GetProcessFrames();
            var hitFrames = new List<ulong>();
            void Damage(int oldHp, int hp)
            {
                if (hp >= oldHp) return;
                impacts.Add((Time.GetTicksUsec() - start) / 1e6);
                hitFrames.Add(Engine.GetProcessFrames());
            }
            player.Creature.CurrentHpChanged += Damage;
            try
            {
                await VerifySawatariFeedback(DualFeedback, model.Creature, player.Creature,
                    () => (Task)AccessTools.Method(typeof(SawatariMonster), "PlayDualAttack").Invoke(model, [player.Creature])!, 2);
                Require(impacts.Count == 2, "Dual thrust did not resolve exactly two damage events.");
                if (speed == FastModeType.Instant)
                    Require(hitFrames.All(f => f == frame), "Instant dual thrust yielded a render frame.");
                else
                    Require(Math.Abs(impacts[1] - impacts[0] - 7d * 1001 / 24000) < .035,
                        $"Dual hit cadence was {impacts[1] - impacts[0]:F4}s.");
                Require(anchor.Transform.IsEqualApprox(baseline) && enemy.Position.IsEqualApprox(layout)
                    && enemy.IntentContainer.Position.IsEqualApprox(intent), "Dual thrust drifted its body or combat UI.");
                timings.Add(new JsonObject { ["action"] = "dual", ["mode"] = speed.ToString(),
                    ["first"] = impacts[0], ["second"] = impacts[1] });
                _checkpoints.Write("sawatari.motion.dual." + speed);
            }
            finally { player.Creature.CurrentHpChanged -= Damage; }
            await WaitFrames(35);
        }
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
        Task Dual() => (Task)AccessTools.Method(typeof(SawatariMonster), "PlayDualAttack").Invoke(model, [player.Creature])!;
        async Task HurtDuringAttack()
        {
            await Cmd.Wait(.12f);
            await CreatureCmd.Damage(choice, [model.Creature], 5m, ValueProp.Unpowered, player.Creature, null
#if !NINJASLAYER_LEGACY_DAMAGE_API
                , null
#endif
            );
        }
        await Task.WhenAll(Dual(), HurtDuringAttack());
        await WaitFrames(20);
        Require(anchor.Transform.IsEqualApprox(baseline), "Dual/hurt overlap left a tilted body.");
        Task pausedAttack = Dual();
        await WaitFrames(2);
        _tree.Paused = true;
        try
        {
            Transform2D pausedPose = anchor.Transform;
            await WaitFrames(8);
            Require(anchor.Transform.IsEqualApprox(pausedPose), "Dual motion advanced while the game was paused.");
        }
        finally { _tree.Paused = false; }
        await pausedAttack;
        Require(anchor.Transform.IsEqualApprox(baseline), "Paused dual did not restore its baseline.");
        _checkpoints.Write("sawatari.motion.hurt-and-pause-restored");
        await WaitFrames(30);
        for (int hand = 0; hand < 2; hand++)
        {
            SaveManager.Instance.PrefsSave.FastMode = hand == 0 ? FastModeType.Normal : FastModeType.Fast;
            Sprite2D knife = knives[hand];
            bool damageAtContact = false;
            void Damage(int oldHp, int hp)
            {
                if (hp < oldHp) damageAtContact = knife.GetParent().Name.ToString() is "Primary" or "Secondary";
            }
            player.Creature.CurrentHpChanged += Damage;
            try
            {
                Task throwing = VerifySawatariFeedback(ThrowFeedback, model.Creature, player.Creature,
                    () => (Task)AccessTools.Method(typeof(SawatariMonster), "ThrowMove")
                        .Invoke(model, [new Creature[] { player.Creature }])!, 1);
                while (!throwing.IsCompleted)
                {
                    await WaitFrames(1);
                    if (knife.GetParent() == room.CombatVfxContainer)
                        Require(Math.Abs(knife.GlobalTransform.X.Length() - sizes[hand]) * 285 < .5f,
                            "The thrown knife changed size during flight.");
                }
                await throwing;
                Require(damageAtContact, "Thrown knife damage did not coincide with arrival at the hand.");
                SaveScreenshot(Path.Combine(directory, $"held-source-sized-{hand + 1}.png"));
            }
            finally { player.Creature.CurrentHpChanged -= Damage; }
            await WaitFrames(40);
            Require(Math.Abs(knife.GlobalTransform.X.Length() - sizes[hand]) * 285 < .5f,
                "The received knife changed size after hurt recovered.");
            VerifyCaughtMacheteOrientation(knife);
            SaveScreenshot(Path.Combine(directory, $"held-mirror-fixed-{hand + 1}.png"));
        }
        async Task ReturnWithImpact(SawatariMachete card)
        {
            Sprite2D? slash = null;
            Sprite2D? projectile = null;
            ulong impactFrame = 0;
            Vector2 impactCore = Vector2.Zero;
            bool hit = false;
            bool onDamageFrame = false, atCore = false, withSound = false, arrived = false;
            var contactSounds = new List<ulong>();
            SawatariSoundObserver = (path, volume) =>
            {
                if (path == MacheteSoundB)
                {
                    Require(Mathf.IsEqualApprox(volume, 1f), "Player return changed the native sound volume.");
                    contactSounds.Add(Engine.GetProcessFrames());
                }
                Require(path != TmpSfx.slashAttack, "Player return layered the old slash sound.");
            };
            void Enter(Node node)
            {
                if (node is Sprite2D sprite && node.SceneFilePath.EndsWith("vfx_attack_slash.tscn"))
                {
                    slash = sprite;
                    impactFrame = Engine.GetProcessFrames();
                    impactCore = model.Creature.GetCreatureNode()!.VfxSpawnPosition;
                }
                if (node is Sprite2D blade && blade.Texture?.ResourcePath.EndsWith("/machete.png") == true)
                    projectile = blade;
            }
            void Damage(int oldHp, int hp)
            {
                if (hp >= oldHp) return;
                hit = true;
                onDamageFrame = slash != null && impactFrame == Engine.GetProcessFrames();
                atCore = slash != null && slash.GlobalPosition.DistanceTo(impactCore) < .5f;
                withSound = contactSounds.SequenceEqual(new[] { Engine.GetProcessFrames() });
                arrived = projectile?.GetParent()?.GetParent() == weaponRoot;
                timings.Add(new JsonObject { ["action"] = "machete-impact", ["mode"] = SaveManager.Instance.PrefsSave.FastMode.ToString(),
                    ["damageFrame"] = Engine.GetProcessFrames(), ["vfxFrame"] = impactFrame,
                    ["atCore"] = atCore, ["withSound"] = withSound, ["arrived"] = arrived });
            }
            room.CombatVfxContainer.ChildEnteredTree += Enter;
            model.Creature.CurrentHpChanged += Damage;
            try
            {
                Require(card.Type == CardType.Attack && card.Rarity == CardRarity.Token, "Machete is not a Token attack.");
                await CardCmd.AutoPlay(choice, card, model.Creature);
                Require(hit, "Machete did not produce real damage.");
                Require(onDamageFrame, "Machete slash did not start on the damage frame.");
                Require(atCore, "Machete slash missed the target core.");
                Require(withSound, "Machete damage did not start exactly one native axe attack sound.");
                Require(arrived, $"Machete hit feedback played before the knife arrived: {projectile?.GetPath()}.");
            }
            finally
            {
                SawatariSoundObserver = null;
                room.CombatVfxContainer.ChildEnteredTree -= Enter;
                model.Creature.CurrentHpChanged -= Damage;
            }
            await WaitFrames(25);
        }
        foreach (var card in PileType.Hand.GetPile(player).Cards.OfType<SawatariMachete>().ToArray())
        {
            await ReturnWithImpact(card);
            foreach (Sprite2D knife in knives) VerifyCaughtMacheteOrientation(knife);
        }
        for (int i = 0; i < knives.Length; i++)
        {
            Require(Math.Abs(knives[i].GlobalTransform.X.Length() - sizes[i]) * 285 < .5f,
                "Returning the knife changed its source size.");
            VerifyCaughtMacheteOrientation(knives[i]);
        }
        _checkpoints.Write("sawatari.motion.mirrored-catch-and-token-impact");
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Instant;
        Type facing = typeof(SawatariMachete).Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.NinjaSlayerFacingState", true)!;
        AccessTools.Method(facing, "SetFacing").Invoke(null, [player.Creature.GetCreatureNode()!, true]);
        await VerifySawatariFeedback(ThrowFeedback, model.Creature, player.Creature,
            () => (Task)AccessTools.Method(typeof(SawatariMonster), "ThrowMove")
                .Invoke(model, [new Creature[] { player.Creature }])!, 1);
        foreach (Sprite2D knife in knives) VerifyCaughtMacheteOrientation(knife);
        await ReturnWithImpact(PileType.Hand.GetPile(player).Cards.OfType<SawatariMachete>().Single());
        foreach (Sprite2D knife in knives) VerifyCaughtMacheteOrientation(knife);
        _checkpoints.Write("sawatari.motion.instant-catch-and-token-impact");
        AccessTools.Method(facing, "SetFacing").Invoke(null, [player.Creature.GetCreatureNode()!, false]);
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
        _checkpoints.Write("sawatari.motion.same-sized-round-trip");
        await VerifyPlayerWeaponForms(directory);
        await VerifySawatariFeedbackDefenses(model);
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Instant;
        Task instantArrow = (Task)AccessTools.Method(weapons, "PlayArrow").Invoke(null, [model.Creature, player.Creature])!;
        Require(instantArrow.IsCompletedSuccessfully, "Instant arrow created an awaited Tween.");
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
        AccessTools.Method(weapons, "ShowBamboo").Invoke(visual, null);
        await WaitFrames(35);
        AccessTools.Method(weapons, "ShowBow").Invoke(visual, [true]);
        await WaitFrames(3);
        await (Task)AccessTools.Method(weapons, "PlayArrow").Invoke(null, [model.Creature, player.Creature])!;
        _checkpoints.Write("sawatari.motion.swap-takeover");
        AccessTools.Method(weapons, "ShowBamboo").Invoke(visual, null);
        await WaitFrames(35);
        AccessTools.Method(weapons, "Refresh").Invoke(visual, [true]);
        await WaitFrames(35);
        await AudioReference();
        await AudioReference();
        File.WriteAllText(Path.Combine(directory, "sawatari-timing.json"), timings.ToJsonString());
        _checkpoints.Write("sawatari.motion.completed");
    }

    private static void VerifyCaughtMacheteOrientation(Sprite2D knife)
    {
        Transform2D grip = knife.GetParent<Node2D>().GlobalTransform;
        Transform2D blade = knife.GlobalTransform;
        Require(blade.X.Normalized().DistanceTo(grip.X.Normalized()) < .002f
            && blade.Y.Normalized().DistanceTo(grip.Y.Normalized()) < .002f,
            "Transferred Machete has a different mirror/angle from a newly generated blade at the same grip.");
    }
}
