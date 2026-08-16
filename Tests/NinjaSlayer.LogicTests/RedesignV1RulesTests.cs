using NinjaSlayer.Content;

namespace NinjaSlayer.LogicTests;

public sealed class RedesignV1RulesTests
{
    [Fact]
    public void NewRunRulesDefaultToRedesignV1()
    {
        Assert.Equal(NinjaSlayerRulesVersion.RedesignV1, new NinjaSlayerRunRules().RulesVersion);
    }

    [Fact]
    public void TeaCapacityContractIsThirteenThenTwenty()
    {
        Assert.Equal(13, RedesignV1Rules.StartingTeaEnergy);
        Assert.Equal(20, RedesignV1Rules.AncientTeaEnergy);
    }

    [Fact]
    public void ShurikenDamageIncludesStockBonus()
    {
        Assert.Equal(7, RedesignV1Rules.ShurikenDamage(3));
    }
}
