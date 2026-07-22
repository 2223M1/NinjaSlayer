using Godot;
using MegaCrit.Sts2.Core.Entities.Characters;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Relics;
using STS2RitsuLib.Scaffolding.Characters;
using STS2RitsuLib.Scaffolding.Godot;
using STS2RitsuLib.Scaffolding.Visuals.StateMachine;

namespace NinjaSlayer.Content;

public interface INinjaSlayerCharacter
{
}

public abstract class NinjaSlayerCharacterTemplate<TCardPool>
    : ModCharacterTemplate<TCardPool, NinjaSlayerRelicPool, NinjaSlayerPotionPool>, INinjaSlayerCharacter
    where TCardPool : CardPoolModel
{
    public override CharacterGender Gender => NinjaSlayerCharacterStats.Gender;
    public override CharacterAssetProfile AssetProfile => NinjaSlayerAssetProfile.Profile;
    public override Color NameColor => NinjaSlayerCharacterStats.NameColor;
    public override int StartingHp => NinjaSlayerCharacterStats.StartingHp;
    public override int StartingGold => NinjaSlayerCharacterStats.StartingGold;
    public override Color EnergyLabelOutlineColor => NinjaSlayerCharacterStats.EnergyLabelOutlineColor;
    public override Color DialogueColor => NinjaSlayerCharacterStats.DialogueColor;
    public override VfxColor SpeechBubbleColor => NinjaSlayerCharacterStats.SpeechBubbleColor;
    public override Color MapDrawingColor => NinjaSlayerCharacterStats.MapDrawingColor;
    public override Color RemoteTargetingLineColor => NinjaSlayerCharacterStats.RemoteTargetingLineColor;
    public override Color RemoteTargetingLineOutline => NinjaSlayerCharacterStats.RemoteTargetingLineOutlineColor;
    public override float AttackAnimDelay => NinjaSlayerCharacterStats.AttackAnimDelay;
    public override float CastAnimDelay => NinjaSlayerCharacterStats.CastAnimDelay;
    public override bool RequiresEpochAndTimeline => NinjaSlayerCharacterStats.RequiresEpochAndTimeline;
    public override string CharacterSelectSfx => NinjaSlayerAudio.NinjaSlayerSelectEvent;
    public override string CharacterTransitionSfx => NinjaSlayerAudio.NinjaSlayerTransitionEvent;
    protected override IEnumerable<string> ExtraAssetPaths => BossDeathExplosionVfx.AssetPaths;

    protected override NCreatureVisuals? TryCreateCreatureVisuals() =>
        RitsuGodotNodeFactories.CreateFromScenePath<NCreatureVisuals>(NinjaSlayerAssetProfile.VisualsPath);

    protected override ModAnimStateMachine? SetupCustomCombatAnimationStateMachine(
        Node visualsRoot,
        CharacterModel character) =>
        NinjaSlayerAnimations.BuildCombatAnimationStateMachine(visualsRoot, character);

    [Obsolete("RitsuLib compatibility path for non-Spine combat animation triggers.")]
    protected override ModAnimStateMachine? SetupCustomNonSpineAnimationStateMachine(
        Node visualsRoot,
        CharacterModel character) =>
        NinjaSlayerAnimations.BuildCombatAnimationStateMachine(visualsRoot, character);

    public override List<string> GetArchitectAttackVfx() =>
        [.. NinjaSlayerCharacterStats.ArchitectAttackVfx];
}
