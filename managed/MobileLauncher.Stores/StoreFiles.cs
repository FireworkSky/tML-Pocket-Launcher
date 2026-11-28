using System.IO.Compression;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Reflection.Metadata;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MobileLauncher.Stores;

public static class StoreFiles
{
    public static string? TerrariaVersion(string? value) => value != null &&
        Regex.IsMatch(value.Trim(), @"^1\.\d{1,2}\.\d{1,2}(?:\.\d{1,2})?[a-z]?$", RegexOptions.CultureInvariant)
        ? value.Trim() : null;

    public static string? VersionProbePath(string path)
    {
        _ = ContentPath(path); // Apply the same path validation to probes.
        var name = path.Replace('\\', '/').Split('/').Last();
        return name is "Terraria.exe" or "Terraria.dll" or "TerrariaServer.exe" or "gameinfo"
            ? "_version/" + name : null;
    }

    // Read version metadata only. Imported desktop code is never loaded or executed.
    public static async Task PreserveVersionAsync(string directory, string? fallback, CancellationToken ct)
    {
        string? version = null;
        foreach (var name in new[] { "Terraria.exe", "Terraria.dll", "TerrariaServer.exe" })
        {
            var file = Path.Combine(directory, "_version", name);
            if (!File.Exists(file) || new FileInfo(file).Length > 64 * 1024 * 1024) continue;
            try
            {
                version = TerrariaVersion(FileVersionInfo.GetVersionInfo(file).ProductVersion);
                if (version == null)
                {
                    using var stream = File.OpenRead(file);
                    using var pe = new PEReader(stream);
                    if (pe.HasMetadata) version = TerrariaVersion(pe.GetMetadataReader().GetAssemblyDefinition().Version.ToString());
                }
            }
            catch (Exception error) when (error is IOException or BadImageFormatException or ArgumentException) { }
            if (version != null) break;
        }
        var gameInfo = Path.Combine(directory, "_version", "gameinfo");
        if (version == null && File.Exists(gameInfo) && new FileInfo(gameInfo).Length <= 20480)
        {
            var lines = await File.ReadAllLinesAsync(gameInfo, ct);
            if (lines.FirstOrDefault()?.Trim().Equals("Terraria", StringComparison.OrdinalIgnoreCase) == true)
                version = TerrariaVersion(lines.ElementAtOrDefault(1));
        }
        version ??= TerrariaVersion(fallback);
        if (version != null)
            await File.WriteAllTextAsync(Path.Combine(directory, "launcher-package.json"),
                JsonSerializer.Serialize(new { format = 1, appId = 105600, version }), ct);
    }

    public static string? ContentPath(string path)
    {
        path = path.Replace('\\', '/');
        var parts = path.Split('/');
        if (path.StartsWith('/') || parts.Any(p => p is ".." or "." || p.Contains(':') || p.Contains('\0')))
            throw new InvalidDataException("资源清单包含不安全路径");
        var index = Array.IndexOf(parts, "Content");
        return index < 0 || index == parts.Length - 1 ? null : string.Join('/', parts.Skip(index));
    }

    public static string Target(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("资源路径越界");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return full;
    }

    internal static async Task<bool> TryReuseContentAsync(string contentRoot, string relative,
        string destination, ulong size, byte[] expected, CancellationToken ct)
    {
        if (ContentPath(relative) != relative || !relative.StartsWith("Content/", StringComparison.Ordinal)) return false;
        var suffix = relative["Content/".Length..];
        if (Path.IsPathRooted(suffix)) return false;
        var source = new FileInfo(Path.Combine(contentRoot, suffix));
        try
        {
            if (!source.Exists || (ulong)source.Length != size) return false;
            await using var input = source.OpenRead();
            await using var output = File.Create(destination);
            await input.CopyToAsync(output, ct);
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        await using var copy = File.OpenRead(destination);
        return CryptographicOperations.FixedTimeEquals(await SHA1.HashDataAsync(copy, ct), expected);
    }

    public static async Task VerifySha1Async(string file, byte[] expected, CancellationToken ct)
    {
        await using var input = File.OpenRead(file);
        if (!CryptographicOperations.FixedTimeEquals(await SHA1.HashDataAsync(input, ct), expected))
            throw new InvalidDataException("Steam 资源 SHA-1 校验失败");
    }

    public static async Task<string> PackageAsync(string directory, CancellationToken ct)
    {
        if (!Directory.Exists(Path.Combine(directory, "Content", "Images")))
            throw new InvalidDataException("下载包缺少 Terraria Content/Images");
        var archive = directory + ".zip";
        try
        {
            using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
                if (!relative.StartsWith("Content/", StringComparison.Ordinal) && relative != "launcher-package.json") continue;
                var entry = zip.CreateEntry(relative, CompressionLevel.NoCompression);
                await using var source = File.OpenRead(file);
                await using var target = entry.Open();
                await source.CopyToAsync(target, ct);
            }
            return archive;
        }
        catch { File.Delete(archive); throw; }
    }
}
