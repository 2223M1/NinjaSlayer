using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wasapi.CoreAudioApi.Interfaces;

public static class ProcessAudioClient
{
    internal static async Task<AudioClient> Activate(string directory)
    {
        string processFile = Path.Combine(directory, "game.pid");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!File.Exists(processFile)) await Task.Delay(10, timeout.Token);
        uint process = uint.Parse(await File.ReadAllTextAsync(processFile, timeout.Token));
        var parameters = new ActivationParameters { Type = 1, Process = process, Mode = 0 };
        nint memory = Marshal.AllocHGlobal(Marshal.SizeOf<ActivationParameters>());
        try
        {
            Marshal.StructureToPtr(parameters, memory, false);
            var blob = new BlobVariant { Type = 65, Size = Marshal.SizeOf<ActivationParameters>(), Data = memory };
            Guid iid = typeof(IAudioClient).GUID;
            var handler = new CompletionHandler();
            Marshal.ThrowExceptionForHR(ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref iid,
                ref blob, handler, out IActivateAudioInterfaceAsyncOperation operation));
            object activated = await handler.Completion.Task.WaitAsync(timeout.Token);
            GC.KeepAlive(operation);
            GC.KeepAlive(handler);
            return new AudioClient((IAudioClient)activated);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ActivationParameters { public int Type; public uint Process; public int Mode; }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct BlobVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public int Size;
        [FieldOffset(16)] public nint Data;
    }

    [ComVisible(true), Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAgileObject;

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class CompletionHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        internal readonly TaskCompletionSource<object> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            try
            {
                operation.GetActivateResult(out int result, out object activated);
                Marshal.ThrowExceptionForHR(result);
                Completion.TrySetResult(activated);
            }
            catch (Exception exception) { Completion.TrySetException(exception); }
        }
    }

    [DllImport("Mmdevapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int ActivateAudioInterfaceAsync(string path, ref Guid iid,
        ref BlobVariant parameters, IActivateAudioInterfaceCompletionHandler handler,
        out IActivateAudioInterfaceAsyncOperation operation);
}
