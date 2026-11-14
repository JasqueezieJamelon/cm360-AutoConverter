using System.Text.Json;
using System.Text.Json.Serialization;

namespace SensConverter.Core;

/// <summary>
/// Reads and writes the two json files. Writes go to a temp file first and are then moved over the
/// real one, so a crash mid-save cannot leave a half-written library behind. A file that will not
/// parse is renamed out of the way rather than deleted, and the user is told.
/// </summary>
public sealed class Storage
{
    public const string SettingsFileName = "settings.json";
    public const string LibraryFileName = "games.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly List<string> warnings = new();

    public Storage(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        DataDirectory = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(DataDirectory);
    }

    public string DataDirectory { get; }

    public string SettingsPath => Path.Combine(DataDirectory, SettingsFileName);

    public string LibraryPath => Path.Combine(DataDirectory, LibraryFileName);

    /// <summary>Problems worth showing the user once, such as a quarantined file.</summary>
    public IReadOnlyList<string> Warnings => warnings;

    public void ClearWarnings() => warnings.Clear();

    /// <summary>
    /// Portable by default: data sits next to the exe. If that folder is read-only (the program was
    /// dropped into Program Files, say), falls back to the user's local app data.
    /// </summary>
    public static Storage CreateDefault(string? overrideDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            return new Storage(overrideDirectory);
        }

        string appDirectory = AppContext.BaseDirectory;
        if (IsWritable(appDirectory))
        {
            return new Storage(appDirectory);
        }

        string fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SensConverter");
        return new Storage(fallback);
    }

    public AppSettings LoadSettings()
    {
        AppSettings settings = Load<AppSettings>(SettingsPath) ?? new AppSettings();
        settings.Normalize();
        return settings;
    }

    public void SaveSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        Save(SettingsPath, settings);
    }

    public GameLibrary LoadLibrary()
    {
        GameLibrary library = Load<GameLibrary>(LibraryPath) ?? new GameLibrary();
        library.Normalize();
        return library;
    }

    public void SaveLibrary(GameLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        library.SchemaVersion = GameLibrary.CurrentSchemaVersion;
        Save(LibraryPath, library);
    }

    private T? Load<T>(string path)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            string quarantined = Quarantine(path);
            warnings.Add($"{Path.GetFileName(path)} could not be read ({ex.Message.Trim()}). " +
                         $"It was renamed to {Path.GetFileName(quarantined)} and a fresh one started.");
            return null;
        }
    }

    private static void Save<T>(string path, T value)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions);
        string temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    private static string Quarantine(string path)
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string target = Path.Combine(
            Path.GetDirectoryName(path) ?? ".",
            $"{Path.GetFileNameWithoutExtension(path)}.bad-{stamp}{Path.GetExtension(path)}");

        try
        {
            File.Move(path, target, overwrite: true);
        }
        catch (IOException)
        {
            // Nothing more to do; the caller still falls back to defaults.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }

        return target;
    }

    private static bool IsWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string probe = Path.Combine(directory, ".write-test-" + Guid.NewGuid().ToString("n"));
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
