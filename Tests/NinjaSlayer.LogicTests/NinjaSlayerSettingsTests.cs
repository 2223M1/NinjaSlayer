using NinjaSlayer.Content;

namespace NinjaSlayer.LogicTests;

public sealed class NinjaSlayerSettingsTests
{
    [Fact]
    public void ValidationDefaultsOnButRequiresARunSnapshot()
    {
        Assert.True(new NinjaSlayerSettingsData().ForceAllEventsOnce);
        Assert.False(new NinjaSlayerRunState().EventValidationEnabled);
    }
}
