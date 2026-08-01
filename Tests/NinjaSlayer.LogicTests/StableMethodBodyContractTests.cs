using NinjaSlayer.Code.Compatibility;

namespace NinjaSlayer.LogicTests;

public sealed class StableMethodBodyContractTests
{
    [Fact]
    public void StableFingerprintAcceptsExactHostAndMethodIdentity()
    {
        GameHostContractProfile profile = GameHostContractProfile.AllKnown
            .Single(item => item.GameVersion == "0.107.1");

        Assert.True(StableMethodBodyContract.Matches(
            Fingerprint(profile, profile.LethalDamage),
            profile,
            profile.LethalDamage));
    }

    [Theory]
    [InlineData("0.2.0.0", "97f10687-c306-4798-ab75-8b9f23f34dfb", 0x06008154, "93374cd3459eb592a9902b36e2951e3a8edd76149a23b25fa916f8a09d29a193")]
    [InlineData("0.1.0.0", "fc7d1cd0-3eb8-4bc1-be64-afffa905aab8", 0x06008154, "93374cd3459eb592a9902b36e2951e3a8edd76149a23b25fa916f8a09d29a193")]
    [InlineData("0.1.0.0", "97f10687-c306-4798-ab75-8b9f23f34dfb", 0x06008155, "93374cd3459eb592a9902b36e2951e3a8edd76149a23b25fa916f8a09d29a193")]
    [InlineData("0.1.0.0", "97f10687-c306-4798-ab75-8b9f23f34dfb", 0x06008154, "changed-il")]
    public void StableFingerprintRejectsBehavioralIdentityChanges(
        string assemblyVersion,
        string moduleMvid,
        int metadataToken,
        string ilSha256)
    {
        GameHostContractProfile profile = GameHostContractProfile.AllKnown
            .Single(item => item.GameVersion == "0.107.1");

        Assert.False(StableMethodBodyContract.Matches(
            new MethodBodyFingerprint(
                assemblyVersion,
                Guid.Parse(moduleMvid),
                metadataToken,
                ilSha256),
            profile,
            profile.LethalDamage));
    }

    [Fact]
    public void KnownProfilesCoverBothActiveChannelsAndDrawLayouts()
    {
        Assert.Equal(2, GameHostContractProfile.AllKnown.Count);
        Assert.Equal(
            ["stable", "preview"],
            GameHostContractProfile.AllKnown
                .Select(profile => profile.Channel)
                .ToArray());
        Assert.Equal(
            GameHostContractProfile.AllKnown.Count,
            GameHostContractProfile.AllKnown
                .Select(profile => profile.ModuleMvid)
                .Distinct()
                .Count());
        Assert.Single(GameHostContractProfile.Supported);
        Assert.Equal("preview", GameHostContractProfile.Supported[0].Channel);

        GameHostContractProfile publicRelease = GameHostContractProfile.AllKnown
            .Single(profile => profile.GameVersion == "0.107.1");
        Assert.Equal(PreparedDrawHostLayout.DirectAsync, publicRelease.PreparedDraw.Layout);
        Assert.Null(publicRelease.PreparedDraw.InternalMethod);

        Assert.All(
            GameHostContractProfile.AllKnown.Where(profile => profile.GameVersion != "0.107.1"),
            profile =>
            {
                Assert.Equal(
                    PreparedDrawHostLayout.WrapperWithAsyncInternal,
                    profile.PreparedDraw.Layout);
                Assert.NotNull(profile.PreparedDraw.InternalMethod);
            });
    }

    [Fact]
    public void CurrentBuildOnlyResolvesItsSelectedProfile()
    {
        GameHostContractProfile selected = Assert.Single(GameHostContractProfile.Supported);
        Assert.True(GameHostContractProfile.TryResolve(
            Fingerprint(selected, selected.PreparedDraw.PublicMethod),
            out GameHostContractProfile resolved));
        Assert.Same(selected, resolved);

        GameHostContractProfile other = GameHostContractProfile.AllKnown
            .Single(profile => !ReferenceEquals(profile, selected));
        Assert.False(GameHostContractProfile.TryResolve(
            Fingerprint(other, other.PreparedDraw.PublicMethod),
            out _));
    }

    [Fact]
    public void EveryKnownProfileMatchesAllDeclaredMethodContracts()
    {
        foreach (GameHostContractProfile profile in GameHostContractProfile.AllKnown)
        {
            MethodBodyContract[] methods =
            [
                profile.LethalDamage,
                profile.PreparedDraw.PublicMethod,
                profile.PreparedDraw.AsyncMoveNext,
                profile.PreparedQueueAdd,
                profile.PreparedQueueRemove
            ];
            foreach (MethodBodyContract method in methods)
            {
                Assert.True(StableMethodBodyContract.Matches(
                    Fingerprint(profile, method),
                    profile,
                    method));
            }

            if (profile.PreparedDraw.InternalMethod is { } internalMethod)
            {
                Assert.True(StableMethodBodyContract.Matches(
                    Fingerprint(profile, internalMethod),
                    profile,
                    internalMethod));
            }
        }
    }

    private static MethodBodyFingerprint Fingerprint(
        GameHostContractProfile profile,
        MethodBodyContract method) =>
        new(
            profile.AssemblyVersion,
            profile.ModuleMvid,
            method.MetadataToken,
            method.IlSha256);
}
