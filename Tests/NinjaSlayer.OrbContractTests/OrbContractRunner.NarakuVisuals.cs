using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Relics;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static readonly List<string> NarakuSounds = [];
    private static bool RecordNarakuSound(string sfx) { NarakuSounds.Add(sfx); return false; }

    private async Task VerifyNarakuOtherCharacterVisuals()
    {
        if (!_hasPresentationResources) throw new InvalidOperationException("Naraku transformation requires the product scenes.");
        STS2RitsuLib.RitsuLibFramework.EnsureGodotScriptsRegistered(typeof(NarakuUnleashedRelic).Assembly,
            STS2RitsuLib.RitsuLibFramework.CreateLogger("Naraku contracts"));
        foreach (string path in new[] { NinjaSlayerAssetProfile.VisualsPath,
            "res://NinjaSlayer/images/characters/ninja_slayer/naraku.png" })
            PreloadManager.Cache.SetAsset(path, GD.Load(path));
        var harmony = new Harmony("NinjaSlayer.OrbContracts.NarakuVisuals");
        harmony.Patch(AccessTools.Method(typeof(Creature), nameof(Creature.GetCreatureNode)),
            prefix: new HarmonyMethod(GetType(), nameof(ResolveAimActor)));
        harmony.Patch(AccessTools.Method(typeof(SfxCmd), nameof(SfxCmd.Play), [typeof(string), typeof(float)]),
            prefix: new HarmonyMethod(GetType(), nameof(RecordNarakuSound)));
        try
        {
            foreach (var character in ModelDb.AllCharacters.Where(c => c is not INinjaSlayerCharacter))
            {
                using var combat = new OrbCombat(ninjaSlayer: true);
                var player = Player.CreateForNewRun(character, UnlockState.all, 2);
                _ = RunState.CreateForTest([combat.Player, player]);
                combat.State.AddPlayer(player);
                player.ResetCombatState();
                var originalDeck = player.Deck.Cards.ToArray();
                Require(!NinjaSlayerFormState.UsesNinjaSlayerVisuals(player.Creature),
                    "A teammate who has not accepted must keep their original presentation.");
                player.AddRelicInternal(ModelDb.Relic<NarakuUnleashedRelic>().ToMutable());
                NCreatureVisuals rig;
                TestMode.IsOn = false;
                try { rig = player.Creature.CreateVisuals() ?? throw new InvalidOperationException("Missing transformed rig."); }
                finally { TestMode.IsOn = true; }
                Require(rig.SceneFilePath == NinjaSlayerAssetProfile.VisualsPath,
                    $"{character.Id}: native creature creation must use the transformed rig.");
                var actor = new AimContractCreature();
                AccessTools.Property(typeof(NCreature), "Entity").SetValue(actor, player.Creature);
                AccessTools.Property(typeof(NCreature), "Visuals").SetValue(actor, rig);
                actor.AddChild(rig);
                AimActors.Add(player.Creature, actor);
                _hurtRoom = new FreeInputContractRoom();
                ((List<NCreature>)AccessTools.Field(typeof(NCombatRoom), "_creatureNodes").GetValue(_hurtRoom)!).Add(actor);
                _hurtRoom.AddChild(actor);
                AddChild(_hurtRoom);
                try
                {
                    NarakuVisualOverlay.Sync(player.Creature);
                    var body = rig.GetNode<Sprite2D>("%Visuals");
                    var overlay = rig.FindChild("NarakuVisualOverlay", true, false) as Sprite2D;
                    Require(!actor.HasSpineAnimation && !body.Visible && overlay is { Visible: true }
                        && overlay.Texture.ResourcePath.EndsWith("/naraku.png"),
                        $"{character.Id}: Naraku must replace the visible body.");
                    actor.SetAnimationTrigger("Dead");
                    Require(body.Texture.ResourcePath.Contains("/dead/"), "Transformed native death cues must reach the rig.");
                    actor.SetAnimationTrigger("Revive");
                    Require(body.Texture.ResourcePath.StartsWith(NinjaSlayerFormPresentationCatalog.NormalIdleTexturePrefix),
                        "Revival must return the transformed rig to idle.");
                    NarakuSounds.Clear();
                    Task animation = Task.CompletedTask;
                    Require(NinjaSlayerCombatAnimations.TryPlayTriggerAnim(player.Creature, "Attack", .01f, ref animation),
                        "A transformed player's attack must use Naraku animation and audio.");
                    await animation;
                    Require(NinjaSlayerCombatAnimations.TryPlayTriggerAnim(player.Creature, "Hit", 0f, ref animation),
                        "A transformed player's hurt must use Naraku animation and audio.");
                    await animation;
                    Require(NarakuSounds.Contains(NinjaSlayerCombatAudioSet.Naraku.FastAttack)
                        && NarakuSounds.Contains(NinjaSlayerCombatAudioSet.Naraku.Hurt)
                        && NinjaSlayerCombatAudioSet.For(player.Creature).Death == NinjaSlayerCombatAudioSet.Naraku.Death,
                        "Naraku's attack, hurt and death audio must be selected for other characters.");
                    Require(player.Character == character && player.Deck.Cards.SequenceEqual(originalDeck),
                        "Transformation must retain the original character and permanent deck.");
                    await ToSignal(GetTree().CreateTimer(.5), SceneTreeTimer.SignalName.Timeout);
                    GD.Print($"PASS Naraku {character.Id}: native rig factory, visible body, death/revive cues, attack/hurt audio, original identity and deck");
                }
                finally
                {
                    _hurtRoom.Free(); _hurtRoom = null;
                    AimActors.Remove(player.Creature);
                }
            }
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
