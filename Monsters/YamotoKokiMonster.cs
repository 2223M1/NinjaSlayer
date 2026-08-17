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
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using STS2RitsuLib.Scaffolding.Godot;

namespace NinjaSlayer.Monsters;

[RegisterMonster]
public sealed class YamotoKokiMonster : ModMonsterTemplate
{
    public const int SummonMissileCount = 2;
    public const string SummonMissileMoveId = "SUMMON_ORIGAMI_MISSILE";
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
        MoveState summon = new(SummonMissileMoveId, SummonMissileMove, new YamotoKokiSummonIntent());
        MoveState slash = new(
            IaiSlashMoveId,
            IaiSlashMove,
            new YamotoKokiIaiSlashIntent(() => GetIaiSlashDamage()));
        summon.FollowUpState = summon;
        slash.FollowUpState = slash;
        return new MonsterMoveStateMachine([summon, slash], summon);
    }

    public static MoveState PickRandomMove(MonsterMoveStateMachine machine, Rng rng)
    {
        string moveId = rng.NextBool() ? SummonMissileMoveId : IaiSlashMoveId;
        return (MoveState)machine.States[moveId];
    }

    public int GetIaiSlashDamage() => CompanionDamageMath.ScaleForActiveRelics(
        IaiSlashDamage,
        Creature.PetOwner is { } owner
            ? YamotoKokiPartyState.GetActiveRelicCount(owner.RunState)
            : 1);

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

    private async Task SummonMissileMove(IReadOnlyList<Creature> _)
    {
        if (Creature.PetOwner is not { } owner)
        {
            return;
        }

        YamotoKokiOrigamiMissileOrbitController? orbitController = NCombatRoom.Instance is { } room
            ? YamotoKokiOrigamiMissileOrbitController.Ensure(room)
            : null;
        orbitController?.BeginSpawnBatch(owner, SummonMissileCount);
        try
        {
            for (int i = 0; i < SummonMissileCount; i++)
            {
                await YamotoKokiCombatAnimations.PlaySummon(Creature, async () =>
                {
                    Creature missileCreature = await PlayerCmd.AddPet<YamotoKokiOrigamiMissile>(owner);
                    NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.YamotoKokiMissileSummonEvent);
                    if (missileCreature.Monster is YamotoKokiOrigamiMissile missile)
                    {
                        await missile.PrepareExplosionIntent(missileCreature);
                    }
                });
            }
        }
        finally
        {
            orbitController?.EndSpawnBatch(owner);
        }
    }

    private async Task IaiSlashMove(IReadOnlyList<Creature> _)
    {
        List<Creature> enemies = GetCurrentIaiTargets();
        if (enemies.Count == 0)
        {
            return;
        }

        NCreature? ownerNode = Creature.GetCreatureNode();
        NCreature? focusNode = ownerNode == null
            ? null
            : enemies
                .Select((enemy, index) => (Enemy: enemy, Index: index, Node: enemy.GetCreatureNode()))
                .Where(candidate => candidate.Node != null)
                .OrderBy(candidate => Math.Abs(
                    candidate.Node!.Visuals.Bounds.GetGlobalRect().GetCenter().X
                    - ownerNode.Visuals.Bounds.GetGlobalRect().GetCenter().X))
                .ThenBy(candidate => candidate.Index)
                .Select(candidate => candidate.Node)
                .FirstOrDefault();
        FinisherSession? finisher = null;
        if (ownerNode != null && focusNode != null)
        {
            FinisherEligibilityService.TryCreateYamotoKokiSession(
                Creature,
                ownerNode,
                focusNode,
                enemies,
                _ => GetIaiSlashDamage(),
                out finisher);
        }

        try
        {
            if (focusNode == null)
            {
                NinjaSlayerCombatVfx.PlayYamotoKokiIaiPetals(Creature);
                if (finisher != null)
                {
                    await finisher.Begin();
                }

                await PlayIaiImpact();
            }
            else
            {
                await YamotoKokiCombatAnimations.PlayIaiSlash(
                    Creature,
                    async () =>
                    {
                        NinjaSlayerCombatVfx.PlayYamotoKokiIaiPetals(Creature);
                        if (finisher != null)
                        {
                            await finisher.Begin();
                        }
                    },
                    PlayIaiImpact,
                    finisher == null
                        ? YamotoKokiIaiApproachMode.StandardLunge
                        : YamotoKokiIaiApproachMode.FinisherCloseRange);
            }

            if (finisher != null)
            {
                await finisher.CompleteAsync(
                    FinisherCompletionStatus.Succeeded,
                    FinisherCompletionMode.PlayPose);
            }
        }
        catch (Exception ex)
        {
            if (finisher != null)
            {
                await finisher.CompleteAsync(
                    FinisherCompletionStatus.Faulted,
                    FinisherCompletionMode.CommitWithoutPose,
                    ex.Message);
            }

            throw;
        }
    }

    private async Task PlayIaiImpact()
    {
        List<Creature> enemies = GetCurrentIaiTargets();
        if (enemies.Count == 0)
        {
            return;
        }

        List<Creature> connectedTargets = enemies
            .Where(target => target.GetPower<EvasionPower>() is not { } evasion
                || !evasion.CanEvade(target, ValueProp.Move, Creature))
            .ToList();
        if (connectedTargets.Count > 0)
        {
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.YamotoKokiFastAttackEvent);
            NinjaSlayerCombatVfx.PlayYamotoKokiIaiImpact(connectedTargets);
        }

        await CreatureCmd.Damage(
            new ThrowingPlayerChoiceContext(),
            enemies,
            GetIaiSlashDamage(),
            ValueProp.Move,
            Creature);
    }

    private List<Creature> GetCurrentIaiTargets()
    {
        if (Creature.CombatState is not { } combatState || !combatState.IsLiveCombat())
        {
            return [];
        }

        return combatState.HittableEnemies
            .Where(enemy => enemy.IsAlive
                && enemy.IsHittable
                && ReferenceEquals(enemy.CombatState, combatState))
            .ToList();
    }
}
