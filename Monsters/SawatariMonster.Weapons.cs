using Godot;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Saves.Runs;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Monsters;

public sealed partial class SawatariMonster
{
    public const string DualMoveId = "DUAL_MACHETE";
    public const string ThrowMoveId = "THROW_MACHETE";
    private bool _actThree;
    private int _heldMachetes = 3;
    private bool _mustThrow;
    private int _nextThrowHand;

    [SavedProperty]
    public bool ActThree { get => _actThree; set { AssertMutable(); _actThree = value; } }
    // Two bits describe the two hands. Cards deliberately do not own knife identities.
    [SavedProperty]
    public int HeldMachetes { get => _heldMachetes; private set { AssertMutable(); _heldMachetes = value; } }
    [SavedProperty]
    public bool MustThrow { get => _mustThrow; private set { AssertMutable(); _mustThrow = value; } }
    [SavedProperty]
    public int NextThrowHand { get => _nextThrowHand; private set { AssertMutable(); _nextThrowHand = value; } }

    public int MacheteCount => (HeldMachetes & 1) + ((HeldMachetes >> 1) & 1);
    private static int DualDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 10, 8);
    private static int ThrowDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 14, 12);
    private static int ArrowDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 16, 14);
    public string PlannedMacheteMove => MacheteCount == 0 ? AttackMoveId
        : MustThrow || MacheteCount == 1 ? ThrowMoveId : DualMoveId;

    private MonsterMoveStateMachine GenerateMacheteMoves()
    {
        MoveState dual = new(DualMoveId, DualMove, new MultiAttackIntent(DualDamage, 2));
        MoveState throwing = new(ThrowMoveId, ThrowMove,
            new SingleAttackIntent(ThrowDamage), new BuffIntent(), new StatusIntent(1));
        MoveState bamboo = new(AttackMoveId, AttackMove,
            new MultiAttackIntent(BambooDamage, SawatariEventRules.AttackHits), new BuffIntent());
        ConditionalBranchState select = new("SELECT_WEAPON");
        select.AddState(dual, () => PlannedMacheteMove == DualMoveId);
        select.AddState(throwing, () => PlannedMacheteMove == ThrowMoveId);
        select.AddState(bamboo, () => PlannedMacheteMove == AttackMoveId);
        dual.FollowUpState = throwing.FollowUpState = bamboo.FollowUpState = select;
        return new MonsterMoveStateMachine([dual, throwing, bamboo, select], dual);
    }

    internal int ChooseReturnHand() => HeldMachetes switch
    {
        0 => RunRng.Niche.NextInt(2), 1 => 1, 2 => 0, _ => -1
    };

    public void ReturnMachete(int? hand = null)
    {
        AssertMutable();
        if (!ActThree || Creature.IsDead || MacheteCount == 2) return;
        HeldMachetes |= 1 << (hand ?? ChooseReturnHand());
        SetMoveImmediate((MoveState)MoveStateMachine!.States[PlannedMacheteMove], forceTransition: true);
        SawatariWeaponVisuals.Get(Creature)?.Refresh();
    }

    private Creature? ChooseTarget(IReadOnlyList<Creature> targets)
    {
        Creature[] alive = targets.Where(target => target.IsAlive && target.IsHittable).ToArray();
        return alive.Length == 0 ? null : RunRng.CombatTargets.NextItem(alive);
    }

    private async Task ArrowMove(IReadOnlyList<Creature> targets)
    {
        if (ChooseTarget(targets) is not { } target) return;
        await AttackWithWeapons(target, ArrowDamage, 1,
            victim => VfxCmd.PlayOnCreatureCenter(victim, VfxCmd.slashPath),
            _ => SawatariWeaponVisuals.PlayArrow(Creature, target));
        if (Creature.IsAlive)
            await PowerCmd.Apply<PlatingPower>(new BlockingPlayerChoiceContext(), Creature, 4, Creature, null);
    }

    internal Task PlayDualAttack(Creature target)
    {
        SawatariWeaponVisuals.Get(Creature)?.Refresh(dual: true);
        return AttackWithWeapons(target, DualDamage, 2,
            victim => NinjaSlayerCombatVfx.PlaySawatariFlyingSlash(Creature, victim), null);
    }

    private async Task DualMove(IReadOnlyList<Creature> targets)
    {
        if (ChooseTarget(targets) is not { } target) return;
        await PlayDualAttack(target);
        MustThrow = true;
        SawatariWeaponVisuals.Get(Creature)?.Refresh();
    }

    private async Task ThrowMove(IReadOnlyList<Creature> targets)
    {
        if (ChooseTarget(targets) is not { Player: { } player } target) return;
        var combatState = CombatState;
        NextThrowHand = MacheteCount == 2 ? RunRng.Niche.NextInt(2) : HeldMachetes == 1 ? 0 : 1;
        int hand = NextThrowHand;
        int catchHand = SawatariMachete.ChooseFreeHand(player);
        Sprite2D? knife = null;
        try
        {
            await AttackWithWeapons(target, ThrowDamage, 1, NinjaSlayerCombatVfx.PlayMacheteHitFx, async hit =>
            {
                if (hit != 0) return;
                HeldMachetes &= ~(1 << hand);
                MustThrow = false;
                knife = await SawatariWeaponVisuals.PlayThrow(this, target, hand, catchHand);
            });
            // The native damage command owns defenses and death; throwing is not conditional on HP loss.
            if (Creature.IsAlive)
                await PowerCmd.Apply<VigorPower>(new BlockingPlayerChoiceContext(), Creature, 6, Creature, null);
            if (player.Creature.IsAlive)
            {
                var card = combatState.CreateCard<SawatariMachete>(player);
                card.HeldHand = catchHand;
                if (GodotObject.IsInstanceValid(knife)) PlayerMacheteVisuals.Bind(card, knife!);
                await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, null);
                knife = null;
            }
            SawatariWeaponVisuals.Get(Creature)?.Refresh();
        }
        finally
        {
            if (GodotObject.IsInstanceValid(knife)) knife!.QueueFree();
        }
    }

    // FromMonster always targets every player in both supported hosts. Preserve
    // the attack hooks/history while the native damage command hits our chosen target.
    private async Task AttackWithWeapons(Creature target, int damage, int hits,
        Action<Creature> hitFx, Func<int, Task>? beforeHit)
    {
        var combatState = CombatState;
        AttackCommand attack = DamageCmd.Attack(damage).WithHitCount(hits).FromMonster(this);
        var choice = new BlockingPlayerChoiceContext();
        await Hook.BeforeAttack(combatState, attack);
        decimal hitCount = Hook.ModifyAttackHitCount(combatState, attack, hits);
        var results = new List<DamageResult>();
        async Task<bool> Impact()
        {
            if (!Creature.IsAlive || !target.IsAlive || !combatState.ContainsCreature(target)
                || CombatManager.Instance.IsOverOrEnding || !target.IsHittable) return false;
            // The dual thrust's blade sound is also audible on a miss.
            if (beforeHit == null) NDebugAudioManager.Instance?.Play(TmpSfx.heavyAttack);
            bool connects = target.GetPower<EvasionPower>() is not { } evasion
                || !evasion.CanEvade(target, attack.DamageProps, Creature);
            if (connects) hitFx(target);
            results.AddRange(await CreatureCmd.Damage(choice, [target], damage, attack.DamageProps, Creature, null
#if !NINJASLAYER_LEGACY_DAMAGE_API
                , null
#endif
            ));
            return Creature.IsAlive && target.IsAlive;
        }
        try
        {
            if (beforeHit == null)
            {
                using var pacing = CombatPresentationPacingScope.Begin(CombatPresentationPacingPolicy.ComboDamage);
                await SawatariWeaponVisuals.PlayDual(Creature, target, checked((int)Math.Ceiling(hitCount)), Impact);
            }
            else
            {
                for (int hit = 0; hit < hitCount && Creature.IsAlive && target.IsAlive; hit++)
                {
                    await beforeHit(hit);
                    if (!await Impact()) break;
                }
            }
        }
        finally
        {
            attack.AddResultsInternal(results);
            CombatManager.Instance.History.CreatureAttacked(combatState, Creature, results);
            await Hook.AfterAttack(combatState, choice, attack);
        }
    }
}
