using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

internal enum FinisherTargeting
{
    Single,
    All,
    Random,
    Fixed
}

internal sealed record FinisherAttackSpec(
    CardModel Card,
    CardPlay CardPlay,
    FinisherForecastDescriptor Forecast)
{
    public static FinisherAttackSpec FromCard(
        CardModel card,
        CardPlay cardPlay,
        decimal? damageOverride = null,
        int? hitCountOverride = null,
        ValueProp? propsOverride = null)
    {
        Func<Creature, decimal> damage;
        ValueProp props;
        if (damageOverride.HasValue)
        {
            damage = _ => damageOverride.Value;
            props = propsOverride ?? ResolveProps(card);
        }
        else if (card.DynamicVars.TryGetValue(CalculatedDamageVar.defaultName, out DynamicVar? calculated)
            && calculated is CalculatedDamageVar calculatedDamage)
        {
            damage = target => calculatedDamage.Calculate(target);
            props = calculatedDamage.Props;
        }
        else
        {
            DamageVar damageVar = card.DynamicVars.Damage;
            damage = _ => damageVar.BaseValue;
            props = damageVar.Props;
        }

        FinisherTargeting targeting = card.TargetType switch
        {
            TargetType.AllEnemies => FinisherTargeting.All,
            TargetType.RandomEnemy => FinisherTargeting.Random,
            _ => FinisherTargeting.Single
        };
        int hitCount = hitCountOverride
            ?? (VanillaHitPreviewCompatibility.TryGetHitCount(card, cardPlay.Target, out int resolvedHits) ? resolvedHits : 0);
        return new FinisherAttackSpec(
            card,
            cardPlay,
            new FinisherForecastDescriptor(
                damage,
                propsOverride ?? props,
                Math.Max(1, hitCount),
                targeting,
                cardPlay.Target));
    }

    private static ValueProp ResolveProps(CardModel card)
    {
        return card.DynamicVars.TryGetValue(DamageVar.defaultName, out DynamicVar? damage)
            && damage is DamageVar damageVar
            ? damageVar.Props
            : ValueProp.Move;
    }
}

internal static class NinjaSlayerFinisherCinematic
{
    private static readonly AsyncLocal<CommandBypassFrame?> CommandBypass = new();
    private static readonly AsyncLocal<int> DirectDamageBypassDepth = new();

    internal static bool TryInterceptRangedDamage(
        PlayerChoiceContext choiceContext, IEnumerable<Creature>? targets, decimal amount,
        ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay,
        out Task<IEnumerable<DamageResult>>? result)
    {
        result = null;
        if (DirectDamageBypassDepth.Value > 0 || FinisherRangedAction.For(dealer) is not { } ranged
            || ranged.Source != cardSource)
            return false;
        Creature[] actualTargets = targets?.ToArray() ?? [];
        FinisherSession? ownedSession = null;
        if (ranged.Session == null)
        {
            ownedSession = FinisherEligibilityService.CreateActionSession(dealer!,
                new FinisherActionForecastDescriptor(_ => amount, props, 1, FinisherTargeting.Fixed,
                    CardSource: cardSource, CardPlay: cardPlay, TriggersKarate: false,
                    FixedTargets: actualTargets));
            ownedSession?.Begin();
        }
        if (ranged.Session == null) return false;
        result = Execute();
        return true;

        async Task<IEnumerable<DamageResult>> Execute()
        {
            await using FinisherSession? session = ownedSession;
            await ranged.WaitForImpact();
            if (!ReferenceEquals(dealer!.CombatState, ranged.Session!.CombatState)
                || !ReferenceEquals(MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance, ranged.Session.Room)
                || ranged.Session.Completion.IsCompleted)
                throw new OperationCanceledException("The ranged impact's combat ended before projectile arrival.");
            ranged.Session!.NotifyPrimaryDamage(dealer, cardSource, cardPlay, rangedImpact: true);
            IEnumerable<DamageResult> results = await ExecuteOriginalDirectDamage(
                choiceContext, actualTargets, amount, props, dealer, cardSource, cardPlay);
            if (session != null) await session.CompleteAsync(playPose: true);
            return results;
        }
    }

