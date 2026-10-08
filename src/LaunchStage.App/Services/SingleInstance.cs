using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using LaunchStage.Core.Logging;

namespace LaunchStageApp.Services;

/// <summary>
/// Keeps one copy of LaunchStage running. A second launch (a Stream Deck press, a shortcut,
/// "LaunchStage.exe --activate Stream") hands its request to the running copy and exits.
/// Waiting for requests uses no CPU.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private static readonly string Id = "LaunchStage-" + Environment.UserName;

    private readonly Mutex? _mutex;
    private CancellationTokenSource? _listening;
    private bool _disposed;

    public SingleInstance()
    {
        try
        {
            _mutex = new Mutex(false, @"Local\" + Id, out bool createdNew);
            IsFirstInstance = createdNew;
        }
        catch (UnauthorizedAccessException)
        {
            // The running copy has administrator rights, so a normal copy isn't allowed to open its lock.
            // That still means "already running".
            IsFirstInstance = false;
        }
    }

    public bool IsFirstInstance { get; }

    private const int AnyProcess = -1;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>Sends a command ("show" or "activate") to the copy that's already running.</summary>
    public static bool Send(string command, string? argument)
    {
        try
        {
            // Let the running copy bring a window to the front (e.g. the PIN box for a private profile).
            AllowSetForegroundWindow(AnyProcess);

            using var client = new NamedPipeClientStream(".", Id, PipeDirection.Out);
            client.Connect(3000);
            using var writer = new StreamWriter(client);
            writer.Write(command + "\n" + (argument ?? ""));
            writer.Flush();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Couldn't reach the running LaunchStage: {ex.Message}");
            return false;
        }
    }

    /// <summary>Keeps trying for a while (used right after starting the administrator copy).</summary>
    public static bool SendWithRetry(string command, string? argument, TimeSpan timeout)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            if (Send(command, argument))
            {
                return true;
            }

            Thread.Sleep(500);
        }

        return false;
    }

    /// <summary>Listens for commands from later launches. <paramref name="onCommand"/> runs on a background thread.</summary>
    public void Listen(Action<string, string?> onCommand)
    {
        _listening = new CancellationTokenSource();
        CancellationToken token = _listening.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = CreateServer();
                    await server.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(server);
                    string message = await reader.ReadToEndAsync(token);
                    string[] parts = message.Split('\n', 2);
                    string command = parts[0].Trim().ToLowerInvariant();
                    string? argument = parts.Length > 1 && parts[1].Trim().Length > 0 ? parts[1].Trim() : null;
                    onCommand(command, argument);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Warn($"Command listener error: {ex.Message}");
                    await Task.Delay(500);
                }
            }
        });
    }

    /// <summary>
    /// Lets this user's other LaunchStage copies talk to this one, even when this copy runs as administrator
    /// and the other doesn't (Windows blocks that by default).
    /// </summary>
    private static NamedPipeServerStream CreateServer()
    {
        var security = new PipeSecurity();
        var user = WindowsIdentity.GetCurrent().User;
        if (user != null)
        {
            security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        }

        return NamedPipeServerStreamAcl.Create(
            Id, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listening?.Cancel();
        _mutex?.Dispose();
    }
}
