namespace NinjaSlayer.Code.Prepared;

internal enum CombatStateAccessStatus
{
    Supplied,
    CardOwned,
    Unavailable,
    Mismatched
}

internal readonly record struct CombatStateAccessResult<TState>(
    CombatStateAccessStatus Status,
    TState? State,
    string? Reason = null)
    where TState : class
{
    public bool Succeeded => State is not null
        && Status is CombatStateAccessStatus.Supplied or CombatStateAccessStatus.CardOwned;
}

internal static class CombatStateAccessPolicy
{
    public static CombatStateAccessResult<TState> Resolve<TState>(
        TState? suppliedState,
        TState? cardState)
        where TState : class
    {
        if (suppliedState is not null && cardState is not null && !ReferenceEquals(suppliedState, cardState))
        {
            return new CombatStateAccessResult<TState>(
                CombatStateAccessStatus.Mismatched,
                null,
                "The hook and card refer to different combat states.");
        }

        if (suppliedState is not null)
        {
            return new CombatStateAccessResult<TState>(CombatStateAccessStatus.Supplied, suppliedState);
        }

        return cardState is not null
            ? new CombatStateAccessResult<TState>(CombatStateAccessStatus.CardOwned, cardState)
            : new CombatStateAccessResult<TState>(
                CombatStateAccessStatus.Unavailable,
                null,
                "Neither the hook nor the card exposes a current combat state.");
    }
}
