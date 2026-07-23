using System.Diagnostics;
using System.Text.Json;

namespace NinjaSlayer.SmokeDriver;

internal sealed class SmokeCheckpointWriter : IDisposable
{
    private readonly SmokeConfiguration _configuration;
    private readonly StreamWriter _writer;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private long _sequence;

    public SmokeCheckpointWriter(SmokeConfiguration configuration)
    {
        _configuration = configuration;
        Directory.CreateDirectory(Path.GetDirectoryName(configuration.CheckpointPath)!);
        _writer = new StreamWriter(configuration.CheckpointPath, append: true) { AutoFlush = true };
    }

    public void Write(string name, string status = "passed", object? data = null)
    {
        var checkpoint = new SmokeCheckpoint(
            1,
            _configuration.CandidateSha.ToLowerInvariant(),
            _configuration.Phase.ToString().ToLowerInvariant(),
            name,
            Interlocked.Increment(ref _sequence),
            status,
            _stopwatch.ElapsedMilliseconds,
            data);
        _writer.WriteLine(JsonSerializer.Serialize(checkpoint, SmokeJsonContext.Default.SmokeCheckpoint));
    }

    public void Dispose() => _writer.Dispose();
}
