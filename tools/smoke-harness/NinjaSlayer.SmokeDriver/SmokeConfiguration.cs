using System.Text.Json;
using System.Text.Json.Serialization;

namespace NinjaSlayer.SmokeDriver;

internal enum SmokePhase
{
    Fresh,
    Resume,
    FullAutoSlay
}

internal sealed record SmokeConfiguration(
    string CandidateSha,
    string Seed,
    SmokePhase Phase,
    string CheckpointPath,
    string AutoSlayLogPath,
    string FailureScreenshotPath)
{
    public static SmokeConfiguration Load(string path)
    {
        string fullPath = Path.GetFullPath(path);
        SmokeConfiguration? configuration = JsonSerializer.Deserialize(
            File.ReadAllText(fullPath),
            SmokeJsonContext.Default.SmokeConfiguration);
        if (configuration is null
            || configuration.CandidateSha.Length != 40
            || configuration.CandidateSha.Any(character => !Uri.IsHexDigit(character))
            || string.IsNullOrWhiteSpace(configuration.Seed))
        {
            throw new InvalidDataException("Smoke configuration is missing a valid candidate SHA or seed.");
        }

        return configuration with
        {
            CheckpointPath = Path.GetFullPath(configuration.CheckpointPath),
            AutoSlayLogPath = Path.GetFullPath(configuration.AutoSlayLogPath),
            FailureScreenshotPath = Path.GetFullPath(configuration.FailureScreenshotPath)
        };
    }
}

[JsonSerializable(typeof(SmokeConfiguration))]
[JsonSerializable(typeof(SmokeCheckpoint))]
internal partial class SmokeJsonContext : JsonSerializerContext;

internal sealed record SmokeCheckpoint(
    int SchemaVersion,
    string CandidateSha,
    string Phase,
    string Name,
    long Sequence,
    string Status,
    long ElapsedMilliseconds,
    object? Data = null);
