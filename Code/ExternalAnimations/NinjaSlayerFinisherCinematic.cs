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

public enum FinisherTargeting
{
    Single,
    All,
    Random,
    Fixed
}

internal enum FinisherPresentationMode
{
    Legacy,
    Enhanced
}

public sealed record FinisherAttackSpec(
    CardModel Card,
    CardPlay CardPlay,
    Func<Creature, decimal> Damage,
    ValueProp Props,
    int HitCount,
    FinisherTargeting Targeting,
    Creature? SingleTarget = null,
    IReadOnlyList<Creature>? FixedTargets = null)
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
            ?? (HitPreviewResolver.TryResolve(card, cardPlay.Target, out int resolvedHits) ? resolvedHits : 0);
        return new FinisherAttackSpec(
            card,
            cardPlay,
            damage,
            propsOverride ?? props,
            Math.Max(1, hitCount),
            targeting,
            cardPlay.Target);
    }

    private static ValueProp ResolveProps(CardModel card)
    {
        return card.DynamicVars.TryGetValue(DamageVar.defaultName, out DynamicVar? damage)
            && damage is DamageVar damageVar
            ? damageVar.Props
            : ValueProp.Move;
    }
}

public static class NinjaSlayerFinisherCinematic
{
    private static readonly AsyncLocal<CommandBypassFrame?> CommandBypass = new();
    private static readonly AsyncLocal<int> DirectDamageBypassDepth = new();

    public static bool IsMovementOwned(Creature creature) => FinisherSessionRegistry.GetActiveSession()?.Owner == creature;

    internal static void TryProtectLethalDamage(
        Creature target,
        ref decimal amount,
        out FinisherProtectionToken? token) =>
        FinisherProtectionService.TryProtectLethalDamage(target, ref amount, out token);

    internal static void ConfirmProtectedDamageResult(
        DamageResult? result,
        bool originalRan,
        FinisherProtectionToken? token) =>
        FinisherProtectionService.ConfirmProtectedDamageResult(result, originalRan, token);

    internal static void FinalizeLethalProtection(FinisherProtectionToken? token) =>
        FinisherProtectionService.FinalizeLethalProtection(token);

    internal static bool TryTakeDamageDisplayOverride(DamageResult result, out int displayDamage) =>
        FinisherProtectionService.TryTakeDamageDisplayOverride(result, out displayDamage);

    internal static void NotifyPrimaryAttackAnimation(Creature creature, string triggerName)
    {
        FinisherSessionRegistry.GetActiveSession()?.NotifyPrimaryAttackAnimation(creature, triggerName);
    }

    internal static void NotifyPrimaryDamage(Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
    {
        FinisherSessionRegistry.GetActiveSession()?.NotifyPrimaryDamage(dealer, cardSource, cardPlay);
    }

    internal static void NotifyDeathAnimationStarting(MegaCrit.Sts2.Core.Nodes.Combat.NCreature creature)
    {
        FinisherSessionRegistry.GetActiveSession()?.NotifyDeathAnimationStarting(creature);
    }

    internal static bool TryInterceptAttackCommand(
        AttackCommand command,
        PlayerChoiceContext? choiceContext,
        out Task<AttackCommand>? result)
    {
        result = null;
        if (!NinjaSlayerPatchCapabilities.FinisherEnabled
            || FinisherSessionRegistry.HasRegisteredSession()
            || IsCommandBypassed(command)
            || !FinisherAttackCommandAdapter.TryCreateSpec(command, out FinisherAttackSpec? spec)
            || spec == null
            || FinisherEligibilityService.IsExcludedAttackCard(spec.Card))
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
        if (!NinjaSlayerPatchCapabilities.FinisherEnabled
            || FinisherSessionRegistry.HasRegisteredSession()
            || DirectDamageBypassDepth.Value > 0
            || dealer?.Player?.Character is not INinjaSlayerCharacter
            || cardSource?.Type != CardType.Attack
            || cardPlay == null
            || cardSource.Owner?.Creature != dealer
            || FinisherEligibilityService.IsExcludedAttackCard(cardSource))
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
            _ => amount,
            props,
            1,
            FinisherTargeting.Fixed,
            FixedTargets: targetList);
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

    internal static Task WrapAfterCardPlayed(Task original, CardPlay cardPlay) =>
        FinisherCleanupService.CompleteAfterCardPlayed(original, cardPlay);

    internal static Task WrapCardPlay(Task original, CardModel card) =>
        FinisherCleanupService.CleanupAfterCardPlay(original, card);

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
        if (!FinisherEligibilityService.TryCreateSession(spec, command, entryPoint, out FinisherSession? session))
        {
            return await ExecuteOriginalCommand(command, choiceContext);
        }

        ArgumentNullException.ThrowIfNull(session);
        bool transferred = false;
        try
        {
            await session.Begin();
            AttackCommand result = await ExecuteOriginalCommand(command, choiceContext);
            if (session.RequiresAfterCardPlayed)
            {
                FinisherSessionRegistry.TransferToAfterCardPlayed(session);
                transferred = true;
            }
            else
            {
                await session.CompleteAsync(
                    FinisherCompletionStatus.Succeeded,
                    FinisherCompletionMode.PlayPose);
            }

            return result;
        }
        catch (Exception ex)
        {
            await session.CompleteAsync(
                FinisherCompletionStatus.Faulted,
                FinisherCompletionMode.CommitWithoutPose,
                ex.Message);
            throw;
        }
        finally
        {
            if (!transferred)
            {
                await session.CompleteAsync(
                    FinisherCompletionStatus.Cancelled,
                    FinisherCompletionMode.CommitWithoutPose,
                    "Command wrapper exited before normal completion.");
            }
        }
    }

