using System.Text.Json;
using NinjaSlayer.Content;

namespace NinjaSlayer.LogicTests;

public sealed class NinjaSlayerSettingsTests
{
    [Fact]
    public void SettingsDefaultsPreserveExistingFeatures()
    {
        Assert.False(new NinjaSlayerSettingsData().RadioEnabled);
        Assert.False(new NinjaSlayerSettingsData().FreeControlEnabled);
        Assert.True(new NinjaSlayerSettingsData().NarrationEnabled);
        Assert.False(new NinjaSlayerSettingsData().TelemetryNoticeShown);
        Assert.False(new NinjaSlayerSettingsData().PublicReplayEnabled);
        Assert.False(new NinjaSlayerSettingsData().MangaSelectPortraitEnabled);
    }

    [Fact]
    public void SettingsDataKeepsItsCurrentJsonContract()
    {
        var settings = new NinjaSlayerSettingsData
        {
            RadioEnabled = true,
            NarrationEnabled = false,
            TelemetryNoticeShown = true,
            PublicReplayEnabled = true,
            MangaSelectPortraitEnabled = true
        };

        string json = JsonSerializer.Serialize(settings);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal(
            ["RadioEnabled", "FreeControlEnabled", "NarrationEnabled", "TelemetryNoticeShown", "PublicReplayEnabled", "MangaSelectPortraitEnabled"],
            root.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.True(root.GetProperty("RadioEnabled").GetBoolean());

        NinjaSlayerSettingsData restored = JsonSerializer.Deserialize<NinjaSlayerSettingsData>(json)!;
        Assert.True(restored.RadioEnabled);
        Assert.False(restored.NarrationEnabled);
        Assert.False(JsonSerializer.Deserialize<NinjaSlayerSettingsData>("{\"ForceAllEventsOnce\":true}")!.RadioEnabled);
        Assert.True(JsonSerializer.Deserialize<NinjaSlayerSettingsData>("{\"FreeControlEnabled\":true}")!.FreeControlEnabled);
        Assert.True(JsonSerializer.Deserialize<NinjaSlayerSettingsData>("{}")!.NarrationEnabled);
        Assert.True(restored.TelemetryNoticeShown);
        Assert.True(restored.PublicReplayEnabled);
        Assert.True(restored.MangaSelectPortraitEnabled);
        Assert.False(JsonSerializer.Deserialize<NinjaSlayerSettingsData>("{\"NarrationEnabled\":false}")!.MangaSelectPortraitEnabled);
        Assert.False(JsonSerializer.Deserialize<NinjaSlayerSettingsData>("{\"TelemetryNoticeShown\":true}")!.PublicReplayEnabled);
        Assert.False(JsonSerializer.Deserialize<NinjaSlayerSettingsData>("{\"ForceAllEventsOnce\":false}")!.TelemetryNoticeShown);
    }

    [Fact]
    public void RunStateKeepsItsCurrentJsonContract()
    {
        var runState = new NinjaSlayerRunState
        {
            PendingAncientEntranceAnimation = true,
            CompletedBossGreetingRoomKeys = ["act1:boss", "act2:boss"]
        };

        string json = JsonSerializer.Serialize(runState);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal(
            [
                "PendingAncientEntranceAnimation",
                "CompletedBossGreetingRoomKeys"
            ],
            root.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.True(root.GetProperty("PendingAncientEntranceAnimation").GetBoolean());
        Assert.Equal(
            ["act1:boss", "act2:boss"],
            root.GetProperty("CompletedBossGreetingRoomKeys")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray());

        NinjaSlayerRunState restored = JsonSerializer.Deserialize<NinjaSlayerRunState>(json)!;
        Assert.True(restored.PendingAncientEntranceAnimation);
        Assert.Equal(["act1:boss", "act2:boss"], restored.CompletedBossGreetingRoomKeys);
    }

}
