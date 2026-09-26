using System.Text.Json;
using NinjaSlayer.Content;

namespace NinjaSlayer.LogicTests;

public sealed class NinjaSlayerSettingsTests
{
    [Fact]
    public void ValidationDefaultsOnButRequiresARunSnapshot()
    {
        Assert.True(new NinjaSlayerSettingsData().ForceAllEventsOnce);
        Assert.False(new NinjaSlayerSettingsData().FreeControlEnabled);
        Assert.True(new NinjaSlayerSettingsData().NarrationEnabled);
        Assert.False(new NinjaSlayerSettingsData().TelemetryNoticeShown);
        Assert.False(new NinjaSlayerSettingsData().PublicReplayEnabled);
        Assert.False(new NinjaSlayerSettingsData().MangaSelectPortraitEnabled);
        Assert.False(new NinjaSlayerRunState().EventValidationEnabled);
    }

    [Fact]
    public void SettingsDataKeepsItsCurrentJsonContract()
    {
        var settings = new NinjaSlayerSettingsData
        {
            ForceAllEventsOnce = false,
            NarrationEnabled = false,
            TelemetryNoticeShown = true,
            PublicReplayEnabled = true,
            MangaSelectPortraitEnabled = true
        };

        string json = JsonSerializer.Serialize(settings);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal(
            ["ForceAllEventsOnce", "FreeControlEnabled", "NarrationEnabled", "TelemetryNoticeShown", "PublicReplayEnabled", "MangaSelectPortraitEnabled", "BriefBossGreetingEnabled"],
            root.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.False(root.GetProperty("ForceAllEventsOnce").GetBoolean());

        NinjaSlayerSettingsData restored = JsonSerializer.Deserialize<NinjaSlayerSettingsData>(json)!;
        Assert.False(restored.ForceAllEventsOnce);
        Assert.False(restored.NarrationEnabled);
        Assert.True(JsonSerializer.Deserialize<NinjaSlayerSettingsData>("{}")!.NarrationEnabled);
        Assert.True(restored.TelemetryNoticeShown);
        Assert.True(restored.PublicReplayEnabled);
        Assert.True(restored.MangaSelectPortraitEnabled);
        Assert.False(JsonSerializer.Deserialize<NinjaSlayerSettingsData>("{\"NarrationEnabled\":false}")!.MangaSelectPortraitEnabled);
        Assert.False(JsonSerializer.Deserialize<NinjaSlayerSettingsData>("{\"TelemetryNoticeShown\":true}")!.PublicReplayEnabled);
        Assert.False(JsonSerializer.Deserialize<NinjaSlayerSettingsData>("{\"ForceAllEventsOnce\":false}")!.TelemetryNoticeShown);
    }

    [Fact]
    public void FirstCompletedGreetingEnablesBriefModeForOldAndNewSettings()
    {
        foreach (var settings in new[] { new NinjaSlayerSettingsData(), JsonSerializer.Deserialize<NinjaSlayerSettingsData>("{}")! })
        {
            Assert.Null(settings.BriefBossGreetingEnabled);
            settings.CompleteFirstBossGreeting();
            Assert.True(settings.BriefBossGreetingEnabled);
            Assert.True(JsonSerializer.Deserialize<NinjaSlayerSettingsData>(JsonSerializer.Serialize(settings))!.BriefBossGreetingEnabled);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletingOrShorteningGreetingNeverOverwritesManualChoice(bool choice)
    {
        var settings = new NinjaSlayerSettingsData { BriefBossGreetingEnabled = choice };
        settings.CompleteFirstBossGreeting();
        settings.CompleteFirstBossGreeting();
        Assert.Equal(choice, settings.BriefBossGreetingEnabled);
        Assert.Equal(choice, JsonSerializer.Deserialize<NinjaSlayerSettingsData>(JsonSerializer.Serialize(settings))!.BriefBossGreetingEnabled);
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
