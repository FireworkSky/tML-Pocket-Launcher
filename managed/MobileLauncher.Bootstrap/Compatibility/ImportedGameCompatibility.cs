using System.Reflection;
using MobileLauncher.Bootstrap.Platform;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace MobileLauncher.Bootstrap.Compatibility;

/// <summary>Android adapters for unmodified tML/FNA release assemblies.</summary>
public sealed class ImportedGameCompatibility : IDisposable
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private readonly List<IDisposable> _hooks = new();
    private readonly Assembly _game;
    private readonly IAndroidBridge _bridge;
    private object? _ime;
    private string _composition = string.Empty;

    public ImportedGameCompatibility(Assembly game, IAndroidBridge bridge, bool needsPlatformAdapters)
    {
        _game = game;
        _bridge = bridge;
        try
        {
            if (!needsPlatformAdapters) return;

            var content = Environment.GetEnvironmentVariable("TML_ANDROID_CONTENT");
            if (string.IsNullOrWhiteSpace(content) || !Directory.Exists(Path.Combine(content, "Images")))
            {
                throw new InvalidOperationException("The imported Terraria Content directory is incomplete.");
            }

            // Resource-only installs have no desktop Steam client. tML's normal Content
            // manager still loads its own overrides and the imported vanilla resources.
            Patch("Terraria.ModLoader.Engine.InstallVerifier", "Startup", (_, _) =>
            {
                var verifier = game.GetType("Terraria.ModLoader.Engine.InstallVerifier", true)!;
                var distribution = verifier.GetField("DistributionPlatform", Flags)!;
                distribution.SetValue(null, Enum.Parse(distribution.FieldType, "GoG"));
                verifier.GetField("vanillaExePath", Flags)!.SetValue(null,
                    Path.Combine(Directory.GetParent(content)!.FullName, "Terraria.exe"));
                Console.WriteLine($"[ImportedGame] Using validated Terraria resources: {content}");
                return null;
            }, required: true);

            Patch("Terraria.Main", "InitTMLContentManager", (args, _) =>
            {
                ImportedContentManager.Configure(game, args[0]!, content);
                return null;
            }, required: true);

            ImportedMobileUi.Apply(game, content, _hooks);

            if (File.Exists(Path.Combine(content, "..", "conversion-report.json")) &&
                !File.Exists(Path.Combine(content, "Images", "logo_1.xnb")) &&
                !File.Exists(Path.Combine("Content", "Images", "logo_1.xnb")))
            {
                var splashLoader = Find(game, "Terraria.Initializers.AssetInitializer", "LoadSplashAssets")
                    ?? throw new MissingMethodException("Terraria.Initializers.AssetInitializer", "LoadSplashAssets");
                _hooks.Add(new ILHook(splashLoader, il =>
                {
                    var cursor = new ILCursor(il);
                    if (!cursor.TryGotoNext(MoveType.After, op => op.MatchLdstr(@"Images\logo_")) ||
                        !cursor.TryGotoNext(MoveType.After, op => op.MatchCall<string>(nameof(string.Concat))))
                    {
                        throw new InvalidOperationException("The tModLoader splash asset request has changed.");
                    }

                    // The legacy splash field is initialized but the game never reads it.
                    cursor.EmitDelegate<Func<string, string>>(_ => @"Images\SplashScreens\Splash_1");
                }));
            }

            // SocialMode.None still calls SteamedWraps.Initialize in desktop tML.
            // Preserve tag initialization while preventing its native SteamGameServer fallback.
            Patch("Terraria.Social.Steam.SteamedWraps", "Initialize", (_, _) =>
            {
                ImportedWorkshopCompatibility.Initialize(game.GetType("Terraria.Social.Steam.SteamedWraps", true)!);
                return null;
            });
            Patch("Terraria.Social.Steam.SteamedWraps", "TryInitViaGameServer", (_, _) => false);
            Patch("Terraria.Social.SocialAPI", "Initialize", (args, next) =>
            {
                var method = Find(game, "Terraria.Social.SocialAPI", "Initialize")!;
                var enumType = Nullable.GetUnderlyingType(method.GetParameters()[0].ParameterType)!;
                args[0] = Enum.Parse(enumType, "None");
                return next();
            }, required: true);
            Patch("Terraria.ModLoader.Core.ModOrganizer", "DetectAbnormalSteamWorkshopDownloads", (args, _) =>
            {
                args[0] = null;
                args[1] = string.Empty;
                args[2] = string.Empty;
                return string.Empty;
            });
            Patch("Terraria.Utils", "OpenToURL", (args, _) => { bridge.OpenUrl((string)args[0]!); return null; });
            Patch("Terraria.Utils", "OpenFolder", (_, _) => { bridge.ShowToast("请使用启动器管理游戏文件"); return null; });

            // The bundled touch patch is the single pointer adapter. This hook
            // only drains lifecycle/IME messages and must call the patched method.
            Patch("Terraria.GameInput.PlayerInput", "MouseInput", (_, next) =>
            {
                PumpEvents();
                return next();
            }, required: true);

            _ = int.TryParse(Environment.GetEnvironmentVariable("TML_RENDER_RESOLUTION_PERCENT"), out int renderPercent);
            bool fixed1080p = Environment.GetEnvironmentVariable("TML_FIXED_2400_1080") == "1";
            var mainType = game.GetType("Terraria.Main", throwOnError: true)!;
            var minWidth = mainType.GetField("minScreenW", Flags);
            var minHeight = mainType.GetField("minScreenH", Flags);
            Patch("Terraria.Main", "SetDisplayMode", (args, next) =>
            {
                if (bridge.TryGetDisplaySize(out var width, out var height))
                {
                    (width, height) = AndroidDisplayResolution.NormalizeLandscape(width, height);
                    if (fixed1080p)
                    {
                        (width, height) = (2400, 1080);
                    }
                    else if (renderPercent is 60 or 75)
                    {
                        (width, height) = AndroidDisplayResolution.Scale(width, height, renderPercent,
                            (int?)minWidth?.GetValue(null) ?? 800, (int?)minHeight?.GetValue(null) ?? 720);
                    }
                    args[0] = width;
                    args[1] = height;
                    args[2] = true;
                }
                bridge.SetFullscreen(true);
                return next();
            }, required: true);

            var relogic = Assembly.Load(new AssemblyName("ReLogic"));
            PatchAssembly(relogic, "ReLogic.Localization.IME.PlatformIme", "Enable", (args, next) =>
            {
                _ime = args[0];
                bridge.SetImeVisible(true);
                return next();
            });
            PatchAssembly(relogic, "ReLogic.Localization.IME.PlatformIme", "Disable", (_, next) =>
            {
                bridge.SetImeVisible(false);
                _composition = string.Empty;
                return next();
            });
            PatchAssembly(relogic, "ReLogic.Localization.IME.FnaIme", "get_CompositionString", (_, _) => _composition);
            // FnaIme receives committed characters from SDL's existing TextInputEXT event.
            Patch("Terraria.ModLoader.UI.UIFocusInputTextField", "Focus", (_, next) =>
            {
                var result = next();
                RequestIme();
                return result;
            });
            Patch("Terraria.ModLoader.UI.UIFocusInputTextField", "LeftClick", (_, next) =>
            {
                var result = next();
                RequestIme();
                return result;
            });
            PatchAssembly(relogic, "ReLogic.OS.Linux.Clipboard", "GetClipboard", (_, _) => bridge.GetClipboardText() ?? string.Empty);
            PatchAssembly(relogic, "ReLogic.OS.Linux.Clipboard", "SetClipboard", (args, _) =>
            {
                bridge.SetClipboardText((string)args[1]!); return null;
            });
            Console.WriteLine($"[ImportedGame] Installed {_hooks.Count} Android runtime adapters for {game.GetName().Version}.");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void RequestIme()
    {
        if (_ime is not null)
            _ime.GetType().GetMethod("Enable", Flags)!.Invoke(_ime, null);
        else
            _bridge.SetImeVisible(true);
    }

    private void PumpEvents()
    {
        while (_bridge.Events.TryDequeue(out var input))
        {
            if (input.Kind == AndroidInputEventKind.ImeComposition)
            {
                _composition = input.Text ?? string.Empty;
            }

            if (input.Kind == AndroidInputEventKind.ImeCommit)
            {
                _composition = string.Empty;
            }

            if (input.Kind == AndroidInputEventKind.ImeVisibility && input.Action == 1 && !input.Focused && _ime is not null)
            {
                _ime.GetType().GetMethod("Disable", Flags)?.Invoke(_ime, null);
                _game.GetType("Terraria.GameInput.PlayerInput")?.GetField("WritingText", Flags)?.SetValue(null, false);
            }
        }
    }

    private void Patch(string type, string method, Func<object?[], Func<object?>, object?> intercept, bool required = false) =>
        PatchAssembly(_game, type, method, intercept, required);

    private void PatchAssembly(Assembly assembly, string type, string method,
        Func<object?[], Func<object?>, object?> intercept, bool required = false)
    {
        var target = Find(assembly, type, method);
        if (target is null)
        {
            if (required)
            {
                throw new NotSupportedException($"This tML version is missing Android adapter target {type}.{method}.");
            }

            Console.WriteLine($"[ImportedGame] Optional adapter unavailable: {type}.{method}");
            return;
        }
        _hooks.Add(new RuntimeMethodHook(target, intercept));
    }

    private static MethodInfo? Find(Assembly assembly, string type, string name) =>
        assembly.GetType(type)?.GetMethods(Flags).FirstOrDefault(method => method.Name == name && !method.ContainsGenericParameters);

    public void Dispose()
    {
        for (var index = _hooks.Count - 1; index >= 0; index--)
        {
            _hooks[index].Dispose();
        }

        _hooks.Clear();
    }
}
