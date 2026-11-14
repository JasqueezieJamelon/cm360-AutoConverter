using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SensConverter.Core;

/// <summary>Identifying a game by its exe: a content hash, plus a guess at a readable name.</summary>
public static class ExeIdentity
{
    /// <summary>Tokens that are build plumbing rather than part of a game name.</summary>
    private static readonly string[] NoiseTokens =
    {
        "win64", "win32", "x64", "x86", "shipping", "release", "debug", "final", "retail", "launcher",
    };

    /// <summary>SHA-256 of a file, lowercase hex. Streams, so a 200 MB exe is fine.</summary>
    public static string Sha256File(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20);
        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(stream);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Turns something like "my_game-Win64-Shipping.exe" into "My Game". Only a starting
    /// suggestion; the user can rename freely.
    /// </summary>
    public static string PrettyNameFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Unnamed game";
        }

        string raw = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "Unnamed game";
        }

        string[] words = raw
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Replace('.', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => !NoiseTokens.Contains(w, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (words.Length == 0)
        {
            return raw;
        }

        StringBuilder builder = new();
        foreach (string word in words)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            // Leave existing capitalisation alone (DOOM, GTAV); only fix all-lowercase words.
            builder.Append(word.All(char.IsLower)
                ? CultureInfo.CurrentCulture.TextInfo.ToTitleCase(word)
                : word);
        }

        return builder.ToString();
    }
}
