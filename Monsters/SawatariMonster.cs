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
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using STS2RitsuLib.Scaffolding.Godot;

namespace NinjaSlayer.Monsters;

[RegisterMonster]
public sealed partial class SawatariMonster : ModMonsterTemplate
{
    public const string EnhanceMoveId = "BAMBOO_ENHANCEMENT";
    public const string AttackMoveId = "EMPTY_HAND_COMBO";
    public const string SecondAttackMoveId = "SECOND_BAMBOO_COMBO";
    public const string TexturePath = "res://NinjaSlayer/images/monsters/sawatari/body.png";
    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(
        AscensionLevel.ToughEnemies,
        ActThree ? 300 : SawatariEventRules.ToughHp,
        ActThree ? 280 : SawatariEventRules.BaseHp);

    private int BambooDamage => ActThree
        ? AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 4, 3)
        : SawatariEventRules.AttackDamage;

    public override int MaxInitialHp => MinInitialHp;
    public override bool IsHealthBarVisible => false;
    public override string DeathSfx => NinjaSlayerAudio.ForestSawatariDeathEvent;

    protected override string VisualsPath =>
        "res://NinjaSlayer/scenes/creature_visuals/sawatari.tscn";

    protected override NCreatureVisuals? TryCreateCreatureVisuals() =>
        RitsuGodotNodeFactories.CreateFromScenePath<NCreatureVisuals>(VisualsPath);

    public override IEnumerable<string> AssetPaths => base.AssetPaths.Append(TexturePath)
        .Concat(SawatariWeaponVisuals.AssetPaths);

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        // Canonical models enumerate intents while preloading, before any creature exists.
        if (IsMutable && Creature.Side == CombatSide.Player)
        {
            MoveState support = new(ActThree ? DualMoveId : AttackMoveId, SupportMove,
                new MultiAttackIntent(ActThree ? DualDamage : BambooDamage, ActThree ? 2 : SawatariEventRules.AttackHits));
            support.FollowUpState = support;
            return new MonsterMoveStateMachine([support], support);
        }
        if (ActThree) return GenerateMacheteMoves();
        MoveState enhance = new(EnhanceMoveId, ArrowMove, new SingleAttackIntent(ArrowDamage), new BuffIntent());
        MoveState attack = new(
            AttackMoveId,
            AttackMove,
            new MultiAttackIntent(BambooDamage, SawatariEventRules.AttackHits), new BuffIntent());
        MoveState secondAttack = new(SecondAttackMoveId, AttackMove,
            new MultiAttackIntent(BambooDamage, SawatariEventRules.AttackHits), new BuffIntent());
        enhance.FollowUpState = attack;
        attack.FollowUpState = secondAttack;
        secondAttack.FollowUpState = enhance;
        return new MonsterMoveStateMachine([enhance, attack, secondAttack], enhance);
    }

    public override Task BeforeCombatStart()
    {
        if (Creature.Side == CombatSide.Player)
            SetMoveImmediate((MoveState)MoveStateMachine!.States[ActThree ? DualMoveId : AttackMoveId], true);
        return Task.CompletedTask;
    }

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        SetFacingPlayerSide(Creature.Side == CombatSide.Player);
        SawatariWeaponVisuals.Create(this);
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
        if (!wasRemovalPrevented && ReferenceEquals(creature, Creature))
            SawatariWeaponVisuals.Get(Creature)?.Refresh();
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
        ICombatState combatState)
    {
        if (side == CombatSide.Enemy && Creature.Side == CombatSide.Player)
            NinjaSlayerRapidAnimationCoordinator.CancelAndRestore(Creature);
        SawatariWeaponVisuals.Get(Creature)?.Refresh();
        return SawatariEventSession.TryGet(combatState, out SawatariEventSession? session)
            ? session.PlaySupportTurn(this, side)
            : Task.CompletedTask;
    }

    public override bool ShouldCreatureBeRemovedFromCombatAfterDeath(Creature creature) =>
        !ReferenceEquals(creature, Creature)
        || !SawatariEventSession.IsActiveDuelCreature(creature);

    private async Task SupportMove(IReadOnlyList<Creature> targets)
    {
        Creature[] candidates = targets.Where(target => CanHit(Creature, target, CombatState)).ToArray();
        if (candidates.Length == 0) return;
        Creature target = CombatState.RunState.Rng.CombatTargets.NextItem(candidates)!;
        if (ActThree) await PlayDualAttack(target);
        else await PlayAttack(target);
    }

    internal async Task PlayAttack(Creature target)
    {
        Creature attacker = Creature;
        ICombatState? combatState = attacker.CombatState;
        if (combatState == null || !CanHit(attacker, target, combatState))
        {
            return;
        }

        SawatariWeaponVisuals.Get(attacker)?.ShowBamboo();

        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.ForestSawatariAttackEvent);
        AttackCommand command = MegaCrit.Sts2.Core.Commands.DamageCmd
            .Attack(BambooDamage)
            .WithHitCount(SawatariEventRules.AttackHits)
            .FromMonster(this);
        int hitCount = (int)Math.Ceiling(Math.Max(
            0m,
            Hook.ModifyAttackHitCount(combatState, command, SawatariEventRules.AttackHits)));
        var choiceContext = new BlockingPlayerChoiceContext();
        var results = new List<DamageResult>();

        await Hook.BeforeAttack(combatState, command);
        await using FinisherSession? finisher = FinisherEligibilityService.CreateCompanionSession(attacker,
            new FinisherActionForecastDescriptor(_ => BambooDamage, command.DamageProps, hitCount,
                FinisherTargeting.Single, SingleTarget: target));
        finisher?.Begin();
        FinisherApproach? approach = null;
        if (FinisherAttackCommandAdapter.PredictReverseVictim(command, [target], BambooDamage, hitCount)
            ?.GetCreatureNode() is { } focus && attacker.GetCreatureNode() is { } actorNode)
        {
            approach = FinisherApproach.Create(actorNode, focus, Godot.Vector2.One);
            approach.Start(CombatActionTimingRuntime.VisualSeconds(SawatariBambooAnimation.CycleSeconds * SawatariBambooAnimation.PeakPhase));
        }
        try
        {
            await SawatariBambooAnimation.Play(
                attacker,
                hitCount,
                async () =>
                {
                    if (!CanHit(attacker, target, combatState))
                    {
                        return;
                    }

                    bool connects = target.GetPower<EvasionPower>() is not { } evasion
                        || !evasion.CanEvade(target, command.DamageProps, attacker);
                    FinisherApproach.ReachImpact(attacker);
                    NinjaSlayerCombatVfx.PlaySawatariBambooHit(target, connects);

                    using (CombatPresentationPacingScope.Begin(CombatPresentationPacingPolicy.ComboDamage))
                    {
                        results.AddRange(await CreatureCmd.Damage(
                            choiceContext,
                            [target],
                            BambooDamage,
                            command.DamageProps,
                            attacker,
                            null
#if !NINJASLAYER_LEGACY_DAMAGE_API
                            , null
#endif
                        ));
                    }
                });
        }
        finally
        {
            approach?.ReleasePrediction();
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
        if (finisher != null) await finisher.CompleteAsync(playPose: true);
    }

    private async Task AttackMove(IReadOnlyList<Creature> targets)
    {
        Creature[] candidates = targets
            .Where(target => target.IsAlive && target.IsHittable)
            .ToArray();
        if (candidates.Length > 0)
        {
            Creature? target = Creature.CombatState?.RunState.Rng.CombatTargets.NextItem(candidates);
            if (target != null)
            {
                await PlayAttack(target);
                if (Creature.IsAlive)
                    await PowerCmd.Apply<StrengthPower>(new BlockingPlayerChoiceContext(), Creature,
                        ActThree ? 4 : 1, Creature, null);
                if (SawatariEventSession.TryGet(Creature.CombatState, out SawatariEventSession? session)
                    && session.ConsumeBambooVoiceAfterAttack(Creature))
                {
                    NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.ForestSawatariBambooEvent);
                }
            }
        }
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

        visuals?.GetNode<NinjaSlayerShadowController>(NinjaSlayerVisualRig.ShadowControllerNodeName).SetMirrored(playerSide);
    }
}
