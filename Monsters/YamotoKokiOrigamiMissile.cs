using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Nodes;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Monsters;

[RegisterMonster]
public sealed class YamotoKokiOrigamiMissile : ModMonsterTemplate
{
    public const string ExplodeMoveId = "EXPLODE_MOVE";
    public const int ExplodeDamage = 4;
    private const float LaunchSeconds = 0.16f;

    private bool _hasExploded;
    private int _earliestExplosionTurn = int.MaxValue;
    private bool _isApplyingIntrinsicPowers;

    public override MonsterAssetProfile AssetProfile =>
        new(YamotoKokiOrigamiMissileVisuals.VisualsPath);

    protected override NCreatureVisuals? TryCreateCreatureVisuals() =>
        YamotoKokiOrigamiMissileVisuals.Create();

    public override int MinInitialHp => 1;
    public override int MaxInitialHp => 1;
    public override bool IsHealthBarVisible => false;
    public override DamageSfxType TakeDamageSfxType => DamageSfxType.Magic;
    public override bool ShouldFadeAfterDeath => false;
    public override string DeathSfx => "event:/sfx/enemy/enemy_attacks/living_fog/living_fog_minion_die";

    public bool IsLaunching { get; private set; }

    public int GetExplodeDamage() => CompanionDamageMath.ScaleForActiveRelics(
        ExplodeDamage,
        Creature.PetOwner is { } owner
            ? YamotoKokiPartyState.GetActiveRelicCount(owner.RunState)
            : 1);

    private bool HasExploded
    {
        get => _hasExploded;
        set
        {
            AssertMutable();
            _hasExploded = value;
        }
    }

    [SavedProperty]
    public int EarliestExplosionTurn
    {
        get => _earliestExplosionTurn;
        private set
        {
            AssertMutable();
            _earliestExplosionTurn = value;
        }
    }

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        _isApplyingIntrinsicPowers = true;
        try
        {
            await PowerCmd.Apply<MinionPower>(
                new ThrowingPlayerChoiceContext(),
                Creature,
                1m,
                Creature,
                null);
        }
        finally
        {
            _isApplyingIntrinsicPowers = false;
        }
    }

    public override bool ShouldAllowHitting(Creature creature) =>
        creature != Creature || _isApplyingIntrinsicPowers;

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        MoveState explode = new(ExplodeMoveId, ExplodeMove);
        explode.FollowUpState = explode;
        return new MonsterMoveStateMachine([explode], explode);
    }

    public async Task PrepareExplosionIntent(Creature missileCreature)
    {
        EarliestExplosionTurn = missileCreature.PetOwner?.PlayerCombatState?.TurnNumber + 1
            ?? int.MaxValue;
        if (MoveStateMachine == null)
        {
            SetUpForCombat();
        }

        MoveState explode = (MoveState)MoveStateMachine!.States[ExplodeMoveId];
        SetMoveImmediate(explode, forceTransition: true);
        NCreature? node = missileCreature.GetCreatureNode();
        if (node != null && CombatState.IsLiveCombat())
        {
            await node.UpdateIntent(CombatState.HittableEnemies);
        }
    }

    public bool CanExplodeOnTurn(int turnNumber) =>
        !HasExploded && Creature.IsAlive && turnNumber >= EarliestExplosionTurn;

    public async Task ExecuteExplosion(Creature missileCreature)
    {
        if (missileCreature.IsDead || HasExploded)
        {
            return;
        }

        await ExplodeMove(CombatState.HittableEnemies);
    }

    private async Task ExplodeMove(IReadOnlyList<Creature> targets)
    {
        if (HasExploded || Creature.IsDead) return;
        Creature? target = BeginExplosion();
        if (target != null) await LaunchAtTarget(target);
        await CompleteExplosion(target);
    }

    internal Creature? BeginExplosion()
    {
        if (HasExploded || Creature.IsDead)
            throw new InvalidOperationException("An inactive origami missile cannot launch twice.");
        HasExploded = true;
        Creature[] enemies = CombatState.HittableEnemies.Where(c => c.IsAlive && c.IsHittable).ToArray();
        Creature? target = enemies.Length == 0 ? null : Creature.PetOwner!.RunState.Rng.CombatTargets.NextItem(enemies);
        IsLaunching = target != null;
        return target;
    }

    internal async Task CompleteExplosion(Creature? target)
    {
        using var ranged = FinisherRangedAction.Begin(Creature);
        if (Creature.GetCreatureNode() is { } missileNode) ranged.Track(missileNode.Visuals);
        try
        {
            if (target is { IsAlive: true, IsHittable: true } && !Creature.IsDead
                && ReferenceEquals(target.CombatState, Creature.CombatState)
                && !CombatManager.Instance.IsOverOrEnding)
            {
                SfxCmd.Play("event:/sfx/enemy/enemy_attacks/living_fog/living_fog_explode");
                // Preserve the established explosion lead-in before the damage frame.
                await CreatureCmd.TriggerAnim(Creature, "ExplodeTrigger", 0.1f);
                Creature.GetCreatureNode()?.Visuals
                    .GetNodeOrNull<NYamotoKokiOrigamiMissileVfx>(
                        $"Visuals/{nameof(NYamotoKokiOrigamiMissileVfx)}")?.EnsureBurst();
                using (YamotoKokiOrigamiMissileHitSparkScope.Enter(target))
                using (CombatPresentationPacingScope.Begin(CombatPresentationPacingPolicy.ComboDamage))
                {
                    if (MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance is { } room && target.GetCreatureNode() != null
                        && MegaCrit.Sts2.Core.Nodes.Vfx.NFireSmokePuffVfx.Create(target) is { } puff)
                    {
                        puff.Scale = Godot.Vector2.One * 0.35f;
                        room.CombatVfxContainer.AddChildSafely(puff);
                    }
                    await CreatureCmd.Damage(new ThrowingPlayerChoiceContext(), target,
                        GetExplodeDamage(), ValueProp.Move | ValueProp.Unpowered, Creature);
                }
            }
            if (!Creature.IsDead) await CreatureCmd.Kill(Creature);
        }
        finally { IsLaunching = false; }
    }

    internal async Task LaunchAtTarget(Creature target)
    {
        NCreature? missileNode = Creature.GetCreatureNode();
        if (missileNode == null)
        {
            return;
        }

        missileNode.Visuals
            .GetNodeOrNull<NYamotoKokiOrigamiMissileIdleBob>(nameof(NYamotoKokiOrigamiMissileIdleBob))
            ?.StopAndReset();
        Label? damageAmount = missileNode.Visuals.GetNodeOrNull<Label>("%DamageAmount");
        if (damageAmount != null)
        {
            damageAmount.Visible = false;
        }

        NCreature? targetNode = target.GetCreatureNode();
        if (targetNode == null)
        {
            return;
        }

        Vector2 destination = missileNode.GlobalPosition
            + targetNode.VfxSpawnPosition
            - missileNode.VfxSpawnPosition;
        Tween tween = missileNode.CreateTween();
        tween.TweenProperty(
                missileNode,
                new NodePath("global_position"),
                destination,
                LaunchSeconds)
            .SetEase(Tween.EaseType.In)
            .SetTrans(Tween.TransitionType.Cubic);
        bool completed = await TweenPlayback.AwaitCompletion(tween, missileNode);
        if (completed && GodotObject.IsInstanceValid(missileNode))
        {
            missileNode.GlobalPosition = destination;
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
