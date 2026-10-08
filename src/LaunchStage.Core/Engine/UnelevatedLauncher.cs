using System.Runtime.InteropServices;
using System.Text;
using LaunchStage.Core.Native;

namespace LaunchStage.Core.Engine;

/// <summary>
/// When LaunchStage runs as administrator, anything it starts would normally be an admin app too.
/// This starts a program with the desktop's own (normal) rights instead, by borrowing the Windows shell's
/// security token. That keeps apps like Discord and Chrome running as normal apps.
/// </summary>
internal static class UnelevatedLauncher
{
    private const uint TokenRights =
        NativeMethods.TOKEN_QUERY |
        NativeMethods.TOKEN_ASSIGN_PRIMARY |
        NativeMethods.TOKEN_DUPLICATE |
        NativeMethods.TOKEN_ADJUST_DEFAULT |
        NativeMethods.TOKEN_ADJUST_SESSIONID;

    public static bool TryStart(string exePath, string? arguments, string? workingDirectory, out string? error)
    {
        error = null;
        IntPtr shellProcess = IntPtr.Zero;
        IntPtr shellToken = IntPtr.Zero;
        IntPtr normalToken = IntPtr.Zero;

        try
        {
            IntPtr shellWindow = NativeMethods.GetShellWindow();
            if (shellWindow == IntPtr.Zero)
            {
                error = "the Windows desktop isn't running";
                return false;
            }

            NativeMethods.GetWindowThreadProcessId(shellWindow, out uint shellProcessId);
            shellProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_INFORMATION, false, shellProcessId);
            if (shellProcess == IntPtr.Zero)
            {
                error = $"couldn't open the desktop process (error {Marshal.GetLastWin32Error()})";
                return false;
            }

            if (!NativeMethods.OpenProcessToken(shellProcess, NativeMethods.TOKEN_DUPLICATE, out shellToken))
            {
                error = $"couldn't read the desktop's rights (error {Marshal.GetLastWin32Error()})";
                return false;
            }

            if (!NativeMethods.DuplicateTokenEx(shellToken, TokenRights, IntPtr.Zero,
                    NativeMethods.SecurityImpersonation, NativeMethods.TokenPrimary, out normalToken))
            {
                error = $"couldn't copy the desktop's rights (error {Marshal.GetLastWin32Error()})";
                return false;
            }

            var commandLine = new StringBuilder($"\"{exePath}\"");
            if (!string.IsNullOrWhiteSpace(arguments))
            {
                commandLine.Append(' ').Append(arguments);
            }

            var startup = new NativeMethods.STARTUPINFO { cb = Marshal.SizeOf<NativeMethods.STARTUPINFO>() };
            if (!NativeMethods.CreateProcessWithTokenW(normalToken, 0, null, commandLine, 0, IntPtr.Zero,
                    string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory,
                    ref startup, out var info))
            {
                error = $"Windows refused to start it (error {Marshal.GetLastWin32Error()})";
                return false;
            }

            NativeMethods.CloseHandle(info.hThread);
            NativeMethods.CloseHandle(info.hProcess);
            return true;
        }
        finally
        {
            if (normalToken != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(normalToken);
            }

            if (shellToken != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(shellToken);
            }

            if (shellProcess != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(shellProcess);
            }
        }
    }
}
