using System.Text.Json;
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

    [Fact]
    public void SettingsDataKeepsItsCurrentJsonContract()
    {
        var settings = new NinjaSlayerSettingsData
        {
            ForceAllEventsOnce = false
        };

        string json = JsonSerializer.Serialize(settings);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal(
            ["ForceAllEventsOnce"],
            root.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.False(root.GetProperty("ForceAllEventsOnce").GetBoolean());

        NinjaSlayerSettingsData restored = JsonSerializer.Deserialize<NinjaSlayerSettingsData>(json)!;
        Assert.False(restored.ForceAllEventsOnce);
    }

    [Fact]
    public void RunStateKeepsItsCurrentJsonContract()
    {
        var runState = new NinjaSlayerRunState
        {
            EventValidationEnabled = true,
            PendingAncientEntranceAnimation = true,
            CompletedBossGreetingRoomKeys = ["act1:boss", "act2:boss"]
        };

        string json = JsonSerializer.Serialize(runState);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal(
            [
                "EventValidationEnabled",
                "PendingAncientEntranceAnimation",
                "CompletedBossGreetingRoomKeys"
            ],
            root.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.True(root.GetProperty("EventValidationEnabled").GetBoolean());
        Assert.True(root.GetProperty("PendingAncientEntranceAnimation").GetBoolean());
        Assert.Equal(
            ["act1:boss", "act2:boss"],
            root.GetProperty("CompletedBossGreetingRoomKeys")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray());

        NinjaSlayerRunState restored = JsonSerializer.Deserialize<NinjaSlayerRunState>(json)!;
        Assert.True(restored.EventValidationEnabled);
        Assert.True(restored.PendingAncientEntranceAnimation);
        Assert.Equal(["act1:boss", "act2:boss"], restored.CompletedBossGreetingRoomKeys);
    }

}
