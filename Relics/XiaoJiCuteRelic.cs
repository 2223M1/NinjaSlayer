using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Saves.Runs;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

public sealed class XiaoJiCuteRelic : NinjaSlayerRelicTemplate
{
    private const string CombatsKey = "Combats";
    private int _combatsLeft = 5;

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

    public override async Task BeforeCombatStart()
    {
        if (IsUsedUp)
        {
            return;
        }

        Flash();
        Creature xiaoJi = await PlayerCmd.AddPet<XiaoJiMonster>(Owner);
        await AssignRandomIntent(xiaoJi);
        CombatsLeft--;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player != Owner)
        {
            return;
        }

        Creature? xiaoJi = Owner.PlayerCombatState?.GetPet<XiaoJiMonster>();
        if (xiaoJi == null || xiaoJi.IsDead || xiaoJi.Monster is not XiaoJiMonster monster)
        {
            return;
        }

        Flash();
        IReadOnlyList<Creature> enemies = xiaoJi.CombatState?.HittableEnemies ?? [];
        NCreature? node = xiaoJi.GetCreatureNode();
        if (node != null)
        {
            await node.PerformIntent();
        }

        await monster.NextMove.PerformMove(enemies);
        monster.MoveStateMachine?.OnMovePerformed(monster.NextMove);
        await AssignRandomIntent(xiaoJi);
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
            if (pet.Monster is XiaoJiGasBomb bomb && pet.IsAlive)
            {
                await bomb.ExecuteExplosion(pet);
            }
        }
    }

    private static async Task AssignRandomIntent(Creature xiaoJi)
    {
        if (xiaoJi.Monster is not XiaoJiMonster monster)
        {
            return;
        }

        if (monster.MoveStateMachine == null)
        {
            monster.SetUpForCombat();
        }

        MoveState next = XiaoJiMonster.PickRandomMove(
            monster.MoveStateMachine!,
            xiaoJi.PetOwner!.RunState.Rng.MonsterAi);
        monster.SetMoveImmediate(next, forceTransition: true);

        NCreature? node = xiaoJi.GetCreatureNode();
        ICombatState? combatState = xiaoJi.CombatState;
        if (node != null && combatState != null && combatState.IsLiveCombat())
        {
            await node.UpdateIntent(combatState.HittableEnemies);
        }
    }
}