    public static bool IsMovementOwned(Creature creature) =>
        FinisherSessionRegistry.GetActiveSession() is { IsRanged: false } session && session.Actor == creature;

    internal static bool TryPlayOwnedAction(
        Creature creature,
        float repeatWaitSeconds,
        out Task action)
    {
        FinisherSession? session = FinisherSessionRegistry.GetActiveSession();
        if (session?.Actor != creature || session.IsRanged)
        {
            action = Task.CompletedTask;
            return false;
        }

        action = session.PlayActionToPeak(creature, repeatWaitSeconds);
        return true;
    }

    internal static Task WaitForOwnedActionPeak(Creature creature)
    {
        FinisherSession? session = FinisherSessionRegistry.GetActiveSession();
        return session?.Actor == creature ? session.EnsureActionPeak() : Task.CompletedTask;
    }

    internal static bool TryInterceptAttackCommand(
        AttackCommand command,
        PlayerChoiceContext? choiceContext,
        out Task<AttackCommand>? result)
    {
        result = null;
        if (IsCommandBypassed(command)
            || !FinisherAttackCommandAdapter.TryCreateSpec(command, out FinisherAttackSpec? spec))
        {
            return false;
        }

        result = ExecuteCommandWithFinisher(
            command,
            choiceContext ?? new BlockingPlayerChoiceContext(),
            spec,
            "generic-command");
        return true;
    }

    internal static bool TryInterceptDirectDamage(
        PlayerChoiceContext choiceContext,
        IEnumerable<Creature>? targets,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay,
        out Task<IEnumerable<DamageResult>>? result)
    {
        result = null;
        if (DirectDamageBypassDepth.Value > 0
            || dealer?.Player?.Character is not INinjaSlayerCharacter
            || cardSource?.Type != CardType.Attack
            || cardPlay == null
            || cardSource.Owner?.Creature != dealer)
        {
            return false;
        }

        List<Creature> targetList = targets?.Where(target => target.IsAlive).Distinct().ToList() ?? [];
        if (targetList.Count == 0)
        {
            return false;
        }

        var spec = new FinisherAttackSpec(
            cardSource,
            cardPlay,
            new FinisherForecastDescriptor(
                _ => amount,
                props,
                1,
                FinisherTargeting.Fixed,
                FixedTargets: targetList));
        result = ExecuteDirectDamageWithFinisher(
            choiceContext,
            spec,
            () => ExecuteOriginalDirectDamage(
                choiceContext,
                targetList,
                amount,
                props,
                dealer,
                cardSource,
                cardPlay));
        return true;
    }

    public static async Task<AttackCommand> ExecuteWithFinisher(
        AttackCommand command,
        PlayerChoiceContext choiceContext,
        CardModel card,
        CardPlay cardPlay,
        decimal? damageOverride = null,
        int? hitCountOverride = null)
    {
        FinisherAttackSpec spec = FinisherAttackSpec.FromCard(
            card,
            cardPlay,
            damageOverride,
            hitCountOverride);
        return await ExecuteCommandWithFinisher(command, choiceContext, spec, "explicit-command");
    }

    private static async Task<AttackCommand> ExecuteCommandWithFinisher(
        AttackCommand command,
        PlayerChoiceContext choiceContext,
        FinisherAttackSpec spec,
        string entryPoint)
    {
        using FinisherRangedAction? ranged = FinisherRangedAction.IsRangedCard(spec.Card)
            ? FinisherRangedAction.Begin(spec.Card.Owner.Creature, spec.Card) : null;
        if (!FinisherEligibilityService.TryCreateSession(
                spec,
                command,
                entryPoint,
                out FinisherSession? session))
        {
            return await ExecuteOriginalCommand(command, choiceContext);
        }

        AttackCommand result;
        try
        {
            session.Begin();
            result = await ExecuteOriginalCommand(command, choiceContext);
            if (session.RequiresAfterCardPlayed)
            {
                FinisherSessionRegistry.TransferToAfterCardPlayed(session);
                return result;
            }
        }
        catch (Exception originalFailure)
        {
            try
            {
                await session.CompleteAsync(playPose: false);
            }
            catch (Exception completionFailure)
            {
                throw new AggregateException(
                    "Finisher card execution and cleanup both failed.",
                    originalFailure,
                    completionFailure);
            }

            throw;
        }

        await session.CompleteAsync(playPose: true);
        return result;
    }

