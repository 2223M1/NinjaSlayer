using System.Text.Json;
using System.Text.Json.Nodes;
using NinjaSlayer.Content;

namespace NinjaSlayer.LogicTests;

public sealed class RunDataTests
{
    private static readonly JsonSerializerOptions RitsuJsonOptions = new()
    {
        IncludeFields = true
    };

    [Fact]
    public void PreGreetingSinglePlayerStateRetainsVersionOneDefaults()
    {
        Dictionary<ulong, NinjaSlayerRunState> players = ReadPlayers("single-player-pre-greeting-v1.json");

        NinjaSlayerRunState state = Assert.Single(players).Value;
        Assert.True(state.PendingAncientEntranceAnimation);
        Assert.Empty(state.CompletedBossGreetingRoomKeys);
        Assert.False(NinjaSlayerRunStateNormalizer.TryNormalizeRoomKeys(state, out _));
    }

    [Fact]
    public void MultiplayerStateNormalizesRoomKeysWithoutChangingPlayerOwnership()
    {
        Dictionary<ulong, NinjaSlayerRunState> players = ReadPlayers("multiplayer-boss-greeting-v1.json");

        Assert.Equal([1001UL, 1002UL], players.Keys.Order());
        NinjaSlayerRunState first = players[1001];
        List<string> original = [.. first.CompletedBossGreetingRoomKeys];
        Assert.True(NinjaSlayerRunStateNormalizer.TryNormalizeRoomKeys(first, out List<string> normalized));
        Assert.Equal(["ACT1/BOSS", "act1/boss", "ACT2/BOSS"], normalized);
        Assert.Equal(original, first.CompletedBossGreetingRoomKeys);

        first.CompletedBossGreetingRoomKeys = normalized;
        Assert.False(NinjaSlayerRunStateNormalizer.TryNormalizeRoomKeys(first, out _));

        NinjaSlayerRunState second = players[1002];
        Assert.True(second.PendingAncientEntranceAnimation);
        Assert.True(NinjaSlayerRunStateNormalizer.TryNormalizeRoomKeys(second, out List<string> empty));
        Assert.Empty(empty);
    }

    private static Dictionary<ulong, NinjaSlayerRunState> ReadPlayers(string fixtureName)
    {
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "RunData", fixtureName);
        JsonNode root = JsonNode.Parse(File.ReadAllText(fixturePath))
            ?? throw new InvalidDataException($"RunData fixture is empty: {fixtureName}");
        JsonObject entry = root["_ritsulib"]?["run_saved_data"]?["NinjaSlayer"]?["ninja_slayer_run_state"]
            as JsonObject
            ?? throw new InvalidDataException($"RunData fixture has no NinjaSlayer player slot: {fixtureName}");

        Assert.Equal(1, entry["schema"]?.GetValue<int>());
        Assert.Equal("player", entry["kind"]?.GetValue<string>());
        JsonObject playerNodes = entry["players"] as JsonObject
            ?? throw new InvalidDataException($"RunData fixture has no players: {fixtureName}");

        var players = new Dictionary<ulong, NinjaSlayerRunState>();
        foreach ((string key, JsonNode? value) in playerNodes)
        {
            Assert.True(ulong.TryParse(key, out ulong netId));
            NinjaSlayerRunState state = value?.Deserialize<NinjaSlayerRunState>(RitsuJsonOptions)
                ?? throw new InvalidDataException($"RunData fixture player is invalid: {key}");
            players.Add(netId, state);
        }

        return players;
    }
}
