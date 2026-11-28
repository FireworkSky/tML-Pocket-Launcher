using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace MobileLauncher.Stores;

internal static class TmlResources
{
    internal static Dictionary<string, byte[]> Read(string path)
    {
        if (Path.GetFileName(path) != "tModLoader.dll")
            throw new InvalidDataException("Invalid tModLoader assembly path.");

        using var file = File.OpenRead(path);
        using var pe = new PEReader(file);
        var metadata = pe.GetMetadataReader();
        var resourceDirectory = pe.PEHeaders.CorHeader?.ResourcesDirectory
            ?? throw new InvalidDataException("tModLoader has no managed resource directory.");
        var data = pe.GetSectionData(resourceDirectory.RelativeVirtualAddress);
        var names = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Terraria.IO.Data.ResourcePacksDefaultInfo.tsv"] = "layout",
            ["Terraria.ModLoader.Config.UI.ButtonCollapsed.png"] = "ButtonCollapsed",
            ["Terraria.ModLoader.UI.DropdownIcon.png"] = "DropdownIcon",
            ["Terraria.ModLoader.UI.ButtonError.png"] = "ButtonError",
        };
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var handle in metadata.ManifestResources)
        {
            var resource = metadata.GetManifestResource(handle);
            if (!resource.Implementation.IsNil || !names.TryGetValue(metadata.GetString(resource.Name), out var name))
                continue;

            var offset = checked((int)resource.Offset);
            var reader = data.GetReader(offset, resourceDirectory.Size - offset);
            var length = reader.ReadInt32();
            if (length <= 0 || length > (name == "layout" ? 1_200_000 : 64_000) || length > reader.RemainingBytes)
                throw new InvalidDataException($"Invalid tModLoader resource size: {name}");
            result.Add(name, reader.ReadBytes(length));
        }
        if (result.Count != names.Count)
            throw new InvalidDataException("The installed tModLoader is missing PE conversion resources.");
        return result;
    }
}
