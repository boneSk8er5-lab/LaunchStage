using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LaunchStage.Core.Engine;
using LaunchStage.Core.Logging;
using LaunchStage.Core.Models;
using LaunchStageApp.Services;

namespace LaunchStageApp.Views;

/// <summary>
/// Asks for a private profile's 4-digit PIN. After 5 wrong tries the profile is locked for a minute
/// (counted per profile while LaunchStage is running).
/// </summary>
public partial class PinDialog : Window
{
    private const int MaxTries = 5;
    private static readonly TimeSpan LockTime = TimeSpan.FromSeconds(60);
    private static readonly Dictionary<string, (int Fails, DateTime LockedUntil)> Attempts = new(StringComparer.OrdinalIgnoreCase);

    private readonly Profile _profile;
    private readonly DispatcherTimer _lockTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public PinDialog(Profile profile, string action)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _profile = profile;
        TitleText.Text = $"{profile.Name} is private";
        ActionText.Text = $"Enter its 4-digit PIN to {action}.";

        _lockTimer.Tick += (_, _) => UpdateLock();
        Closed += (_, _) => _lockTimer.Stop();
        Loaded += (_, _) =>
        {
            UpdateLock();
            Activate();
            PinBox.Focus();
        };
    }

    private void PinBox_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsAsciiDigit);

    private void PinBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (PinBox.Password.Length == 4)
        {
            TryUnlock();
        }
    }

    private void Unlock_Click(object sender, RoutedEventArgs e) => TryUnlock();

    private void TryUnlock()
    {
        if (IsLocked(out _))
        {
            return;
        }

        string pin = PinBox.Password;
        if (pin.Length != 4)
        {
            ShowError("The PIN is 4 digits.");
            return;
        }

        if (ProfilePin.Verify(_profile, pin))
        {
            Attempts.Remove(_profile.Name);
            DialogResult = true;
            return;
        }

        Attempts.TryGetValue(_profile.Name, out var state);
        int fails = state.Fails + 1;
        Log.Info($"Wrong PIN for private profile '{_profile.Name}' ({fails} of {MaxTries}).");
        PinBox.Clear();

        if (fails >= MaxTries)
        {
            Attempts[_profile.Name] = (0, DateTime.UtcNow + LockTime);
            UpdateLock();
            return;
        }

        Attempts[_profile.Name] = (fails, DateTime.MinValue);
        int left = MaxTries - fails;
        ShowError(left == 1 ? "Wrong PIN. 1 try left." : $"Wrong PIN. {left} tries left.");
    }

    private bool IsLocked(out int secondsLeft)
    {
        secondsLeft = 0;
        if (Attempts.TryGetValue(_profile.Name, out var state) && state.LockedUntil > DateTime.UtcNow)
        {
            secondsLeft = (int)Math.Ceiling((state.LockedUntil - DateTime.UtcNow).TotalSeconds);
            return true;
        }

        return false;
    }

    private void UpdateLock()
    {
        if (IsLocked(out int seconds))
        {
            PinBox.IsEnabled = false;
            UnlockButton.IsEnabled = false;
            ShowError($"Too many wrong tries. Try again in {seconds} seconds.");
            _lockTimer.Start();
            return;
        }

        _lockTimer.Stop();
        if (!PinBox.IsEnabled)
        {
            PinBox.IsEnabled = true;
            UnlockButton.IsEnabled = true;
            ErrorText.Visibility = Visibility.Collapsed;
            PinBox.Focus();
        }
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
    }
}
