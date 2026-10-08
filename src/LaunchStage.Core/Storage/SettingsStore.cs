using System.Text.Json;
using LaunchStage.Core.Logging;
using LaunchStage.Core.Models;

namespace LaunchStage.Core.Storage;

public static class SettingsStore
{
    /// <summary>Loads settings, creating the file with defaults the first time.</summary>
    public static AppSettings Load()
    {
        AppPaths.EnsureFolders();
        if (!File.Exists(AppPaths.SettingsFile))
        {
            var defaults = new AppSettings();
            Save(defaults);
            return defaults;
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), ProfileStore.JsonOptions)
                   ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // Keep the user's file untouched so a typo can be fixed by hand.
            Log.Error($"Couldn't read settings.json, using defaults: {ex.Message}");
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        AppPaths.EnsureFolders();
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(settings, ProfileStore.JsonOptions));
    }
}