    public static async Task ExecuteSequenceWithFinisher(
        PlayerChoiceContext choiceContext,
        FinisherAttackSpec spec,
        Func<Task> sequence)
    {
        if (!FinisherEligibilityService.TryCreateSession(
                spec,
                command: null,
                entryPoint: "explicit-sequence",
                out FinisherSession? session))
        {
            await sequence();
            return;
        }

        try
        {
            session.Begin();
            await sequence();
            if (session.RequiresAfterCardPlayed)
            {
                FinisherSessionRegistry.TransferToAfterCardPlayed(session);
                return;
            }
        }
        catch (Exception originalFailure)
        {
            try
            {
                await session.CompleteAsync(playPose: false);
            }
            catch (Exception completionFailure)
            {
                throw new AggregateException(
                    "Finisher sequence and cleanup both failed.",
                    originalFailure,
                    completionFailure);
            }

            throw;
        }

        await session.CompleteAsync(playPose: true);
    }

    public static async Task ExecuteDirectWithFinisher(
        PlayerChoiceContext choiceContext,
        FinisherAttackSpec spec,
        Func<Task> damageAction)
    {
        await ExecuteDirectWithFinisher(choiceContext, spec, damageAction, "explicit-direct");
    }

    private static async Task ExecuteDirectWithFinisher(
        PlayerChoiceContext choiceContext,
        FinisherAttackSpec spec,
        Func<Task> damageAction,
        string entryPoint)
    {
        if (!FinisherEligibilityService.TryCreateSession(
                spec,
                command: null,
                entryPoint: entryPoint,
                out FinisherSession? session))
        {
            await damageAction();
            return;
        }

        try
        {
            session.Begin();
            await damageAction();
            if (session.RequiresAfterCardPlayed)
            {
                FinisherSessionRegistry.TransferToAfterCardPlayed(session);
                return;
            }
        }
        catch (Exception originalFailure)
        {
            try
            {
                await session.CompleteAsync(playPose: false);
            }
            catch (Exception completionFailure)
            {
                throw new AggregateException(
                    "Finisher direct damage and cleanup both failed.",
                    originalFailure,
                    completionFailure);
            }

            throw;
        }

        await session.CompleteAsync(playPose: true);
    }

    private static async Task<IEnumerable<DamageResult>> ExecuteDirectDamageWithFinisher(
        PlayerChoiceContext choiceContext,
        FinisherAttackSpec spec,
        Func<Task<IEnumerable<DamageResult>>> damageAction)
    {
        IEnumerable<DamageResult> results = [];
        await ExecuteDirectWithFinisher(
            choiceContext,
            spec,
            async () => results = await damageAction(),
            "direct-damage");
        return results;
    }

    private static bool IsCommandBypassed(AttackCommand command)
    {
        for (CommandBypassFrame? frame = CommandBypass.Value; frame != null; frame = frame.Parent)
        {
            if (ReferenceEquals(frame.Command, command))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<AttackCommand> ExecuteOriginalCommand(
        AttackCommand command,
        PlayerChoiceContext choiceContext)
    {
        CommandBypassFrame? previous = CommandBypass.Value;
        CommandBypass.Value = new CommandBypassFrame(command, previous);
        try
        {
            return await command.Execute(choiceContext);
        }
        finally
        {
            CommandBypass.Value = previous;
        }
    }

    private static async Task<IEnumerable<DamageResult>> ExecuteOriginalDirectDamage(
        PlayerChoiceContext choiceContext,
        IEnumerable<Creature> targets,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay)
    {
        DirectDamageBypassDepth.Value++;
        try
        {
            return await CreatureCmd.Damage(
                choiceContext,
                targets,
                amount,
                props,
                dealer,
                cardSource
#if !NINJASLAYER_LEGACY_DAMAGE_API
                , cardPlay
#endif
            );
        }
        finally
        {
            DirectDamageBypassDepth.Value--;
        }
    }

    private sealed record CommandBypassFrame(AttackCommand Command, CommandBypassFrame? Parent);
}
