using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Aiyara.Harness.Cli;

/// <summary>
/// Windows console mode interop. Enables ANSI/VT escape processing so truecolor and box-drawing
/// output render correctly both in Windows Terminal (which already enables it via ConPTY) and in
/// the legacy conhost.exe window (cmd.exe/PowerShell hosts), which leaves it off by default.
/// </summary>
internal static class NativeConsole
{
    private const int StdOutputHandle = -11;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);

    [SupportedOSPlatform("windows")]
    public static void EnableAnsiColors()
    {
        var handle = GetStdHandle(StdOutputHandle);
        if (handle == nint.Zero || handle == new nint(-1)) return;
        if (!GetConsoleMode(handle, out var mode)) return; // redirected/non-console handle

        SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
    }
}
