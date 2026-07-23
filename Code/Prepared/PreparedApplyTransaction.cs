namespace NinjaSlayer.Code.Prepared;

internal enum PreparedApplyStatus
{
    NotApplied,
    Applied,
    AppliedDegraded,
    SafetyRepaired,
    SafetyRepairFailed
}

internal readonly record struct PreparedApplyResult(
    PreparedApplyStatus Status,
    string? Reason = null,
    Exception? Error = null)
{
    public bool IsPrepared => Status is PreparedApplyStatus.Applied or PreparedApplyStatus.AppliedDegraded;

    public bool IsDegraded => Status is
        PreparedApplyStatus.AppliedDegraded or
        PreparedApplyStatus.SafetyRepaired or
        PreparedApplyStatus.SafetyRepairFailed;

    public bool RequiresLifecycleRepair => Status == PreparedApplyStatus.SafetyRepairFailed;
}

internal enum PreparedCleanupStatus
{
    NotRequired,
    Cleared,
    Failed,
    Deferred
}

internal readonly record struct PreparedCleanupResult(
    PreparedCleanupStatus Status,
    Exception? Error = null,
    string? Reason = null)
{
    public bool RequiresLifecycleRepair => Status is PreparedCleanupStatus.Failed or PreparedCleanupStatus.Deferred;
}

internal enum PreparedQueueTransactionStatus
{
    Succeeded,
    FailedStable,
    FailedUncertain
}

internal readonly record struct PreparedQueueTransactionResult(
    PreparedQueueTransactionStatus Status,
    Exception? Error = null)
{
    public bool Succeeded => Status == PreparedQueueTransactionStatus.Succeeded;
}

internal static class PreparedApplyPolicy
{
    public static PreparedApplyResult ResolveAfterReposition(
        PreparedQueueTransactionResult reposition,
        bool hasStablePreparedPlacement,
        PreparedCleanupResult cleanup,
        string failureReason)
    {
        if (reposition.Succeeded && hasStablePreparedPlacement)
        {
            return new PreparedApplyResult(PreparedApplyStatus.Applied);
        }

        if (hasStablePreparedPlacement)
        {
            return new PreparedApplyResult(
                PreparedApplyStatus.AppliedDegraded,
                failureReason,
                reposition.Error);
        }

        return cleanup.Status switch
        {
            PreparedCleanupStatus.NotRequired or PreparedCleanupStatus.Cleared =>
                new PreparedApplyResult(
                    PreparedApplyStatus.SafetyRepaired,
                    failureReason,
                    reposition.Error),
            _ => new PreparedApplyResult(
                PreparedApplyStatus.SafetyRepairFailed,
                failureReason,
                Combine(reposition.Error, cleanup.Error))
        };
    }

    private static Exception? Combine(Exception? primary, Exception? secondary)
    {
        if (primary is null)
        {
            return secondary;
        }
        if (secondary is null)
        {
            return primary;
        }

        return new AggregateException(primary, secondary);
    }
}

internal static class PreparedQueueTransaction
{
    public static PreparedQueueTransactionResult Execute(
        Action remove,
        Action insertAtTarget,
        Action restoreAtOriginal,
        Func<bool> isPresent)
    {
        try
        {
            remove();
        }
        catch (Exception exception)
        {
            return Recover(exception, restoreAtOriginal, isPresent);
        }

        try
        {
            insertAtTarget();
        }
        catch (Exception exception)
        {
            return Recover(exception, restoreAtOriginal, isPresent);
        }

        try
        {
            if (isPresent())
            {
                return new PreparedQueueTransactionResult(PreparedQueueTransactionStatus.Succeeded);
            }
        }
        catch (Exception exception)
        {
            return new PreparedQueueTransactionResult(
                PreparedQueueTransactionStatus.FailedUncertain,
                exception);
        }

        return Recover(
            new InvalidOperationException("Prepared queue insert completed without retaining the card."),
            restoreAtOriginal,
            isPresent);
    }

    private static PreparedQueueTransactionResult Recover(
        Exception failure,
        Action restoreAtOriginal,
        Func<bool> isPresent)
    {
        if (!TryIsPresent(isPresent, ref failure, out bool present))
        {
            return new PreparedQueueTransactionResult(
                PreparedQueueTransactionStatus.FailedUncertain,
                failure);
        }

        if (!present)
        {
            try
            {
                restoreAtOriginal();
            }
            catch (Exception rollbackFailure)
            {
                failure = Combine(failure, rollbackFailure);
            }

            if (!TryIsPresent(isPresent, ref failure, out present))
            {
                return new PreparedQueueTransactionResult(
                    PreparedQueueTransactionStatus.FailedUncertain,
                    failure);
            }
        }

        return new PreparedQueueTransactionResult(
            present
                ? PreparedQueueTransactionStatus.FailedStable
                : PreparedQueueTransactionStatus.FailedUncertain,
            failure);
    }

    private static bool TryIsPresent(
        Func<bool> isPresent,
        ref Exception failure,
        out bool present)
    {
        try
        {
            present = isPresent();
            return true;
        }
        catch (Exception inspectionFailure)
        {
            failure = Combine(failure, inspectionFailure);
            present = false;
            return false;
        }
    }

    private static Exception Combine(Exception primary, Exception secondary) =>
        new AggregateException(primary, secondary);
}
