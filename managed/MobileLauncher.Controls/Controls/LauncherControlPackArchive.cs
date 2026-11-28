using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace MobileJoystick;

// Archives are read in place. No extraction or writable cache is needed.
internal sealed class LauncherControlPackArchive : IDisposable
{
	internal const int MaxArchiveBytes = 64 * 1024 * 1024;
	private readonly ZipArchive archive;
	private readonly Dictionary<string, ZipArchiveEntry> files = new(StringComparer.OrdinalIgnoreCase);
	private readonly string prefix;

	internal LauncherControlPackArchive(Stream stream)
	{
		if (stream.Length > MaxArchiveBytes) { stream.Dispose(); throw new InvalidDataException("控制布局包 ZIP 过大"); }
		try {
			archive = new ZipArchive(stream, ZipArchiveMode.Read);
			if (archive.Entries.Count > 512) throw new InvalidDataException("控制布局包文件过多");
			long total = 0;
			foreach (ZipArchiveEntry entry in archive.Entries) {
				string name = entry.FullName.Replace('\\', '/');
				if (name.StartsWith('/') || name.Contains(':') || name.Contains('\0')
					|| name.Split('/').Any(part => part is "." or "..")) throw new InvalidDataException("控制布局包路径无效");
				if (name.EndsWith('/')) continue;
				if (entry.Length > 16 * 1024 * 1024 || (total += entry.Length) > MaxArchiveBytes)
					throw new InvalidDataException("控制布局包解压大小超限");
				if (!files.TryAdd(name, entry)) throw new InvalidDataException("控制布局包文件名重复");
			}
			string[] manifests = files.Keys.Where(name => name == "manifest.json" || name.EndsWith("/manifest.json", StringComparison.Ordinal)).ToArray();
			if (manifests.Length != 1) throw new InvalidDataException("控制布局包需包含唯一的 manifest.json");
			prefix = manifests[0][..^"manifest.json".Length];
		}
		catch { archive?.Dispose(); stream.Dispose(); throw; }
	}

	internal byte[] ReadFile(string relative, int limit = 16 * 1024 * 1024)
	{
		if (!files.TryGetValue(prefix + relative.Replace('\\', '/'), out ZipArchiveEntry entry) || entry.Length > limit)
			throw new InvalidDataException("控制布局包文件缺失或过大：" + relative);
		using Stream input = entry.Open();
		byte[] data = new byte[checked((int)entry.Length)];
		input.ReadExactly(data);
		if (input.ReadByte() != -1) throw new InvalidDataException("控制布局包文件长度无效");
		return data;
	}

	public void Dispose() => archive.Dispose();
}
