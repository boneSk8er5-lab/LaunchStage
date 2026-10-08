using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using LaunchStage.Core.Desktop;
using LaunchStage.Core.Engine;
using LaunchStage.Core.Logging;
using LaunchStage.Core.Storage;

namespace LaunchStageApp.Services;

/// <summary>
/// Keeps %AppData%\LaunchStage\open-profiles.json up to date: the names of the profiles that are open right now
/// (same rule as the tray menu). The Stream Deck plugin reads it to light up the buttons of open profiles.
/// Checked every 2 seconds; the file is only rewritten when something changed, and removed when LaunchStage exits.
/// </summary>
internal sealed class OpenProfilesFile : IDisposable
{
    private static string FilePath => Path.Combine(AppPaths.Root, "open-profiles.json");

    private readonly DispatcherTimer _timer;
    private string? _lastWritten;
    private bool _checking;

    public OpenProfilesFile()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await CheckAsync();
        _timer.Start();
        _ = CheckAsync();
    }

    private async Task CheckAsync()
    {
        if (_checking)
        {
            return;
        }

        _checking = true;
        try
        {
            string json = await Task.Run(() =>
            {
                var windows = WindowFinder.GetAppWindows(includeNotInTaskbar: true);
                var open = ProfileStore.LoadAll()
                    .Where(p => WindowSnapshot.IsProfileOpen(p, windows))
                    .Select(p => p.Name)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return JsonSerializer.Serialize(open);
            });

            if (json != _lastWritten)
            {
                AppPaths.EnsureFolders();
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, FilePath, overwrite: true);
                _lastWritten = json;
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Couldn't update the open profiles list: {ex.Message}");
        }
        finally
        {
            _checking = false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        try
        {
            // LaunchStage isn't running, so it can't say what's open: no lit buttons.
            File.Delete(FilePath);
        }
        catch (Exception ex)
        {
            Log.Debug($"Couldn't remove the open profiles list: {ex.Message}");
        }
    }
}
