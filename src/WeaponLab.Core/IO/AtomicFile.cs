namespace WeaponLab.Core.IO;

/// <summary>
/// File writes that never leave a half-written target: write a sibling
/// temp file, then move it over the destination.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllBytes(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);

        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    public static void WriteAllText(string path, string text)
    {
        WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// Joins a forward-slash resource path under <paramref name="root"/> and
    /// rejects paths that would escape it.
    /// </summary>
    public static string SafeJoin(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Path escapes its folder: {relativePath}");
        }

        return full;
    }
}
