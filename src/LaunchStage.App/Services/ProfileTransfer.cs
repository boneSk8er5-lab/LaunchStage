using System.IO;
using System.Windows;
using LaunchStage.Core.Logging;
using LaunchStage.Core.Models;
using LaunchStage.Core.Storage;
using Microsoft.Win32;

namespace LaunchStageApp.Services;

/// <summary>Import and export with file pickers.</summary>
internal static class ProfileTransfer
{
    private const string Filter = "LaunchStage profile (*.lsprofile)|*.lsprofile";

    /// <summary>Returns true when a profile was imported.</summary>
    public static bool Import(Window? owner)
    {
        var picker = new OpenFileDialog
        {
            Title = "Import a LaunchStage profile",
            Filter = "LaunchStage profile (*.lsprofile;*.json)|*.lsprofile;*.json"
        };

        bool? picked = owner != null ? picker.ShowDialog(owner) : picker.ShowDialog();
        if (picked != true)
        {
            return false;
        }

        try
        {
            var profile = ProfileStore.Import(picker.FileName);
            Dialogs.Info(owner, $"Imported '{profile.Name}'.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Import failed for {picker.FileName}: {ex}");
            Dialogs.Warn(owner, $"That file couldn't be imported.\n\n{ex.Message}");
            return false;
        }
    }

    public static void Export(Window? owner, Profile profile)
    {
        var picker = new SaveFileDialog
        {
            Title = $"Export {profile.Name}",
            FileName = ProfileStore.SafeFileName(profile.Name) + ".lsprofile",
            DefaultExt = ".lsprofile",
            Filter = Filter
        };

        bool? picked = owner != null ? picker.ShowDialog(owner) : picker.ShowDialog();
        if (picked != true)
        {
            return;
        }

        try
        {
            ProfileStore.Export(profile, picker.FileName);
            Dialogs.Info(owner, $"Exported '{profile.Name}' to:\n{picker.FileName}");
        }
        catch (Exception ex)
        {
            Log.Error($"Export failed: {ex}");
            Dialogs.Warn(owner, $"The profile couldn't be exported.\n\n{ex.Message}");
        }
    }
}
