using NinjaSlayer.Code.Compatibility;

namespace NinjaSlayer.LogicTests;

public sealed class StableMethodBodyContractTests
{
    [Fact]
    public void StableFingerprintAcceptsMatchingVersionTokenAndIl()
    {
        Assert.True(StableMethodBodyContract.Matches(
            "0.1.0.0",
            0x06008438,
            "same-il",
            "0.1.0.0",
            0x06008438,
            "same-il"));
    }

    [Theory]
    [InlineData("0.2.0.0", 0x06008438, "same-il")]
    [InlineData("0.1.0.0", 0x06008439, "same-il")]
    [InlineData("0.1.0.0", 0x06008438, "changed-il")]
    public void StableFingerprintRejectsBehavioralIdentityChanges(
        string assemblyVersion,
        int metadataToken,
        string ilSha256)
    {
        Assert.False(StableMethodBodyContract.Matches(
            assemblyVersion,
            metadataToken,
            ilSha256,
            "0.1.0.0",
            0x06008438,
            "same-il"));
    }
}
