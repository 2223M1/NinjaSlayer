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
using MegaCrit.Sts2.Core.Saves.Runs;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

public sealed class YamotoKokiCuteRelic : NinjaSlayerRelicTemplate
{
    private const string CombatsKey = "Combats";
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
        NinjaSlayerRelicAssets.FromCardImage("StrikeNinjaSlayer");

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
        Creature yamotoKoki = await PlayerCmd.AddPet<YamotoKokiMonster>(Owner);
        await AssignRandomIntent(yamotoKoki);
        if (!HasPlayedEntrance)
        {
            HasPlayedEntrance = true;
            await YamotoKokiCombatAnimations.PlayEntrance(yamotoKoki);
        }

        CombatsLeft--;
    }

    public override async Task AfterCombatEnd(CombatRoom room)
    {
        if (!IsUsedUp || HasPlayedFarewell)
        {
            return;
        }

        Creature? yamotoKoki = Owner.PlayerCombatState?.GetPet<YamotoKokiMonster>();
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

        Creature? yamotoKoki = Owner.PlayerCombatState?.GetPet<YamotoKokiMonster>();
        if (yamotoKoki == null || yamotoKoki.IsDead || yamotoKoki.Monster is not YamotoKokiMonster monster)
        {
            return;
        }

        Flash();
        IReadOnlyList<Creature> enemies = yamotoKoki.CombatState?.HittableEnemies ?? [];
        NCreature? node = yamotoKoki.GetCreatureNode();
        if (node != null)
        {
            await node.PerformIntent();
        }

        await monster.NextMove.PerformMove(enemies);
        monster.MoveStateMachine?.OnMovePerformed(monster.NextMove);
        await AssignRandomIntent(yamotoKoki);
    }

    public override async Task BeforeSideTurnStart(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (side != CombatSide.Enemy || Owner.PlayerCombatState == null)
        {
            return;
        }

        foreach (Creature pet in Owner.PlayerCombatState.Pets.ToList())
        {
            if (pet.Monster is YamotoKokiGasBomb bomb && pet.IsAlive)
            {
                await bomb.ExecuteExplosion(pet);
            }
        }
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
        monster.SetMoveImmediate(next, forceTransition: true);

        NCreature? node = yamotoKoki.GetCreatureNode();
        ICombatState? combatState = yamotoKoki.CombatState;
        if (node != null && combatState != null && combatState.IsLiveCombat())
        {
            await node.UpdateIntent(combatState.HittableEnemies);
        }
    }
}
