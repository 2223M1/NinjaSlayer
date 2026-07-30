using NinjaSlayer.Code.Compatibility;

namespace NinjaSlayer.LogicTests;

public sealed class OrobasSeaGlassCandidatePolicyTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public void ReplacementOnlyProtectsNonNinjaSlayerOwners(
        bool ownerIsRestricted,
        bool targetIsRestricted,
        bool expected)
    {
        Assert.Equal(
            expected,
            OrobasSeaGlassCandidatePolicy.ShouldReplace(ownerIsRestricted, targetIsRestricted));
    }

    [Fact]
    public void ReplacementExcludesOwnerAndRestrictedCharacters()
    {
        TestCharacter owner = new("owner", false);
        TestCharacter valid = new("valid", false);
        TestCharacter selected = OrobasSeaGlassCandidatePolicy.SelectReplacement(
            owner,
            [owner, new TestCharacter("restricted", true), valid],
            character => character.IsRestricted,
            (left, right) => left.Id == right.Id,
            candidates => Assert.Single(candidates));

        Assert.Same(valid, selected);
    }

    [Fact]
    public void ReplacementFallsBackToOwnerWhenNoOtherLegalCharacterExists()
    {
        TestCharacter owner = new("owner", false);
        TestCharacter selected = OrobasSeaGlassCandidatePolicy.SelectReplacement(
            owner,
            [owner, new TestCharacter("restricted", true)],
            character => character.IsRestricted,
            (left, right) => left.Id == right.Id,
            _ => throw new InvalidOperationException("Selector must not run for an empty candidate set."));

        Assert.Same(owner, selected);
    }

    [Fact]
    public void FixedSelectorReproducesTheSameChoiceFromMultipleCandidates()
    {
        TestCharacter owner = new("owner", false);
        TestCharacter first = new("first", false);
        TestCharacter second = new("second", false);
        TestCharacter duplicateOwner = new("owner", false);
        TestCharacter restricted = new("restricted", true);
        TestCharacter[] unlocked = [restricted, first, duplicateOwner, second];

        TestCharacter Select() => OrobasSeaGlassCandidatePolicy.SelectReplacement(
            owner,
            unlocked,
            character => character.IsRestricted,
            (left, right) => left.Id == right.Id,
            candidates => candidates[1]);

        Assert.Same(second, Select());
        Assert.Same(second, Select());
    }

    private sealed record TestCharacter(string Id, bool IsRestricted);
}
