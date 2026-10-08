using System.Windows;
using LaunchStage.Core.Models;
using LaunchStageApp.Views;

namespace LaunchStageApp.Services;

/// <summary>Asks for the PIN before anything happens to a private profile. Must be called on the UI thread.</summary>
internal static class PinGate
{
    /// <summary>True when the profile isn't private or the right PIN was entered. "action" finishes "Enter its PIN to ...".</summary>
    public static bool Unlock(Profile profile, string action)
    {
        if (!profile.IsPrivate)
        {
            return true;
        }

        var dialog = new PinDialog(profile, action);
        var owner = Application.Current.Windows.OfType<Window>()
            .FirstOrDefault(w => w.IsActive && w.IsVisible && w is not StatusPopup && w is not PinDialog);
        if (owner != null)
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        return dialog.ShowDialog() == true;
    }
}
