using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using HarmonyLib;
using NinjaSlayer.Content;

namespace NinjaSlayer.SmokeDriver;

// Instrumentation belongs to the harness, not the shipped collector.
internal static class TelemetryPerformance
{
    private static readonly Dictionary<string, Measurement> Samples = new();
    internal static void Start()
    {
        var harmony = new Harmony("NinjaSlayer.SmokeDriver.TelemetryPerformance");
        var assembly = typeof(NinjaSlayerSettings).Assembly;
        foreach (var (type, method) in new[] { ("NinjaSlayer.Code.Telemetry.NinjaSlayerCombatTelemetry", "Emit"), ("NinjaSlayer.Content.NinjaSlayerBalanceTelemetry", "ObserveRunEnded") })
            harmony.Patch(AccessTools.Method(assembly.GetType(type, true)!, method),
                prefix: new HarmonyMethod(typeof(TelemetryPerformance), nameof(Before)),
                postfix: new HarmonyMethod(typeof(TelemetryPerformance), nameof(After)));
    }
    private static void Before(out (long Time, long Bytes) __state) =>
        __state = (Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread());
    private static void After(MethodBase __originalMethod, (long Time, long Bytes) __state)
    {
        string key = __originalMethod.Name;
        if (!Samples.TryGetValue(key, out var sample)) Samples.Add(key, sample = new());
        double elapsed = Stopwatch.GetElapsedTime(__state.Time).TotalMilliseconds;
        sample.Calls++; sample.Milliseconds += elapsed; sample.MaximumMilliseconds = Math.Max(sample.MaximumMilliseconds, elapsed);
        sample.AllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - __state.Bytes;
    }
    internal static JsonObject Snapshot() => new()
    {
        ["syncMethods"] = JsonSerializer.SerializeToNode(Samples),
        ["gamePeakWorkingSetBytes"] = Process.GetCurrentProcess().PeakWorkingSet64
    };
    private sealed class Measurement
    {
        public long Calls { get; set; }
        public double Milliseconds { get; set; }
        public double MaximumMilliseconds { get; set; }
        public long AllocatedBytes { get; set; }
    }
}
