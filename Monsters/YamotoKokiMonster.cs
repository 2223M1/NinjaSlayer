using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using STS2RitsuLib.Scaffolding.Godot;

namespace NinjaSlayer.Monsters;

[RegisterMonster]
public sealed class YamotoKokiMonster : ModMonsterTemplate
{
    public const int SummonBombCount = 2;
    public const string SummonBombMoveId = "SUMMON_BOMB";
    public const string IaiSlashMoveId = "IAI_SLASH";
    public const int IaiSlashDamage = 6;

    public override int MinInitialHp => 9999;
    public override int MaxInitialHp => 9999;
    public override bool IsHealthBarVisible => false;

    protected override string VisualsPath =>
        "res://NinjaSlayer/scenes/creature_visuals/yamoto_koki.tscn";

    protected override NCreatureVisuals? TryCreateCreatureVisuals() =>
        RitsuGodotNodeFactories.CreateFromScenePath<NCreatureVisuals>(VisualsPath);

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        MoveState summon = new(SummonBombMoveId, SummonBombMove, new SummonIntent());
        MoveState slash = new(
            IaiSlashMoveId,
            IaiSlashMove,
            new SingleAttackIntent(() => GetIaiSlashDamage()));
        summon.FollowUpState = summon;
        slash.FollowUpState = slash;
        return new MonsterMoveStateMachine([summon, slash], summon);
    }

    public static MoveState PickRandomMove(MonsterMoveStateMachine machine, Rng rng)
    {
        string moveId = rng.NextBool() ? SummonBombMoveId : IaiSlashMoveId;
        return (MoveState)machine.States[moveId];
    }

    public int GetIaiSlashDamage() => YamotoKokiDamageMath.ScaleForParty(
        IaiSlashDamage,
        Creature.PetOwner?.RunState.Players.Count ?? 1);

    public override bool TryModifyPowerAmountReceived(
        PowerModel canonicalPower,
        Creature target,
        decimal amount,
        Creature? applier,
        out decimal modifiedAmount)
    {
        modifiedAmount = amount;
        if (target != Creature
            || applier == null
            || applier.Side == target.Side
            || canonicalPower.GetTypeForAmount(amount) != PowerType.Debuff)
        {
            return false;
        }

        modifiedAmount = 0m;
        return true;
    }

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        NinjaSlayerCombatVfx.PreloadYamotoKokiIaiFeedback();
    }

    private async Task SummonBombMove(IReadOnlyList<Creature> _)
    {
        if (Creature.PetOwner is not { } owner)
        {
            return;
        }

        YamotoKokiBombOrbitController? orbitController = NCombatRoom.Instance is { } room
            ? YamotoKokiBombOrbitController.Ensure(room)
            : null;
        orbitController?.BeginSpawnBatch(owner, SummonBombCount);
        try
        {
            for (int i = 0; i < SummonBombCount; i++)
            {
                await YamotoKokiCombatAnimations.PlaySummon(Creature, async () =>
                {
                    Creature bombCreature = await PlayerCmd.AddPet<YamotoKokiGasBomb>(owner);
                    NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.YamotoKokiMissileSummonEvent);
                    if (bombCreature.Monster is YamotoKokiGasBomb bomb)
                    {
                        await bomb.PrepareExplosionIntent(bombCreature);
                    }
                });
            }
        }
        finally
        {
            orbitController?.EndSpawnBatch(owner);
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

        NinjaSlayerCombatVfx.PlayYamotoKokiIaiPetals(Creature);
        await CreatureCmd.TriggerAnim(Creature, "SlowAttack", 0.25f);
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.YamotoKokiFastAttackEvent);
        NinjaSlayerCombatVfx.PlayYamotoKokiIaiImpact(enemies);
        await CreatureCmd.Damage(
            new ThrowingPlayerChoiceContext(),
            enemies,
            GetIaiSlashDamage(),
            ValueProp.Move,
            Creature);
    }
}
