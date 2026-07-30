namespace NinjaSlayer.Code.Compatibility;

public static class OrobasSeaGlassCandidatePolicy
{
    public static bool ShouldReplace(bool ownerIsRestricted, bool targetIsRestricted) =>
        !ownerIsRestricted && targetIsRestricted;

    public static T SelectReplacement<T>(
        T owner,
        IEnumerable<T> unlockedCharacters,
        Func<T, bool> isRestricted,
        Func<T, T, bool> hasSameIdentity,
        Func<IReadOnlyList<T>, T> choose)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(unlockedCharacters);
        ArgumentNullException.ThrowIfNull(isRestricted);
        ArgumentNullException.ThrowIfNull(hasSameIdentity);
        ArgumentNullException.ThrowIfNull(choose);

        T[] candidates = unlockedCharacters
            .Where(candidate => !isRestricted(candidate) && !hasSameIdentity(candidate, owner))
            .ToArray();
        return candidates.Length == 0 ? owner : choose(candidates);
    }
}
