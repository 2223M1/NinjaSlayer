using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.ExternalAnimations;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using STS2RitsuLib.Scaffolding.Godot;

namespace NinjaSlayer.Monsters;

[RegisterMonster]
public sealed class YukanoMonster : ModMonsterTemplate
{
    public const string BarrierMoveId = "BARRIER";
    public const string ArrowMoveId = "ARROW";
    public const string HealMoveId = "HEAL";
    public const string ShurikenMoveId = "SHURIKEN";
    public const string ClosedTexturePath =
        "res://NinjaSlayer/images/monsters/yukano_closed.png";
    public const string OpenTexturePath =
        "res://NinjaSlayer/images/monsters/yukano_open.png";
    public const string ShurikenTexturePath =
        "res://NinjaSlayer/images/projectiles/yukano_red_shuriken.png";

    public override int MinInitialHp => 9999;
    public override int MaxInitialHp => 9999;
    public override bool IsHealthBarVisible => false;

    protected override string VisualsPath =>
        "res://NinjaSlayer/scenes/creature_visuals/yukano.tscn";

    protected override NCreatureVisuals? TryCreateCreatureVisuals() =>
        RitsuGodotNodeFactories.CreateFromScenePath<NCreatureVisuals>(VisualsPath);

    public override IEnumerable<string> AssetPaths => base.AssetPaths
        .Concat([
            ClosedTexturePath,
            OpenTexturePath,
            ShurikenTexturePath,
            YukanoCombatAnimations.ArrowAtlasPath
        ])
        .Distinct();

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        MoveState barrier = new(BarrierMoveId, BarrierMove, new DefendIntent());
        MoveState arrow = new(
            ArrowMoveId,
            ArrowMove,
            new SingleAttackIntent(() => GetArrowDamage()));
        MoveState heal = new(HealMoveId, HealMove, new HealIntent());
        MoveState shuriken = new(
            ShurikenMoveId,
            ShurikenMove,
            new DynamicMultiAttackIntent(
                () => GetShurikenDamage(),
                YukanoCompanionRules.ShurikenHits));
        barrier.FollowUpState = heal;
        arrow.FollowUpState = heal;
        heal.FollowUpState = shuriken;
        shuriken.FollowUpState = shuriken;
        return new MonsterMoveStateMachine([barrier, arrow, heal, shuriken], barrier);
    }

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        YukanoCombatAnimations.SetSpeaking(Creature, speaking: false);
    }

    internal void SetOpeningMove(bool anyEnemyIntendsToAttack)
    {
        if (MoveStateMachine == null)
        {
            SetUpForCombat();
        }

        string moveId = anyEnemyIntendsToAttack ? BarrierMoveId : ArrowMoveId;
        SetMoveImmediate((MoveState)MoveStateMachine!.States[moveId], forceTransition: true);
    }

    private async Task BarrierMove(IReadOnlyList<Creature> _)
    {
        foreach (Creature player in CombatState.PlayerCreatures.Where(player => player.IsAlive))
        {
            await MegaCrit.Sts2.Core.Commands.CreatureCmd.GainBlock(
                player,
                YukanoCompanionRules.BarrierBlock,
                ValueProp.Move,
                null);
        }
    }

    private Task ArrowMove(IReadOnlyList<Creature> targets) =>
        AttackRandomTarget(
            targets,
            GetArrowDamage(),
            hits: 1,
            YukanoCombatAnimations.PlayArrow);

    private async Task HealMove(IReadOnlyList<Creature> _)
    {
        foreach (Creature player in CombatState.PlayerCreatures.Where(player => player.IsAlive))
        {
            await MegaCrit.Sts2.Core.Commands.CreatureCmd.Heal(
                player,
                YukanoCompanionRules.HealAmount);
        }
    }

    private Task ShurikenMove(IReadOnlyList<Creature> targets) =>
        AttackRandomTarget(
            targets,
            GetShurikenDamage(),
            YukanoCompanionRules.ShurikenHits,
            YukanoCombatAnimations.PlayShuriken);

    private async Task AttackRandomTarget(
        IReadOnlyList<Creature> targets,
        int damage,
        int hits,
        Func<Creature, Creature, Task> playProjectile)
    {
        ICombatState combatState = CombatState;
        Creature[] candidates = targets
            .Where(target => CanHit(Creature, target, combatState))
            .ToArray();
        Creature? target = candidates.Length == 0
            ? null
            : combatState.RunState.Rng.CombatTargets.NextItem(candidates);
        if (target == null)
        {
            return;
        }

        AttackCommand command = MegaCrit.Sts2.Core.Commands.DamageCmd
            .Attack(damage)
            .WithHitCount(hits)
            .FromMonster(this);
        int hitCount = (int)Math.Ceiling(Math.Max(
            0m,
            Hook.ModifyAttackHitCount(combatState, command, hits)));
        var choiceContext = new BlockingPlayerChoiceContext();
        var results = new List<DamageResult>();

        await Hook.BeforeAttack(combatState, command);
        try
        {
            for (int i = 0; i < hitCount; i++)
            {
                if (!CanHit(Creature, target, combatState))
                {
                    break;
                }

                await playProjectile(Creature, target);
                if (!CanHit(Creature, target, combatState))
                {
                    break;
                }

                using (CombatPresentationPacingScope.Begin(CombatPresentationPacingPolicy.ComboDamage))
                {
                    results.AddRange(await CreatureCmd.Damage(
                        choiceContext,
                        [target],
                        damage,
                        command.DamageProps,
                        Creature,
                        null
#if !NINJASLAYER_LEGACY_DAMAGE_API
                        , null
#endif
                    ));
                }
            }
        }
        finally
        {
            if (results.Count > 0)
            {
                command.AddResultsInternal(results);
            }

            try
            {
                CombatManager.Instance.History.CreatureAttacked(
                    combatState,
                    Creature,
                    results);
            }
            finally
            {
                await Hook.AfterAttack(combatState, choiceContext, command);
            }
        }
    }

    private static bool CanHit(
        Creature attacker,
        Creature target,
        ICombatState combatState) =>
        attacker.IsAlive
        && target.IsAlive
        && target.IsHittable
        && target.Side != attacker.Side
        && ReferenceEquals(attacker.CombatState, combatState)
        && ReferenceEquals(target.CombatState, combatState)
        && combatState.ContainsCreature(attacker)
        && combatState.ContainsCreature(target)
        && combatState.IsLiveCombat()
        && !CombatManager.Instance.IsOverOrEnding;

    private int GetArrowDamage() => CompanionDamageMath.ScaleForActiveRelics(
        YukanoCompanionRules.ArrowDamage,
        YukanoCompanionPartyState.GetActiveRelicCount(Creature.PetOwner!.RunState));

    private int GetShurikenDamage() => CompanionDamageMath.ScaleForActiveRelics(
        YukanoCompanionRules.ShurikenDamage,
        YukanoCompanionPartyState.GetActiveRelicCount(Creature.PetOwner!.RunState));

    private sealed class DynamicMultiAttackIntent : MultiAttackIntent
    {
        public DynamicMultiAttackIntent(Func<decimal> damage, int repeats)
            : base(0, repeats)
        {
            DamageCalc = damage;
        }
    }
}
