namespace LaunchStage.Core.Storage;

/// <summary>
/// Where LaunchStage keeps its files: %AppData%\LaunchStage. The LAUNCHSTAGE_DATA environment variable points it
/// somewhere else instead (only used by tools/GuidePictures, which takes the guide's pictures with demo profiles).
/// </summary>
public static class AppPaths
{
    public static string Root { get; } =
        Environment.GetEnvironmentVariable("LAUNCHSTAGE_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LaunchStage");

    public static string ProfilesFolder => Path.Combine(Root, "Profiles");
    public static string ImagesFolder => Path.Combine(ProfilesFolder, "Images");
    public static string LogsFolder => Path.Combine(Root, "Logs");
    public static string LogFile => Path.Combine(LogsFolder, "debug_log.txt");
    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static void EnsureFolders()
    {
        Directory.CreateDirectory(ProfilesFolder);
        Directory.CreateDirectory(ImagesFolder);
        Directory.CreateDirectory(LogsFolder);
    }
}
