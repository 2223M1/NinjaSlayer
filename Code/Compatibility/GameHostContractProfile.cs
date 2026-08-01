namespace NinjaSlayer.Code.Compatibility;

internal enum PreparedDrawHostLayout
{
    DirectAsync,
    WrapperWithAsyncInternal
}

internal readonly record struct MethodBodyContract(
    int MetadataToken,
    string IlSha256);

internal readonly record struct PreparedDrawHostContract(
    PreparedDrawHostLayout Layout,
    MethodBodyContract PublicMethod,
    MethodBodyContract? InternalMethod,
    MethodBodyContract AsyncMoveNext);

internal sealed record GameHostContractProfile(
    string Channel,
    string GameVersion,
    string RitsuLibPackageId,
    string RitsuLibVersion,
    string BuildVariant,
    string AssemblyVersion,
    Guid ModuleMvid,
    MethodBodyContract LethalDamage,
    PreparedDrawHostContract PreparedDraw,
    MethodBodyContract PreparedQueueAdd,
    MethodBodyContract PreparedQueueRemove)
{
    public static IReadOnlyList<GameHostContractProfile> Supported =>
        GeneratedGameHostContracts.Current;

    internal static IReadOnlyList<GameHostContractProfile> AllKnown =>
        GeneratedGameHostContracts.All;

    public string Id => $"{Channel}/{GameVersion}/{BuildVariant}";

    public static bool TryResolve(
        MethodBodyFingerprint fingerprint,
        out GameHostContractProfile profile)
    {
        profile = Supported.FirstOrDefault(candidate =>
            candidate.MatchesHost(fingerprint))!;
        return profile is not null;
    }

    public bool MatchesHost(MethodBodyFingerprint fingerprint) =>
        string.Equals(
            fingerprint.AssemblyVersion,
            AssemblyVersion,
            StringComparison.Ordinal)
        && fingerprint.ModuleMvid == ModuleMvid;

}
