using System.Diagnostics;

namespace LaunchStage.Core.Desktop;

/// <summary>Turns an .exe into a friendly name ("Code.exe" becomes "Visual Studio Code").</summary>
public static class AppNames
{
    private static readonly Dictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    public static string Get(string? exePath, string processName)
    {
        if (string.IsNullOrEmpty(exePath))
        {
            return processName;
        }

        lock (Gate)
        {
            if (Cache.TryGetValue(exePath, out string? cached))
            {
                return cached;
            }

            string name = processName;
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exePath);
                if (!string.IsNullOrWhiteSpace(info.FileDescription))
                {
                    name = info.FileDescription.Trim();
                }
                else if (!string.IsNullOrWhiteSpace(info.ProductName))
                {
                    name = info.ProductName.Trim();
                }
            }
            catch
            {
                // Some system files can't be read; the process name is fine.
            }

            Cache[exePath] = name;
            return name;
        }
    }
}
