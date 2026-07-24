using Godot;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using STS2RitsuLib.Scaffolding.Godot;
using STS2RitsuLib.Scaffolding.Visuals.StateMachine;

namespace NinjaSlayer.Monsters;

[RegisterMonster]
public sealed class XiaoJiMonster : ModMonsterTemplate
{
    public const string SummonBombMoveId = "SUMMON_BOMB";
    public const string IaiSlashMoveId = "IAI_SLASH";
    public const int IaiSlashDamage = 6;

    public override int MinInitialHp => 9999;
    public override int MaxInitialHp => 9999;
    public override bool IsHealthBarVisible => false;

    protected override NCreatureVisuals? TryCreateCreatureVisuals() =>
        RitsuGodotNodeFactories.CreateFromScenePath<NCreatureVisuals>(NinjaSlayerAssetProfile.VisualsPath);

    protected override ModAnimStateMachine? SetupCustomCombatAnimationStateMachine(
        Node visualsRoot,
        MonsterModel monster)
    {
        if (Creature.PetOwner?.Character is not CharacterModel character
            || character is not INinjaSlayerCharacter)
        {
            return null;
        }

        return NinjaSlayerAnimations.BuildCombatAnimationStateMachine(visualsRoot, character);
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        MoveState summon = new(SummonBombMoveId, SummonBombMove, new SummonIntent());
        MoveState slash = new(
            IaiSlashMoveId,
            IaiSlashMove,
            new SingleAttackIntent(IaiSlashDamage));
        summon.FollowUpState = summon;
        slash.FollowUpState = slash;
        return new MonsterMoveStateMachine([summon, slash], summon);
    }

    public static MoveState PickRandomMove(MonsterMoveStateMachine machine, Rng rng)
    {
        string moveId = rng.NextBool() ? SummonBombMoveId : IaiSlashMoveId;
        return (MoveState)machine.States[moveId];
    }

    private async Task SummonBombMove(IReadOnlyList<Creature> _)
    {
        if (Creature.PetOwner is not { } owner)
        {
            return;
        }

        Creature bombCreature = await PlayerCmd.AddPet<XiaoJiGasBomb>(owner);
        if (bombCreature.Monster is XiaoJiGasBomb bomb)
        {
            await bomb.PrepareExplosionIntent(bombCreature);
        }
    }

    private async Task IaiSlashMove(IReadOnlyList<Creature> targets)
    {
        IReadOnlyList<Creature> enemies = targets.Count > 0
            ? targets.Where(c => c.IsAlive && c.IsHittable).ToList()
            : CombatState.HittableEnemies;
        if (enemies.Count == 0)
        {
            return;
        }

        await CreatureCmd.TriggerAnim(Creature, "Attack", 0.15f);
        NDebugAudioManager.Instance?.Play(TmpSfx.slashAttack);
        VfxCmd.PlayOnCreatureCenters(enemies, VfxCmd.giantHorizontalSlashPath);
        await CreatureCmd.Damage(
            new ThrowingPlayerChoiceContext(),
            enemies,
            IaiSlashDamage,
            ValueProp.Move,
            Creature);
    }
}
