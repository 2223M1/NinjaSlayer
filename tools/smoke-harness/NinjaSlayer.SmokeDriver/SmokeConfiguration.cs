using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NinjaSlayer.SmokeDriver;

internal enum SmokePhase
{
    Fresh,
    Resume,
    FullAutoSlay,
    SawatariSameCombat,
    ReverseFinisher,
    TransitionPerf
}

internal sealed record SmokeConfiguration(
    string CandidateSha,
    string Seed,
    SmokePhase Phase,
    string CheckpointPath,
    string AutoSlayLogPath,
    string FailureScreenshotPath,
    string? TransitionPerfOutputPath = null,
    string? TransitionVariant = null,
    bool? TransitionLoadLimitEnabled = null,
    bool? TransitionFinalizeBatchingEnabled = null,
    bool TransitionPerfWarmup = false)
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

        if (configuration.Phase == SmokePhase.TransitionPerf
            && (string.IsNullOrWhiteSpace(configuration.TransitionPerfOutputPath)
                || configuration.TransitionVariant is not ("baseline" or "load-limit-off" or "finalize-off")
                || configuration.TransitionLoadLimitEnabled is null
                || configuration.TransitionFinalizeBatchingEnabled is null))
        {
            throw new InvalidDataException("TransitionPerf requires an output path and exact component matrix.");
        }

        return configuration with
        {
            CheckpointPath = Path.GetFullPath(configuration.CheckpointPath),
            AutoSlayLogPath = Path.GetFullPath(configuration.AutoSlayLogPath),
            FailureScreenshotPath = Path.GetFullPath(configuration.FailureScreenshotPath),
            TransitionPerfOutputPath = configuration.TransitionPerfOutputPath is null
                ? null
                : Path.GetFullPath(configuration.TransitionPerfOutputPath)
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
    JsonNode? Data = null);
