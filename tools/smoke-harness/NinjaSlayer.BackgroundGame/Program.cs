using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

if (args.Length == 0 || !File.Exists(args[0])) throw new ArgumentException("Expected a game executable.");
string desktopName = "NinjaSlayerTheater-" + Environment.ProcessId;
nint desktop = Native.CreateDesktop(desktopName, 0, 0, 0, 0x000F01FF, 0);
if (desktop == 0) throw new Win32Exception();
var startup = new Native.StartupInfo
{
    cb = Marshal.SizeOf<Native.StartupInfo>(), lpDesktop = @"winsta0\" + desktopName,
    dwFlags = 0x101, wShowWindow = 0,
    hStdInput = Native.GetStdHandle(-10), hStdOutput = Native.GetStdHandle(-11), hStdError = Native.GetStdHandle(-12)
};
string Quote(string arg) => "\"" + arg.Replace("\"", "\\\"") + "\"";
var command = new StringBuilder(string.Join(" ", args.Select(Quote)));
try
{
    if (!Native.CreateProcess(args[0], command, 0, 0, true, 0, 0, Path.GetDirectoryName(args[0]), ref startup, out var process))
        throw new Win32Exception();
    const string prefix = "--ninjaslayer-smoke-config=";
    string configuration = args.Single(argument => argument.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..];
    File.WriteAllText(Path.Combine(Path.GetDirectoryName(configuration)!, "game.pid"), process.processId.ToString());
    Native.CloseHandle(process.hThread);
    try
    {
        while (Native.WaitForSingleObject(process.hProcess, 500) == 258) { }
        if (!Native.GetExitCodeProcess(process.hProcess, out uint exitCode)) throw new Win32Exception();
        return (int)exitCode;
    }
    finally { Native.CloseHandle(process.hProcess); }
}
finally { Native.CloseDesktop(desktop); }

static class Native
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct StartupInfo
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public nint lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInfo { public nint hProcess, hThread; public int processId, threadId; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateDesktop(string name, nint device, nint devmode, int flags, uint access, nint attributes);
    [DllImport("user32.dll")] internal static extern bool CloseDesktop(nint desktop);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool CreateProcess(string application, StringBuilder command, nint processAttributes,
        nint threadAttributes, bool inheritHandles, uint flags, nint environment, string? directory,
        ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32.dll")] internal static extern nint GetStdHandle(int which);
    [DllImport("kernel32.dll")] internal static extern uint WaitForSingleObject(nint handle, uint ms);
    [DllImport("kernel32.dll")] internal static extern bool GetExitCodeProcess(nint process, out uint code);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(nint handle);
}
