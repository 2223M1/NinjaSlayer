using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves.Runs;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using NinjaSlayer.Scripts;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

public sealed class YamotoKokiCuteRelic : NinjaSlayerRelicTemplate
{
    private const string CombatsKey = "Combats";
    private const float MissileAttackIntervalSeconds = 0.2f;
    private const float MoveAfterMissilesDelaySeconds = 0.2f;
    private readonly record struct MissileOperation(NCreature? Node, Task Explosion);
    private int _combatsLeft = 5;
    private bool _hasPlayedEntrance;
    private bool _hasPlayedFarewell;

    public override RelicRarity Rarity => RelicRarity.Event;
    public override bool AddsPet => true;
    public override bool SpawnsPets => true;
    public override bool IsUsedUp => CombatsLeft <= 0;
    public override bool ShowCounter => true;
    public override int DisplayAmount => Math.Max(0, CombatsLeft);

    public override RelicAssetProfile AssetProfile =>
        NinjaSlayerRelicAssets.For(this);

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DynamicVar(CombatsKey, CombatsLeft)
    ];

    [SavedProperty]
    public int CombatsLeft
    {
        get => _combatsLeft;
        set
        {
            AssertMutable();
            _combatsLeft = value;
            DynamicVars[CombatsKey].BaseValue = _combatsLeft;
            InvokeDisplayAmountChanged();
            Status = IsUsedUp ? RelicStatus.Disabled : RelicStatus.Normal;
        }
    }

    [SavedProperty]
    public bool HasPlayedEntrance
    {
        get => _hasPlayedEntrance;
        set
        {
            AssertMutable();
            _hasPlayedEntrance = value;
        }
    }

    [SavedProperty]
    public bool HasPlayedFarewell
    {
        get => _hasPlayedFarewell;
        set
        {
            AssertMutable();
            _hasPlayedFarewell = value;
        }
    }

    public override async Task BeforeCombatStart()
    {
        if (IsUsedUp || !YamotoKokiPartyState.IsController(this))
        {
            return;
        }

        Flash();
        Creature? yamotoKoki = YamotoKokiPartyState.FindLivingCompanion(Owner.RunState);
        bool created = yamotoKoki == null;
        if (created)
        {
            yamotoKoki = await PlayerCmd.AddPet<YamotoKokiMonster>(Owner);
        }

        YamotoKokiIntentLifecycle.BeginCombat(yamotoKoki!);
        await AssignIntent(yamotoKoki!, YamotoKokiMonster.SummonMissileMoveId);
        if (created && !YamotoKokiPartyState.HasPlayedEntrance(Owner.RunState))
        {
            HasPlayedEntrance = true;
            _ = TaskHelper.RunSafely(YamotoKokiCombatAnimations.PlayEntrance(yamotoKoki!));
        }
    }

    public override Task AfterCombatEnd(CombatRoom room)
    {
        if (IsUsedUp || IsMelted)
        {
            return Task.CompletedTask;
        }

        CombatsLeft = YamotoKokiRelicLifetimePolicy.CompleteCombat(CombatsLeft);
        if (!YamotoKokiRelicLifetimePolicy.ShouldPlayFarewell(
                YamotoKokiPartyState.GetActiveRelicCount(Owner.RunState),
                YamotoKokiPartyState.HasPlayedFarewell(Owner.RunState)))
        {
            return Task.CompletedTask;
        }

        Creature? yamotoKoki = YamotoKokiPartyState.FindLivingCompanion(Owner.RunState);
        if (yamotoKoki == null || yamotoKoki.IsDead)
        {
            return Task.CompletedTask;
        }

        YamotoKokiIntentLifecycle.Invalidate(yamotoKoki);
        HasPlayedFarewell = true;
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.YamotoKokiByeEvent);
        _ = TaskHelper.RunSafely(YamotoKokiCombatAnimations.PlayFarewell(yamotoKoki));
        return Task.CompletedTask;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player != Owner || !YamotoKokiPartyState.IsController(this))
        {
            return;
        }

        try
        {
            await PerformTurnStartActions();
        }
        catch (Exception ex)
        {
            Entry.Logger.Error(
                $"Yamoto Koki turn-start action failed; releasing the player turn instead of blocking card input: {ex}");
        }
    }

    private async Task PerformTurnStartActions()
    {
        int turnNumber = Owner.PlayerCombatState?.TurnNumber ?? 0;
        List<Creature> armedMissiles = Owner.PlayerCombatState?.Pets
            .Where(pet => pet.Monster is YamotoKokiOrigamiMissile missile
                && missile.CanExplodeOnTurn(turnNumber))
            .ToList() ?? [];
        if (armedMissiles.Count > 0)
        {
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.YamotoKokiFastAttackEvent);
        }

        Creature? yamotoKoki = YamotoKokiPartyState.FindLivingCompanion(Owner.RunState);
        YamotoKokiMonster? monster = yamotoKoki?.Monster as YamotoKokiMonster;
        MoveState? scheduledMove = monster?.NextMove;

        IReadOnlyList<MissileOperation> missileOperations =
            await StartMissileAttacksStaggered(armedMissiles);
        if (yamotoKoki == null || yamotoKoki.IsDead || monster == null || scheduledMove == null)
        {
            await CompleteMissileOperations(missileOperations);
            return;
        }

        IReadOnlyList<Creature> enemies = yamotoKoki.CombatState?.HittableEnemies ?? [];
        if (armedMissiles.Count > 0)
        {
            Task postLaunchDelay = Cmd.Wait(MoveAfterMissilesDelaySeconds);
            Task missileResolution = WaitForMissileResolutions(missileOperations);
            await Task.WhenAll(postLaunchDelay, missileResolution);
            if (CombatManager.Instance.IsOverOrEnding || yamotoKoki.IsDead)
            {
                await CompleteMissileOperations(missileOperations);
                return;
            }

            Flash();
            Task moveTask = PerformScheduledMove(
                yamotoKoki,
                scheduledMove,
                enemies,
                overlapIntentPresentation: true);
            await Task.WhenAll(CompleteMissileOperations(missileOperations), moveTask);
        }
        else
        {
            await CompleteMissileOperations(missileOperations);
            if (CombatManager.Instance.IsOverOrEnding)
            {
                return;
            }

            Flash();
            await PerformScheduledMove(
                yamotoKoki,
                scheduledMove,
                enemies,
                overlapIntentPresentation: false);
        }

        monster.MoveStateMachine?.OnMovePerformed(scheduledMove);
        if (CombatManager.Instance.IsOverOrEnding)
        {
            YamotoKokiIntentLifecycle.Invalidate(yamotoKoki);
            return;
        }

        await AssignRandomIntent(yamotoKoki);
    }

    private static async Task<IReadOnlyList<MissileOperation>> StartMissileAttacksStaggered(
        List<Creature> armedMissiles)
    {
        List<MissileOperation> operations = [];
        for (int i = 0; i < armedMissiles.Count; i++)
        {
            if (i > 0)
            {
                await Cmd.Wait(MissileAttackIntervalSeconds);
            }

            if (CombatManager.Instance.IsOverOrEnding)
            {
                break;
            }

            Creature armedMissile = armedMissiles[i];
            NCreature? missileNode = armedMissile.GetCreatureNode();
            if (armedMissile.Monster is YamotoKokiOrigamiMissile missile)
            {
                operations.Add(new MissileOperation(
                    missileNode,
                    missile.ExecuteExplosion(armedMissile)));
            }
        }

        return operations;
    }

    private static Task WaitForMissileResolutions(IReadOnlyList<MissileOperation> operations) =>
        Task.WhenAll(operations.Select(operation => operation.Explosion));

    private static async Task CompleteMissileOperations(
        IReadOnlyList<MissileOperation> operations)
    {
        await WaitForMissileResolutions(operations);
        Task[] deathAnimations = operations
            .Select(operation => operation.Node?.DeathAnimationTask)
            .OfType<Task>()
            .ToArray();
        if (deathAnimations.Length > 0)
        {
            await Task.WhenAll(deathAnimations);
        }
    }

    private static async Task PerformScheduledMove(
        Creature yamotoKoki,
        MoveState scheduledMove,
        IReadOnlyList<Creature> enemies,
        bool overlapIntentPresentation)
    {
        NCreature? node = yamotoKoki.GetCreatureNode();
        Task intentTask = node?.PerformIntent() ?? Task.CompletedTask;
        if (!overlapIntentPresentation)
        {
            await intentTask;
            await scheduledMove.PerformMove(enemies);
            return;
        }

        Task moveTask = scheduledMove.PerformMove(enemies);
        await Task.WhenAll(intentTask, moveTask);
    }

    private static async Task AssignRandomIntent(Creature yamotoKoki)
    {
        if (yamotoKoki.Monster is not YamotoKokiMonster monster)
        {
            return;
        }

        if (monster.MoveStateMachine == null)
        {
            monster.SetUpForCombat();
        }

        YamotoKokiIntentGeneration generation = YamotoKokiIntentLifecycle.Capture(yamotoKoki);
        if (!CanChooseNextIntent(generation))
        {
            return;
        }

        ICombatState combatState = yamotoKoki.CombatState!;
        IReadOnlyList<Creature> enemies = combatState.HittableEnemies
            .Where(enemy => enemy.IsAlive && enemy.IsHittable)
            .ToList();
        int nextTurn = (yamotoKoki.PetOwner?.PlayerCombatState?.TurnNumber ?? 0) + 1;
        IReadOnlyList<Creature> nextTurnMissiles = yamotoKoki.PetOwner?.PlayerCombatState?.Pets
            .Where(pet => pet.Monster is YamotoKokiOrigamiMissile missile
                && missile.CanExplodeOnTurn(nextTurn))
            .ToList() ?? [];
        bool forceIai = FinisherForecast.EvaluateYamotoKokiNextTurn(
                yamotoKoki,
                enemies,
                nextTurnMissiles)
            == FinisherForecastOutcome.Guaranteed;
        MoveState next = forceIai
            ? (MoveState)monster.MoveStateMachine!.States[YamotoKokiMonster.IaiSlashMoveId]
            : YamotoKokiMonster.PickRandomMove(
                monster.MoveStateMachine!,
                yamotoKoki.PetOwner!.RunState.Rng.MonsterAi);
        await AssignIntent(yamotoKoki, next, generation);
    }

    private static async Task AssignIntent(Creature yamotoKoki, string moveId)
    {
        if (yamotoKoki.Monster is not YamotoKokiMonster monster)
        {
            return;
        }

        if (monster.MoveStateMachine == null)
        {
            monster.SetUpForCombat();
        }

        MoveState next = (MoveState)monster.MoveStateMachine!.States[moveId];
        await AssignIntent(yamotoKoki, next, YamotoKokiIntentLifecycle.Capture(yamotoKoki));
    }

    private static async Task AssignIntent(
        Creature yamotoKoki,
        MoveState next,
        YamotoKokiIntentGeneration generation)
    {
        if (yamotoKoki.Monster is not YamotoKokiMonster monster)
        {
            return;
        }

        if (!CanSetMove(generation))
        {
            return;
        }

        monster.SetMoveImmediate(next, forceTransition: true);

        NCreature? node = yamotoKoki.GetCreatureNode();
        ICombatState? combatState = yamotoKoki.CombatState;
        if (node != null
            && combatState != null
            && combatState.IsLiveCombat()
            && combatState.HittableEnemies.Any(enemy => enemy.IsAlive && enemy.IsHittable)
            && YamotoKokiIntentLifecycle.PrepareContainerForWrite(generation))
        {
            await node.UpdateIntent(combatState.HittableEnemies);
            if (!YamotoKokiIntentLifecycle.IsCurrent(generation))
            {
                YamotoKokiIntentLifecycle.RehideIfInactive(generation);
            }
        }
    }

    private static bool CanSetMove(YamotoKokiIntentGeneration generation)
    {
        Creature yamotoKoki = generation.Creature;
        return YamotoKokiIntentLifecycle.IsCurrent(generation)
            && !yamotoKoki.IsDead
            && yamotoKoki.CombatState != null
            && !CombatManager.Instance.IsOverOrEnding;
    }

    private static bool CanChooseNextIntent(YamotoKokiIntentGeneration generation) =>
        CanSetMove(generation)
        && generation.Creature.CombatState is { } combatState
        && combatState.IsLiveCombat()
        && combatState.HittableEnemies.Any(enemy => enemy.IsAlive && enemy.IsHittable);

}
