using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using LaunchStage.Core.Logging;
using LaunchStage.Core.Models;

namespace LaunchStage.Core.Storage;

/// <summary>Reads and writes profiles: one .json file per profile in the Profiles folder.</summary>
public static class ProfileStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string FileFor(string profileName) =>
        Path.Combine(AppPaths.ProfilesFolder, SafeFileName(profileName) + ".json");

    public static IReadOnlyList<Profile> LoadAll()
    {
        AppPaths.EnsureFolders();
        var profiles = new List<Profile>();
        foreach (string file in Directory.GetFiles(AppPaths.ProfilesFolder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                profiles.Add(Load(file));
            }
            catch (Exception ex)
            {
                Log.Error($"Couldn't read profile file {file}: {ex.Message}");
            }
        }

        return profiles;
    }

    /// <summary>Finds a profile by name (not case-sensitive). Returns null when there isn't one.</summary>
    public static Profile? Find(string profileName)
    {
        string file = FileFor(profileName);
        if (File.Exists(file))
        {
            return Load(file);
        }

        return LoadAll().FirstOrDefault(p => string.Equals(p.Name, profileName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static Profile Load(string file)
    {
        string json = File.ReadAllText(file);
        var profile = JsonSerializer.Deserialize<Profile>(json, JsonOptions)
                      ?? throw new InvalidDataException($"{file} is empty.");
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            profile.Name = Path.GetFileNameWithoutExtension(file);
        }

        return profile;
    }

    public static void Save(Profile profile)
    {
        AppPaths.EnsureFolders();
        string file = FileFor(profile.Name);
        string temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(profile, JsonOptions));
        File.Move(temp, file, overwrite: true);
        Log.Info($"Saved profile '{profile.Name}' to {file}.");
    }

    public static void Delete(string profileName)
    {
        string file = FileFor(profileName);
        if (File.Exists(file))
        {
            File.Delete(file);
            Log.Info($"Deleted profile '{profileName}'.");
        }
    }

    /// <summary>
    /// Saves a full copy of a profile as "Stream (2)": apps, window spots, options, icon, image and PIN. The copy
    /// isn't a favorite (so there aren't two starred cards) and has no hotkeys (a key can only do one thing).
    /// </summary>
    public static Profile Duplicate(Profile profile)
    {
        var copy = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(profile, JsonOptions), JsonOptions)!;
        copy.Name = UniqueName(profile.Name);
        copy.Favorite = false;
        copy.Hotkey = null;
        copy.ResnapHotkey = null;
        Save(copy);
        Log.Info($"Duplicated profile '{profile.Name}' as '{copy.Name}'.");
        return copy;
    }

    /// <summary>"Stream", or "Stream (2)" if that name is taken.</summary>
    public static string UniqueName(string baseName)
    {
        string name = baseName.Trim();
        if (!File.Exists(FileFor(name)))
        {
            return name;
        }

        for (int i = 2; ; i++)
        {
            string candidate = $"{name} ({i})";
            if (!File.Exists(FileFor(candidate)))
            {
                return candidate;
            }
        }
    }

    /// <summary>Saves a profile, plus its image, as a single .lsprofile file (a zip) for backup or sharing.</summary>
    public static void Export(Profile profile, string destination)
    {
        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        using var zip = ZipFile.Open(destination, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("profile.json");
        using (var writer = new StreamWriter(entry.Open()))
        {
            writer.Write(JsonSerializer.Serialize(profile, JsonOptions));
        }

        foreach (string? fileName in new[] { profile.Image, profile.Icon })
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            string imagePath = Path.Combine(AppPaths.ImagesFolder, Path.GetFileName(fileName));
            if (File.Exists(imagePath))
            {
                zip.CreateEntryFromFile(imagePath, "Images/" + Path.GetFileName(fileName));
            }
        }

        Log.Info($"Exported profile '{profile.Name}' to {destination}.");
    }

    /// <summary>Adds an exported profile (.lsprofile or .json). A name that's already taken gets " (2)" added.</summary>
    public static Profile Import(string source)
    {
        AppPaths.EnsureFolders();
        Profile profile;

        if (source.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            profile = Load(source);
        }
        else
        {
            using var zip = ZipFile.OpenRead(source);
            var entry = zip.GetEntry("profile.json")
                        ?? throw new InvalidDataException("This file doesn't contain a LaunchStage profile.");
            using (var reader = new StreamReader(entry.Open()))
            {
                profile = JsonSerializer.Deserialize<Profile>(reader.ReadToEnd(), JsonOptions)
                          ?? throw new InvalidDataException("The profile inside this file is empty.");
            }

            foreach (var image in zip.Entries.Where(e => e.FullName.StartsWith("Images/", StringComparison.Ordinal) && e.Name.Length > 0))
            {
                image.ExtractToFile(Path.Combine(AppPaths.ImagesFolder, image.Name), overwrite: true);
            }
        }

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            profile.Name = Path.GetFileNameWithoutExtension(source);
        }

        profile.Name = UniqueName(profile.Name);
        Save(profile);
        Log.Info($"Imported profile '{profile.Name}' from {source}.");
        return profile;
    }

    public static string SafeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.Length == 0 ? "Profile" : cleaned;
    }
}