    public static async Task ExecuteSequenceWithFinisher(
        PlayerChoiceContext choiceContext,
        FinisherAttackSpec spec,
        Func<Task> sequence)
    {
        if (!FinisherEligibilityService.TryCreateSession(spec, null, "explicit-sequence", out FinisherSession? session))
        {
            await sequence();
            return;
        }

        ArgumentNullException.ThrowIfNull(session);
        bool transferred = false;
        try
        {
            await session.Begin();
            await sequence();
            if (session.RequiresAfterCardPlayed)
            {
                FinisherSessionRegistry.TransferToAfterCardPlayed(session);
                transferred = true;
            }
            else
            {
                await session.CompleteAsync(
                    FinisherCompletionStatus.Succeeded,
                    FinisherCompletionMode.PlayPose);
            }
        }
        catch (Exception ex)
        {
            await session.CompleteAsync(
                FinisherCompletionStatus.Faulted,
                FinisherCompletionMode.CommitWithoutPose,
                ex.Message);
            throw;
        }
        finally
        {
            if (!transferred)
            {
                await session.CompleteAsync(
                    FinisherCompletionStatus.Cancelled,
                    FinisherCompletionMode.CommitWithoutPose,
                    "Sequence wrapper exited before normal completion.");
            }
        }
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
        if (!FinisherEligibilityService.TryCreateSession(spec, null, entryPoint, out FinisherSession? session))
        {
            await damageAction();
            return;
        }

        ArgumentNullException.ThrowIfNull(session);
        bool transferred = false;
        try
        {
            await session.Begin();
            await damageAction();
            if (session.RequiresAfterCardPlayed)
            {
                FinisherSessionRegistry.TransferToAfterCardPlayed(session);
                transferred = true;
            }
            else
            {
                await session.CompleteAsync(
                    FinisherCompletionStatus.Succeeded,
                    FinisherCompletionMode.PlayPose);
            }
        }
        catch (Exception ex)
        {
            await session.CompleteAsync(
                FinisherCompletionStatus.Faulted,
                FinisherCompletionMode.CommitWithoutPose,
                ex.Message);
            throw;
        }
        finally
        {
            if (!transferred)
            {
                await session.CompleteAsync(
                    FinisherCompletionStatus.Cancelled,
                    FinisherCompletionMode.CommitWithoutPose,
                    "Direct-damage wrapper exited before normal completion.");
            }
        }
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
                cardSource,
                cardPlay);
        }
        finally
        {
            DirectDamageBypassDepth.Value--;
        }
    }

    private sealed record CommandBypassFrame(AttackCommand Command, CommandBypassFrame? Parent);
}
