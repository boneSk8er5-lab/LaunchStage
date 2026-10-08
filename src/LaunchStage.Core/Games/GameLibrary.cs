using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LaunchStage.Core.Logging;
using LaunchStage.Core.Models;
using LaunchStage.Core.Storage;
using Microsoft.Win32;

namespace LaunchStage.Core.Games;

public enum GameSource
{
    Manual,
    Steam,
    Epic
}

/// <summary>One game the picker can start.</summary>
public sealed class GameEntry
{
    /// <summary>"steam:570", "epic:Fortnite" or "manual:&lt;guid&gt;". Stays the same between scans.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";
    public GameSource Source { get; set; }

    /// <summary>What to open: an .exe, a shortcut, or a link such as steam://rungameid/570.</summary>
    public string Launch { get; set; } = "";

    public string? Arguments { get; set; }
    public DateTime? LastPlayed { get; set; }
    public bool Hidden { get; set; }
    public bool Favorite { get; set; }
}

/// <summary>
/// The games LaunchStage knows about: installed Steam and Epic games (found automatically) plus games you add
/// yourself. What you've played, hidden or starred is kept in %AppData%\LaunchStage\games.json.
/// </summary>
public static class GameLibrary
{
    public static string FilePath => Path.Combine(AppPaths.Root, "games.json");

    /// <summary>
    /// Everything: saved games merged with what's installed right now. Hidden games are included (check Hidden).
    /// Store games that are no longer installed drop out (they can't start); games you added yourself always stay.
    /// </summary>
    public static List<GameEntry> Load()
    {
        var installed = FindSteamGames().Concat(FindEpicGames()).ToList();
        var installedIds = new HashSet<string>(installed.Select(g => g.Id), StringComparer.OrdinalIgnoreCase);
        var byId = ReadSaved()
            .Where(g => g.Source == GameSource.Manual || installedIds.Contains(g.Id))
            .GroupBy(g => g.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var found in installed)
        {
            if (byId.TryGetValue(found.Id, out var known))
            {
                // Keep your history and choices; refresh what the store says.
                known.Name = found.Name;
                known.Launch = found.Launch;
                known.Source = found.Source;
            }
            else
            {
                byId[found.Id] = found;
            }
        }

        return byId.Values.ToList();
    }

    /// <summary>Favorites first, then most recently played, then A to Z.</summary>
    public static List<GameEntry> Sorted(IEnumerable<GameEntry> games) =>
        games.OrderByDescending(g => g.Favorite)
            .ThenByDescending(g => g.LastPlayed ?? DateTime.MinValue)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static void Save(IEnumerable<GameEntry> games)
    {
        try
        {
            AppPaths.EnsureFolders();
            // Only what matters to keep: manual games, and store games with history or a choice made.
            var keep = games.Where(g => g.Source == GameSource.Manual || g.LastPlayed != null || g.Hidden || g.Favorite).ToList();
            string temp = FilePath + ".tmp";
            System.IO.File.WriteAllText(temp, JsonSerializer.Serialize(keep, ProfileStore.JsonOptions));
            System.IO.File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't save the game list: {ex.Message}");
        }
    }

    /// <summary>A game added by hand from its .exe or shortcut.</summary>
    public static GameEntry CreateManual(string path) => new()
    {
        Id = "manual:" + Guid.NewGuid().ToString("N"),
        Name = Path.GetFileNameWithoutExtension(path),
        Source = GameSource.Manual,
        Launch = path
    };

    /// <summary>The game as a profile app, so the normal launcher can start it (admin handling included).</summary>
    public static AppEntry ToAppEntry(GameEntry game) => new()
    {
        Name = game.Name,
        Type = AppType.Game,
        Behavior = AppBehavior.LaunchOnly,
        Path = game.Launch,
        Arguments = game.Arguments
    };

    private static List<GameEntry> ReadSaved()
    {
        try
        {
            if (System.IO.File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<List<GameEntry>>(System.IO.File.ReadAllText(FilePath), ProfileStore.JsonOptions) ?? new();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't read the game list: {ex.Message}");
        }

        return new List<GameEntry>();
    }

    // ---------------- Steam ----------------

    // Steam's own tools that show up as "installed apps".
    private static readonly string[] SteamToolWords =
        { "Redistributable", "Proton", "Steam Linux Runtime", "Steamworks", "SteamVR", "Dedicated Server", "Soundtrack" };

    private static List<GameEntry> FindSteamGames()
    {
        var games = new List<GameEntry>();
        try
        {
            string? steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            steam ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            steam = steam.Replace('/', '\\');

            var libraries = new List<string> { steam };
            string libraryFile = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (System.IO.File.Exists(libraryFile))
            {
                foreach (Match match in Regex.Matches(System.IO.File.ReadAllText(libraryFile), "\"path\"\\s+\"([^\"]+)\""))
                {
                    libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
                }
            }

            foreach (string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string apps = Path.Combine(library, "steamapps");
                if (!Directory.Exists(apps))
                {
                    continue;
                }

                foreach (string manifest in Directory.GetFiles(apps, "appmanifest_*.acf"))
                {
                    string text = System.IO.File.ReadAllText(manifest);
                    string? appId = VdfValue(text, "appid");
                    string? name = VdfValue(text, "name");
                    bool installed = int.TryParse(VdfValue(text, "StateFlags"), out int flags) && (flags & 4) != 0;
                    if (appId == null || name == null || !installed
                        || SteamToolWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    games.Add(new GameEntry
                    {
                        Id = "steam:" + appId,
                        Name = name,
                        Source = GameSource.Steam,
                        Launch = "steam://rungameid/" + appId
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Couldn't read the Steam library: {ex.Message}");
        }

        return games.GroupBy(g => g.Id).Select(g => g.First()).ToList();
    }

    private static string? VdfValue(string text, string key)
    {
        var match = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    // ---------------- Epic ----------------

    private static List<GameEntry> FindEpicGames()
    {
        var games = new List<GameEntry>();
        try
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (!Directory.Exists(folder))
            {
                return games;
            }

            foreach (string file in Directory.GetFiles(folder, "*.item"))
            {
                try
                {
                    var node = JsonNode.Parse(System.IO.File.ReadAllText(file));
                    string? name = node?["DisplayName"]?.GetValue<string>();
                    string? appName = node?["AppName"]?.GetValue<string>();
                    string? ns = node?["CatalogNamespace"]?.GetValue<string>();
                    string? itemId = node?["CatalogItemId"]?.GetValue<string>();
                    var categories = node?["AppCategories"]?.AsArray().Select(c => c?.GetValue<string>() ?? "").ToList();
                    if (name == null || appName == null || ns == null || itemId == null)
                    {
                        continue;
                    }

                    if (categories != null && categories.Count > 0 && !categories.Contains("games", StringComparer.OrdinalIgnoreCase))
                    {
                        continue; // Unreal Engine, plugins and other non-games
                    }

                    games.Add(new GameEntry
                    {
                        Id = "epic:" + appName,
                        Name = name,
                        Source = GameSource.Epic,
                        Launch = $"com.epicgames.launcher://apps/{ns}%3A{itemId}%3A{appName}?action=launch&silent=true"
                    });
                }
                catch (Exception ex)
                {
                    Log.Debug($"Skipped Epic manifest {file}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Couldn't read the Epic library: {ex.Message}");
        }

        return games;
    }
}
