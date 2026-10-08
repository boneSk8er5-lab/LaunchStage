using System.Diagnostics;
using System.IO;
using System.Reflection;
using LaunchStage.Core.Logging;

namespace LaunchStageApp.Services;

/// <summary>Version, copyright and license, shown in Help, Settings, the walkthrough and the guide.</summary>
internal static class AppInfo
{
    private static readonly Assembly Self = typeof(AppInfo).Assembly;

    public static string Version => Self.GetName().Version?.ToString(3) ?? "";

    /// <summary>"Copyright (c) 2026 Bones_84. All rights reserved." (from src\Directory.Build.props).</summary>
    public static string Copyright =>
        (Self.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "Copyright (c) Bones_84. All rights reserved.")
        .Replace("(c)", "©");

    /// <summary>The short version of the license.</summary>
    public const string UseNotice =
        "Free for personal use. Download it only from the official LaunchStage page on GitHub; please don't share or re-upload it.";

    /// <summary>Opens LICENSE.txt (next to LaunchStage.exe) in the normal text viewer.</summary>
    public static void OpenLicense()
    {
        string file = Path.Combine(AppContext.BaseDirectory, "LICENSE.txt");
        if (!File.Exists(file))
        {
            Dialogs.Info(null, Copyright + "\n\n" + UseNotice);
            return;
        }

        try
        {
            // Through Explorer, so it opens as a normal app even when LaunchStage runs as administrator.
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{file}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"Couldn't open the license: {ex.Message}");
            Dialogs.Info(null, Copyright + "\n\n" + UseNotice);
        }
    }
}
