using System.Runtime.InteropServices;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;

string directory = Path.GetFullPath(args[0]);
Directory.CreateDirectory(directory);
using var devices = new MMDeviceEnumerator();
using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
bool processLoopback = args.Length > 1 && args[1] == "process";
// The launcher waits for readiness before creating the isolated game process.
File.WriteAllText(Path.Combine(directory, "audio-ready"), "ready");
using var client = processLoopback ? await ProcessAudioClient.Activate(directory) : device.AudioClient;
WaveFormat format = processLoopback ? WaveFormat.CreateIeeeFloatWaveFormat(48000, 2) : client.MixFormat;
client.Initialize(AudioClientShareMode.Shared, AudioClientStreamFlags.Loopback,
    1_000_000, 0, format, Guid.Empty);
using var capture = client.AudioCaptureClient;
using var writer = new WaveFileWriter(Path.Combine(directory, "audio.wav"), format);
using var packets = new StreamWriter(Path.Combine(directory, "audio-packets.jsonl"));
long? firstQpc = null;
long writtenFrames = 0, silenceFrames = 0;
int discontinuities = 0;
byte[] zeros = new byte[format.AverageBytesPerSecond];
client.Start();
DateTime? finish = null;
try
{
    while (finish is null || DateTime.UtcNow < finish)
    {
        while (capture.GetNextPacketSize() > 0)
        {
            IntPtr buffer = capture.GetBuffer(out int frames, out AudioClientBufferFlags flags,
                out long devicePosition, out long qpc);
            try
            {
                if ((flags & AudioClientBufferFlags.TimestampError) != 0 || qpc <= 0)
                    throw new IOException("WASAPI returned an invalid packet timestamp.");
                firstQpc ??= qpc;
                long startFrame = (long)Math.Round((qpc - firstQpc.Value) * format.SampleRate / 10_000_000d);
                long gap = startFrame - writtenFrames;
                // Small endpoint clock jitter must not insert a sample at every
                // packet. Real gaps retain their silence on the shared QPC timeline.
                if (gap > format.SampleRate / 500)
                {
                    silenceFrames += gap;
                    while (gap > 0)
                    {
                        int count = (int)Math.Min(gap, zeros.Length / format.BlockAlign);
                        writer.Write(zeros, 0, count * format.BlockAlign);
                        writtenFrames += count;
                        gap -= count;
                    }
                }
                if ((flags & AudioClientBufferFlags.DataDiscontinuity) != 0) discontinuities++;
                byte[] data = new byte[frames * format.BlockAlign];
                if ((flags & AudioClientBufferFlags.Silent) == 0) Marshal.Copy(buffer, data, 0, data.Length);
                writer.Write(data, 0, data.Length);
                packets.WriteLine(JsonSerializer.Serialize(new { qpc, devicePosition, frames, writtenFrames, flags = (int)flags }));
                writtenFrames += frames;
            }
            finally { capture.ReleaseBuffer(frames); }
        }
        if (finish is null && File.Exists(Path.Combine(directory, "recording-stop.json")))
            finish = DateTime.UtcNow.AddMilliseconds(200);
        Thread.Sleep(2);
    }
}
finally { client.Stop(); }
if (firstQpc is null) throw new IOException("No audio packets were captured.");
File.WriteAllText(Path.Combine(directory, "audio-start.json"), JsonSerializer.Serialize(new
{
    seconds = firstQpc.Value / 10_000_000d, sampleRate = format.SampleRate,
    writtenFrames, silenceFrames, discontinuities, timestampSource = "WASAPI packet QPC", processLoopback
}));
