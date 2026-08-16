using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using STS2RitsuLib.Scaffolding.Godot;

namespace NinjaSlayer.Monsters;

[RegisterMonster]
public sealed class SawatariMonster : ModMonsterTemplate
{
    public const string EnhanceMoveId = "BAMBOO_ENHANCEMENT";
    public const string AttackMoveId = "EMPTY_HAND_COMBO";
    public const string TexturePath = "res://NinjaSlayer/images/monsters/sawatari_bamboo.png";
    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(
        AscensionLevel.ToughEnemies,
        SawatariEventRules.ToughHp,
        SawatariEventRules.BaseHp);

    public override int MaxInitialHp => MinInitialHp;
    public override bool IsHealthBarVisible => false;

    protected override string VisualsPath =>
        "res://NinjaSlayer/scenes/creature_visuals/sawatari.tscn";

    protected override NCreatureVisuals? TryCreateCreatureVisuals() =>
        RitsuGodotNodeFactories.CreateFromScenePath<NCreatureVisuals>(VisualsPath);

    public override IEnumerable<string> AssetPaths => base.AssetPaths.Append(TexturePath);

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        MoveState enhance = new(EnhanceMoveId, EnhanceMove, new BuffIntent());
        MoveState attack = new(
            AttackMoveId,
            AttackMove,
            new MultiAttackIntent(SawatariEventRules.AttackDamage, SawatariEventRules.AttackHits));
        enhance.FollowUpState = attack;
        attack.FollowUpState = attack;
        return new MonsterMoveStateMachine([enhance, attack], enhance);
    }

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        SetFacingPlayerSide(Creature.Side == CombatSide.Player);
        if (Creature.Side == CombatSide.Player
            && SawatariEventSession.TryGet(Creature.CombatState, out SawatariEventSession? session))
        {
            await session.PlayNinjaSlayerEntrance();
        }
    }

    public override Task BeforeDeath(Creature creature)
    {
        if (SawatariEventSession.TryGet(Creature.CombatState, out SawatariEventSession? session))
        {
            session.CaptureDyingCreature(creature);
        }

        return Task.CompletedTask;
    }

    public override Task AfterDeath(
        PlayerChoiceContext choiceContext,
        Creature creature,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        if (!wasRemovalPrevented
            && SawatariEventSession.TryGet(Creature.CombatState, out SawatariEventSession? session))
        {
            session.ObserveDeath(creature, deathAnimLength);
        }

        return Task.CompletedTask;
    }

    public override Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (ReferenceEquals(target, Creature) && result.UnblockedDamage > 0)
        {
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.ForestSawatariHurtEvent);
        }

        return Task.CompletedTask;
    }

    public override Task AfterSideTurnStart(
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState) =>
        SawatariEventSession.TryGet(combatState, out SawatariEventSession? session)
            ? session.PlaySupportTurn(this, side)
            : Task.CompletedTask;

    public override bool ShouldCreatureBeRemovedFromCombatAfterDeath(Creature creature) =>
        !ReferenceEquals(creature, Creature)
        || !SawatariEventSession.IsActiveDuelCreature(creature);

    internal async Task PlayAttack(Creature target)
    {
        Creature attacker = Creature;
        ICombatState? combatState = attacker.CombatState;
        if (combatState == null || !CanHit(attacker, target, combatState))
        {
            return;
        }

        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.ForestSawatariAttackEvent);
        AttackCommand command = MegaCrit.Sts2.Core.Commands.DamageCmd
            .Attack(SawatariEventRules.AttackDamage)
            .WithHitCount(SawatariEventRules.AttackHits)
            .FromMonster(this);
        int hitCount = (int)Math.Ceiling(Math.Max(
            0m,
            Hook.ModifyAttackHitCount(combatState, command, SawatariEventRules.AttackHits)));
        var choiceContext = new BlockingPlayerChoiceContext();
        var results = new List<DamageResult>();

        await Hook.BeforeAttack(combatState, command);
        try
        {
            await SlowAttackAnimation.PlayCombo(
                attacker,
                hitCount,
                CombatActionTimingRuntime.SlowAttackSeconds,
                CombatActionTimingRuntime.ConsecutiveAttackSeconds,
                CombatActionTimingRuntime.DamageRecoverySeconds,
                async () =>
                {
                    if (!CanHit(attacker, target, combatState))
                    {
                        return;
                    }

                    bool connects = target.GetPower<EvasionPower>() is not { } evasion
                        || !evasion.CanEvade(target, command.DamageProps, attacker);
                    if (connects)
                    {
                        NinjaSlayerCombatVfx.PlayDefectStrikeHitFx(target);
                    }

                    using (CombatPresentationPacingScope.Begin(CombatPresentationPacingPolicy.ComboDamage))
                    {
                        results.AddRange(await GameCompatibility.Damage.Deal(
                            choiceContext,
                            [target],
                            SawatariEventRules.AttackDamage,
                            command.DamageProps,
                            attacker,
                            null,
                            null));
                    }
                });
        }
        finally
        {
            if (results.Count > 0)
            {
                command.AddResultsInternal(results);
            }

            try
            {
                CombatManager.Instance.History.CreatureAttacked(combatState, attacker, results);
            }
            finally
            {
                await Hook.AfterAttack(combatState, choiceContext, command);
            }
        }
    }

    private async Task AttackMove(IReadOnlyList<Creature> targets)
    {
        if (!SawatariEventSession.TryGet(Creature.CombatState, out SawatariEventSession? session))
        {
            return;
        }

        Creature[] candidates = targets
            .Where(target => target.IsAlive && target.IsHittable)
            .ToArray();
        if (candidates.Length > 0)
        {
            Creature? target = Creature.CombatState?.RunState.Rng.CombatTargets.NextItem(candidates);
            if (target != null)
            {
                await PlayAttack(target);
                if (session.ConsumeBambooVoiceAfterAttack(Creature))
                {
                    NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.ForestSawatariBambooEvent);
                }
            }
        }
    }

    private async Task EnhanceMove(IReadOnlyList<Creature> _)
    {
        if (!SawatariEventSession.IsActiveDuelCreature(Creature))
        {
            return;
        }

        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.ForestSawatariEnhancedEvent);
        await PowerCmd.Apply<StrengthPower>(
            new ThrowingPlayerChoiceContext(),
            Creature,
            SawatariEventRules.DuelStrength,
            Creature,
            null);
    }

    private static bool CanHit(Creature attacker, Creature target, ICombatState combatState) =>
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

    private void SetFacingPlayerSide(bool playerSide)
    {
        var visuals = NCombatRoom.Instance?.GetCreatureNode(Creature)?.Visuals;
        Sprite2D? body = NinjaSlayerVisualRig.GetBodySprite(visuals);
        if (body != null)
        {
            body.FlipH = playerSide;
        }

        if (NinjaSlayerVisualRig.GetShadow(visuals) is { } shadow)
        {
            shadow.FlipH = playerSide;
        }
    }
}
