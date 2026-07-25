using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

public sealed class YamotoKokiCuteRelic : NinjaSlayerRelicTemplate
{
    private const string CombatsKey = "Combats";
    private const float MissileAttackIntervalSeconds = 0.2f;
    private const float MoveAfterMissilesDelaySeconds = 0.2f;
    private readonly record struct BombOperation(NCreature? Node, Task Explosion);
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
            if (IsUsedUp)
            {
                Status = RelicStatus.Disabled;
            }
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
        if (IsUsedUp)
        {
            return;
        }

        Flash();
        Creature? yamotoKoki = FindLivingPartyYamotoKoki(Owner.RunState);
        bool created = yamotoKoki == null;
        if (created)
        {
            yamotoKoki = await PlayerCmd.AddPet<YamotoKokiMonster>(Owner);
            await AssignIntent(yamotoKoki, YamotoKokiMonster.SummonBombMoveId);
            if (!HasPlayedEntrance)
            {
                HasPlayedEntrance = true;
                await YamotoKokiCombatAnimations.PlayEntrance(yamotoKoki);
            }
        }

        CombatsLeft--;
    }

    public override async Task AfterCombatEnd(CombatRoom room)
    {
        if (!IsUsedUp || HasPlayedFarewell)
        {
            return;
        }

        Creature? yamotoKoki = FindLivingPartyYamotoKoki(Owner.RunState);
        if (yamotoKoki == null || yamotoKoki.IsDead)
        {
            return;
        }

        HasPlayedFarewell = true;
        await YamotoKokiCombatAnimations.PlayFarewell(yamotoKoki);
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player != Owner)
        {
            return;
        }

        int turnNumber = Owner.PlayerCombatState?.TurnNumber ?? 0;
        List<Creature> armedBombs = Owner.PlayerCombatState?.Pets
            .Where(pet => pet.Monster is YamotoKokiGasBomb bomb
                && bomb.CanExplodeOnTurn(turnNumber))
            .ToList() ?? [];
        if (armedBombs.Count > 0)
        {
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.YamotoKokiFastAttackEvent);
        }

        Creature? yamotoKoki = FindLivingPartyYamotoKoki(Owner.RunState);
        YamotoKokiMonster? monster = yamotoKoki?.Monster as YamotoKokiMonster;
        MoveState? scheduledMove = monster?.NextMove;

        Task<IReadOnlyList<BombOperation>> bombStartsTask = StartBombAttacksStaggered(armedBombs);
        Task bombsTask = CompleteBombOperations(bombStartsTask);
        if (yamotoKoki == null || yamotoKoki.IsDead || monster == null || scheduledMove == null)
        {
            await bombsTask;
            return;
        }

        IReadOnlyList<Creature> enemies = yamotoKoki.CombatState?.HittableEnemies ?? [];
        if (armedBombs.Count > 0)
        {
            await bombStartsTask;
            await Cmd.Wait(MoveAfterMissilesDelaySeconds);
            if (CombatManager.Instance.IsOverOrEnding || yamotoKoki.IsDead)
            {
                await bombsTask;
                return;
            }

            Flash();
            Task moveTask = PerformScheduledMove(
                yamotoKoki,
                scheduledMove,
                enemies,
                overlapIntentPresentation: true);
            await Task.WhenAll(bombsTask, moveTask);
        }
        else
        {
            await bombsTask;
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
        await AssignRandomIntent(yamotoKoki);
    }

    private static async Task<IReadOnlyList<BombOperation>> StartBombAttacksStaggered(
        IReadOnlyList<Creature> armedBombs)
    {
        List<BombOperation> operations = [];
        for (int i = 0; i < armedBombs.Count; i++)
        {
            if (i > 0)
            {
                await Cmd.Wait(MissileAttackIntervalSeconds);
            }

            if (CombatManager.Instance.IsOverOrEnding)
            {
                break;
            }

            Creature armedBomb = armedBombs[i];
            NCreature? bombNode = armedBomb.GetCreatureNode();
            if (armedBomb.Monster is YamotoKokiGasBomb bomb)
            {
                operations.Add(new BombOperation(
                    bombNode,
                    bomb.ExecuteExplosion(armedBomb)));
            }
        }

        return operations;
    }

    private static async Task CompleteBombOperations(
        Task<IReadOnlyList<BombOperation>> bombStartsTask)
    {
        IReadOnlyList<BombOperation> operations = await bombStartsTask;
        await Task.WhenAll(operations.Select(operation => operation.Explosion));
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

        MoveState next = YamotoKokiMonster.PickRandomMove(
            monster.MoveStateMachine!,
            yamotoKoki.PetOwner!.RunState.Rng.MonsterAi);
        await AssignIntent(yamotoKoki, next);
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
        await AssignIntent(yamotoKoki, next);
    }

    private static async Task AssignIntent(Creature yamotoKoki, MoveState next)
    {
        if (yamotoKoki.Monster is not YamotoKokiMonster monster)
        {
            return;
        }

        monster.SetMoveImmediate(next, forceTransition: true);

        NCreature? node = yamotoKoki.GetCreatureNode();
        ICombatState? combatState = yamotoKoki.CombatState;
        if (node != null && combatState != null && combatState.IsLiveCombat())
        {
            await node.UpdateIntent(combatState.HittableEnemies);
        }
    }

    private static Creature? FindLivingPartyYamotoKoki(IRunState runState)
    {
        foreach (Player player in runState.Players)
        {
            Creature? yamotoKoki = player.PlayerCombatState?.Pets.FirstOrDefault(
                pet => pet.Monster is YamotoKokiMonster && pet.IsAlive);
            if (yamotoKoki != null)
            {
                return yamotoKoki;
            }
        }

        return null;
    }
}
