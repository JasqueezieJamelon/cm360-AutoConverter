namespace SensConverter.Core;

/// <summary>The saved list of calibrated games.</summary>
public sealed class GameLibrary
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<GameEntry> Games { get; set; } = new();

    public void Normalize()
    {
        Games ??= new List<GameEntry>();
        foreach (GameEntry game in Games)
        {
            game.Normalize();
        }
    }

    /// <summary>Finds a game by exe contents, which survives the game being moved or re-pathed.</summary>
    public GameEntry? FindByHash(string? exeHash)
        => string.IsNullOrWhiteSpace(exeHash)
            ? null
            : Games.FirstOrDefault(g => string.Equals(g.ExeHash, exeHash, StringComparison.OrdinalIgnoreCase));

    public GameEntry? FindByPath(string? exePath)
        => string.IsNullOrWhiteSpace(exePath)
            ? null
            : Games.FirstOrDefault(g => string.Equals(g.ExePath, exePath, StringComparison.OrdinalIgnoreCase));

    public GameEntry? FindById(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : Games.FirstOrDefault(g => g.Id == id);

    public void Upsert(GameEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        int index = Games.FindIndex(g => g.Id == entry.Id);
        if (index >= 0)
        {
            Games[index] = entry;
        }
        else
        {
            Games.Add(entry);
        }
    }

    public bool Remove(string id)
    {
        int index = Games.FindIndex(g => g.Id == id);
        if (index < 0)
        {
            return false;
        }

        Games.RemoveAt(index);
        return true;
    }

    public void SortByName()
        => Games.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
}
