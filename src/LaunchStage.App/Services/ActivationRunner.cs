using LaunchStage.Core.Engine;
using LaunchStage.Core.Logging;
using LaunchStage.Core.Models;
using LaunchStage.Core.Storage;
using LaunchStageApp.Views;

namespace LaunchStageApp.Services;

/// <summary>
/// Runs a profile in the background while the status popup shows progress. Only one profile runs at a time;
/// the launcher's controls are locked meanwhile (BusyChanged).
/// </summary>
internal sealed class ActivationRunner
{
    public bool IsBusy { get; private set; }

    public event Action<bool>? BusyChanged;

    public event Action<Profile, ActivationResult, StatusPopup>? Finished;

    /// <summary>Activates a profile. Must be called on the UI thread.</summary>
    public Task RunAsync(string? profileName) => RunAsync(profileName, closing: false);

    /// <summary>Closes a profile's apps. Must be called on the UI thread.</summary>
    public Task CloseAsync(string? profileName) => RunAsync(profileName, closing: true);

    /// <summary>
    /// Re-snap: puts the profile's open windows back in their spots. Only moves windows, so no PIN is asked.
    /// Must be called on the UI thread.
    /// </summary>
    public async Task ResnapAsync(string? profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return;
        }

        if (IsBusy)
        {
            Log.Info($"Ignored putting '{profileName}' back: another profile is still being set up.");
            return;
        }

        Profile? profile = null;
        try
        {
            profile = ProfileStore.Find(profileName);
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't read '{profileName}': {ex.Message}");
        }

        if (profile == null)
        {
            var missing = new StatusPopup(profileName, resnapping: true);
            missing.ShowProblem($"There's no profile named '{profileName}'.");
            missing.Show();
            return;
        }

        SetBusy(true);
        var popup = new StatusPopup(profile.Name, resnapping: true);
        popup.Show();
        ActivationResult result;
        try
        {
            var found = profile;
            result = await Task.Run(() => ProfileResnapper.Resnap(found));
        }
        catch (Exception ex)
        {
            Log.Error($"Putting '{profile.Name}' back crashed: {ex}");
            result = new ActivationResult();
            result.Problems.Add($"Something went wrong: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }

        popup.ShowResult(result, profile.Name);
    }

    private async Task RunAsync(string? profileName, bool closing)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return;
        }

        if (IsBusy)
        {
            Log.Info($"Ignored '{profileName}': another profile is still being set up.");
            return;
        }

        Profile? found = null;
        string? readError = null;
        try
        {
            found = ProfileStore.Find(profileName);
        }
        catch (Exception ex)
        {
            readError = ex.Message;
        }

        if (found == null)
        {
            var missing = new StatusPopup(profileName, closing);
            missing.ShowProblem(readError != null
                ? $"The profile '{profileName}' couldn't be read: {readError}"
                : $"There's no profile named '{profileName}'.");
            missing.Show();
            return;
        }

        Profile profile = found;
        SetBusy(true);

        // Private profiles need their PIN first (busy meanwhile, so a second press can't slip past the prompt).
        if (!PinGate.Unlock(profile, closing ? "close it" : "open it"))
        {
            Log.Info($"'{profile.Name}' is private and the PIN wasn't entered; nothing was {(closing ? "closed" : "opened")}.");
            SetBusy(false);
            return;
        }

        var popup = new StatusPopup(profile.Name, closing);
        popup.Show();

        var ui = new PopupActivationUi(popup);
        var settings = App.Instance.Settings;
        ActivationResult result;
        try
        {
            result = closing
                ? await Task.Run(() => new ProfileCloser(ui, settings).Close(profile))
                : await Task.Run(() => new ProfileActivator(ui, settings).Activate(profile));
        }
        catch (Exception ex)
        {
            Log.Error($"{(closing ? "Closing" : "Activating")} '{profile.Name}' crashed: {ex}");
            result = new ActivationResult();
            result.Problems.Add($"Something went wrong: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }

        popup.ShowResult(result, profile.Name);
        if (!closing)
        {
            Finished?.Invoke(profile, result, popup);
        }
    }

    private void SetBusy(bool busy)
    {
        IsBusy = busy;
        BusyChanged?.Invoke(busy);
    }

    /// <summary>Connects the engine (on a background thread) to the popup (on the UI thread).</summary>
    private sealed class PopupActivationUi : IActivationUi
    {
        private readonly StatusPopup _popup;

        public PopupActivationUi(StatusPopup popup) => _popup = popup;

        public void Status(string message) => _popup.Dispatcher.InvokeAsync(() => _popup.SetStatus(message));

        public CloseFailureChoice AskCloseFailed(string appName)
        {
            Task<CloseFailureChoice> answer = _popup.Dispatcher.Invoke(() => _popup.AskCloseFailedAsync(appName));
            return answer.GetAwaiter().GetResult();
        }
    }
}
