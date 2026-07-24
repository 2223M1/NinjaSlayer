using Godot;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Monsters;

[RegisterMonster]
public sealed class XiaoJiGasBomb : ModMonsterTemplate
{
    public const string ExplodeMoveId = "EXPLODE_MOVE";
    public const int ExplodeDamage = 6;

    private bool _hasExploded;

    public override MonsterAssetProfile AssetProfile =>
        new(SceneHelper.GetScenePath("creature_visuals/gas_bomb"));

    public override int MinInitialHp => 3;
    public override int MaxInitialHp => 3;
    public override DamageSfxType TakeDamageSfxType => DamageSfxType.Magic;
    public override bool ShouldFadeAfterDeath => false;
    public override string DeathSfx => "event:/sfx/enemy/enemy_attacks/living_fog/living_fog_minion_die";

    private bool HasExploded
    {
        get => _hasExploded;
        set
        {
            AssertMutable();
            _hasExploded = value;
        }
    }

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        await PowerCmd.Apply<DieForYouPower>(new ThrowingPlayerChoiceContext(), Creature, 1m, null, null);
        await PowerCmd.Apply<MinionPower>(new ThrowingPlayerChoiceContext(), Creature, 1m, Creature, null);
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        MoveState explode = new(ExplodeMoveId, ExplodeMove, new DeathBlowIntent(() => ExplodeDamage));
        explode.FollowUpState = explode;
        return new MonsterMoveStateMachine([explode], explode);
    }

    public async Task PrepareExplosionIntent(Creature bombCreature)
    {
        if (MoveStateMachine == null)
        {
            SetUpForCombat();
        }

        MoveState explode = (MoveState)MoveStateMachine!.States[ExplodeMoveId];
        SetMoveImmediate(explode, forceTransition: true);
        NCreature? node = bombCreature.GetCreatureNode();
        if (node != null && CombatState.IsLiveCombat())
        {
            await node.UpdateIntent(CombatState.HittableEnemies);
        }
    }

    public async Task ExecuteExplosion(Creature bombCreature)
    {
        if (bombCreature.IsDead || HasExploded)
        {
            return;
        }

        NCreature? node = bombCreature.GetCreatureNode();
        if (node != null)
        {
            await node.PerformIntent();
        }

        await ExplodeMove(CombatState.HittableEnemies);
    }

    private async Task ExplodeMove(IReadOnlyList<Creature> targets)
    {
        if (HasExploded || Creature.IsDead)
        {
            return;
        }

        HasExploded = true;
        IReadOnlyList<Creature> enemies = targets.Count > 0
            ? targets.Where(c => c.IsAlive && c.IsHittable).ToList()
            : CombatState.HittableEnemies;
        if (enemies.Count > 0)
        {
            Creature? target = Creature.PetOwner?.RunState.Rng.CombatTargets.NextItem(enemies);
            if (target == null)
            {
                await CreatureCmd.Kill(Creature);
                return;
            }

            await CreatureCmd.TriggerAnim(Creature, "ExplodeTrigger", 0.1f);
            SfxCmd.Play("event:/sfx/enemy/enemy_attacks/living_fog/living_fog_explode");
            target.GetVfxContainer()?.AddChildSafely(
                NGaseousImpactVfx.Create(CombatSide.Enemy, CombatState, new Color("#402f45")));
            await CreatureCmd.Damage(
                new ThrowingPlayerChoiceContext(),
                target,
                ExplodeDamage,
                ValueProp.Move,
                Creature);
        }

        if (!Creature.IsDead)
        {
            await CreatureCmd.Kill(Creature);
        }
    }

    public override CreatureAnimator GenerateAnimator(MegaSprite controller)
    {
        AnimState idle = new("idle_loop", isLooping: true);
        AnimState spawn = new("spawn");
        AnimState explode = new("explode");
        AnimState attack = new("attack");
        AnimState hurt = new("hurt");
        AnimState die = new("die");
        attack.NextState = idle;
        hurt.NextState = idle;
        spawn.NextState = idle;
        CreatureAnimator animator = new(spawn, controller);
        animator.AddAnyState("Idle", idle);
        animator.AddAnyState("Attack", attack);
        animator.AddAnyState("Dead", die, () => !HasExploded);
        animator.AddAnyState("Hit", hurt);
        animator.AddAnyState("ExplodeTrigger", explode);
        return animator;
    }
}
