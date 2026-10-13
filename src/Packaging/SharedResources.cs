using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

// Linked into resource-owning assemblies. Regular builds retain embedded resources;
// distribution builds share immutable, content-addressed files across the tools.
internal static class DustweaveSharedResources
{
    private const string IndexName = "Dustweave.PackageResources.txt";
    private static Dictionary<string, string> Index(Assembly assembly)
    {
        using var stream = assembly.GetManifestResourceStream(IndexName);
        if (stream == null) return new(StringComparer.Ordinal);
        using var reader = new StreamReader(stream);
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split('|');
            if (parts.Length != 2 || parts[1].Length != 64 || !parts[1].All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid shared resource index.");
            rows.Add(parts[0], parts[1]);
        }
        return rows;
    }
    public static string[] Names(Assembly assembly) => assembly.GetManifestResourceNames()
        .Where(n => n != IndexName).Concat(Index(assembly).Keys).OrderBy(n => n, StringComparer.Ordinal).ToArray();
    public static Stream? Open(Assembly assembly, string name)
    {
        var embedded = assembly.GetManifestResourceStream(name);
        if (embedded != null) return embedded;
        if (!Index(assembly).TryGetValue(name, out var hash)) return null;
        string path = Path.Combine(AppContext.BaseDirectory, "resources", "shared", hash.ToLowerInvariant() + ".bin");
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Shared resource is damaged: " + name + ". Extract the complete Dustweave package again.");
            stream.Position = 0;
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }
}
