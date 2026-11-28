using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using ReLogic.Content;
using Terraria;

namespace MobileJoystick;

internal static class UiTextureAssets
{
    private const string Prefix = "MobileJoystick/Assets/";
    private static readonly Dictionary<string, Asset<Texture2D>> Textures = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Aliases =
        JsonConvert.DeserializeObject<Dictionary<string, string>>(ReadText("Assets/asset-aliases.json"));
    internal static string Root => Environment.GetEnvironmentVariable("TML_MOBILE_CONTROL_ART");
    private static readonly string DrawnRoot = Environment.GetEnvironmentVariable("TML_DRAWN_CONTROL_ART");
    private static readonly Dictionary<string, string> Paths = new(StringComparer.Ordinal);
    internal static bool Available => Directory.Exists(DrawnRoot) || Directory.Exists(Root);

    internal static Stream Open(string name)
    {
        var assembly = typeof(UiTextureAssets).Assembly;
        return assembly.GetManifestResourceStream(name) ?? assembly.GetManifestResourceStream(name.Replace('/', '\\'));
    }

    internal static string ReadText(string name)
    {
        using var reader = new StreamReader(Open(name) ?? throw new FileNotFoundException(name));
        return reader.ReadToEnd();
    }

    private static string Resolve(string path)
    {
        var name = path.StartsWith(Prefix, StringComparison.Ordinal) ? path[Prefix.Length..] : path;
        if (Paths.TryGetValue(name, out var cached)) return cached;
        var original = string.IsNullOrEmpty(Root) ? null : Path.Combine(Root, Aliases.GetValueOrDefault(name, name) + ".rawimg");
        return Paths[name] = File.Exists(original) ? original : Path.Combine(DrawnRoot ?? string.Empty, name + ".rawimg");
    }

    internal static bool HasAsset(string path)
    {
        return Available && File.Exists(Resolve(path));
    }

    internal static Asset<Texture2D> Request(string path, AssetRequestMode mode = AssetRequestMode.ImmediateLoad)
    {
        var name = Resolve(path);
        if (Textures.TryGetValue(name, out var asset)) return asset;
        using var stream = File.OpenRead(name);
        return Textures[name] = Main.Assets.CreateUntracked<Texture2D>(stream, ".rawimg", mode);
    }

    internal static void Unload()
    {
        foreach (var asset in Textures.Values) asset.Dispose();
        Textures.Clear();
        Paths.Clear();
    }
}
