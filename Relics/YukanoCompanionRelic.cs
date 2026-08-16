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
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Monsters;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Relics;

public sealed class YukanoCompanionRelic : NinjaSlayerRelicTemplate
{
    private const string CombatsKey = "Combats";
    private int _combatsLeft = YukanoCompanionRules.InitialCombats;

    public override RelicRarity Rarity => RelicRarity.Event;
    public override bool AddsPet => true;
    public override bool SpawnsPets => true;
    public override bool IsUsedUp => CombatsLeft <= 0;
    public override bool ShowCounter => true;
    public override int DisplayAmount => Math.Max(0, CombatsLeft);

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
            DynamicVars[CombatsKey].BaseValue = value;
            InvokeDisplayAmountChanged();
            Status = IsUsedUp ? RelicStatus.Disabled : RelicStatus.Normal;
        }
    }

    public override async Task BeforeCombatStart()
    {
        if (IsUsedUp || !YukanoCompanionPartyState.IsController(this))
        {
            return;
        }

        Flash();
        Creature? existing = YukanoCompanionPartyState.FindCompanion(
            Owner.RunState,
            livingOnly: true);
        bool created = existing == null;
        Creature yukano = existing ?? await PlayerCmd.AddPet<YukanoMonster>(Owner);
        YamotoKokiIntentGeneration generation = YamotoKokiIntentLifecycle.BeginCombat(yukano);
        if (yukano.Monster is YukanoMonster monster)
        {
            bool anyEnemyIntendsToAttack = yukano.CombatState?.HittableEnemies.Any(enemy =>
                enemy.IsAlive
                && enemy.IsHittable
                && enemy.Monster?.IntendsToAttack == true) == true;
            monster.SetOpeningMove(anyEnemyIntendsToAttack);
            await UpdateIntent(generation);
        }

        if (created)
        {
            _ = TaskHelper.RunSafely(
                YamotoKokiCombatAnimations.PlayEntrance(yukano, playVoice: false));
        }
    }

    public override Task AfterCombatEnd(CombatRoom room)
    {
        _ = room;
        if (IsUsedUp || IsMelted)
        {
            return Task.CompletedTask;
        }

        CombatsLeft = Math.Max(0, CombatsLeft - 1);
        if (YukanoCompanionPartyState.GetActiveRelicCount(Owner.RunState) != 0
            || YukanoCompanionPartyState.FindCompanion(
                Owner.RunState,
                livingOnly: false) is not { } yukano
            || yukano.IsDead)
        {
            return Task.CompletedTask;
        }

        YamotoKokiIntentLifecycle.Invalidate(yukano);
        _ = TaskHelper.RunSafely(YamotoKokiCombatAnimations.PlayFarewell(yukano));
        return Task.CompletedTask;
    }

    public override async Task AfterPlayerTurnStart(
        PlayerChoiceContext choiceContext,
        Player player)
    {
        _ = choiceContext;
        if (player != Owner || !YukanoCompanionPartyState.IsController(this))
        {
            return;
        }

        try
        {
            await PerformTurnAction();
        }
        catch (Exception ex)
        {
            Entry.Logger.Error(
                $"Yukano turn-start action failed; releasing the player turn instead of blocking card input: {ex}");
        }
    }

    private async Task PerformTurnAction()
    {
        Creature? yukano = YukanoCompanionPartyState.FindCompanion(
            Owner.RunState,
            livingOnly: true);
        if (yukano?.Monster is not YukanoMonster monster
            || monster.NextMove is not { } scheduledMove
            || yukano.CombatState is not { } combatState
            || !combatState.IsLiveCombat()
            || CombatManager.Instance.IsOverOrEnding)
        {
            return;
        }

        List<Creature> enemies = combatState.HittableEnemies
            .Where(enemy => enemy.IsAlive && enemy.IsHittable)
            .ToList();
        if (enemies.Count == 0)
        {
            YamotoKokiIntentLifecycle.Invalidate(yukano);
            return;
        }

        Flash();
        if (yukano.GetCreatureNode() is { } node)
        {
            await node.PerformIntent();
        }

        await scheduledMove.PerformMove(enemies);
        monster.MoveStateMachine?.OnMovePerformed(scheduledMove);
        if (CombatManager.Instance.IsOverOrEnding
            || !combatState.IsLiveCombat()
            || !yukano.IsAlive)
        {
            YamotoKokiIntentLifecycle.Invalidate(yukano);
            return;
        }

        List<Creature> remainingEnemies = combatState.HittableEnemies
            .Where(enemy => enemy.IsAlive && enemy.IsHittable)
            .ToList();
        if (remainingEnemies.Count == 0)
        {
            YamotoKokiIntentLifecycle.Invalidate(yukano);
            return;
        }

        monster.RollMove(remainingEnemies);
        await UpdateIntent(YamotoKokiIntentLifecycle.Capture(yukano), reveal: true);
    }

    private static async Task UpdateIntent(
        YamotoKokiIntentGeneration generation,
        bool reveal = false)
    {
        Creature yukano = generation.Creature;
        ICombatState? combatState = yukano.CombatState;
        NCreature? node = yukano.GetCreatureNode();
        if (combatState == null
            || node == null
            || !combatState.IsLiveCombat()
            || !YamotoKokiIntentLifecycle.PrepareContainerForWrite(generation))
        {
            return;
        }

        await (reveal
            ? node.RefreshIntents()
            : node.UpdateIntent(combatState.HittableEnemies));
        if (!YamotoKokiIntentLifecycle.IsCurrent(generation))
        {
            YamotoKokiIntentLifecycle.RehideIfInactive(generation);
        }
    }

}
