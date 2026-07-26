namespace NinjaSlayer.Code.Compatibility;

internal static class StableMethodBodyContract
{
    public static bool Matches(
        string assemblyVersion,
        int metadataToken,
        string ilSha256,
        string expectedAssemblyVersion,
        int expectedMetadataToken,
        string expectedIlSha256) =>
        string.Equals(assemblyVersion, expectedAssemblyVersion, StringComparison.Ordinal)
        && metadataToken == expectedMetadataToken
        && string.Equals(ilSha256, expectedIlSha256, StringComparison.Ordinal);
}
