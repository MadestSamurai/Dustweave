namespace Dustweave;

// Resolve only an explicitly selected data/cache root. Descendants used for
// replacement or cleanup still pass the existing no-links checks.
public static class DailyStoragePath
{
    public static void RequireNoLinks(string path, string message = "storage.link")
    {
        for (string? part = Path.GetFullPath(path); part != null; part = Path.GetDirectoryName(part))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(part); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException(message + ": " + part);
        }
    }
    public static string ResolveRoot(string path) => Resolve(Path.GetFullPath(path), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    private static string Resolve(string path, HashSet<string> links)
    {
        string current = Path.GetPathRoot(path)!;
        foreach (string part in path[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) == 0) continue;
            if (!links.Add(current) || links.Count > 32 || (attributes & FileAttributes.Directory) == 0)
                throw Unavailable(current);
            try
            {
                var target = new DirectoryInfo(current).ResolveLinkTarget(true);
                if (target == null || !Directory.Exists(target.FullName)) throw Unavailable(current);
                current = Resolve(Path.GetFullPath(target.FullName), links);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { throw Unavailable(current, error); }
        }
        return Path.TrimEndingDirectorySeparator(current);
    }
    private static IOException Unavailable(string path, Exception? inner = null)
        => new("storage.path_unavailable: " + path, inner);
}
