using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using MobileLauncher.Bootstrap.Compatibility;
using MobileLauncher.Bootstrap.Platform;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;

namespace MobileLauncher.Bootstrap.Runtime;

public sealed class TmlLoaderEntrypoint
{
    private static Hook? _loggingHooksBypass;
    private int _started;

    public int Launch(
        string assemblyPath,
        string[] arguments,
        string workingDirectory,
        IAndroidBridge androidBridge)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("tModLoader can only be launched once per CoreCLR process.");
        }

        if (!File.Exists(assemblyPath))
        {
            throw new BootstrapStageException(BootstrapStage.Validation, $"tModLoader assembly was not found: {assemblyPath}");
        }

        Directory.CreateDirectory(workingDirectory);
        Directory.SetCurrentDirectory(workingDirectory);

        try
        {
            var assemblyDirectory = Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!;
            var monoModPath = Path.Combine(AppContext.BaseDirectory, "MonoMod.Android");
            Environment.SetEnvironmentVariable("MONOMOD_PATH", monoModPath);
            using var dependencies = new GameAssemblyResolver(assemblyDirectory, monoModPath);
            Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
            ApplyAndroidCompatibilityPatch(assembly);
            using var platformServices = AndroidPlatformServiceProxy.TryConfigure(assembly, androidBridge);
            using var mobilePatches = IsAndroidRuntime() ? LoadBundledPatches(assembly, androidBridge) : null;
            using var mobileControls = IsAndroidRuntime() ? LoadBundledControls() : null;
            using var importedCompatibility = IsAndroidRuntime()
                ? new ImportedGameCompatibility(assembly, androidBridge, platformServices is null)
                : null;
            LogPlatformConfiguration(androidBridge, platformServices is not null);
            MethodInfo? entryPoint = assembly.EntryPoint;
            if (entryPoint is null)
            {
                throw new BootstrapStageException(BootstrapStage.Runtime, "tModLoader assembly has no entry point.");
            }

            object? result = entryPoint.GetParameters().Length == 0
                ? entryPoint.Invoke(null, null)
                : entryPoint.Invoke(null, new object?[] { arguments });
            return result is int exitCode ? exitCode : 0;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw new BootstrapStageException(BootstrapStage.Game, "tModLoader entry point failed.", exception.InnerException);
        }
    }

    private static IDisposable LoadBundledPatches(Assembly game, IAndroidBridge bridge)
    {
        // Only this fixed APK-owned path may provide executable patches.
        var path = Path.Combine(AppContext.BaseDirectory, "MobileLauncher.Patches.dll");
        var patches = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        var entry = patches.GetType("MobileLauncher.Patches.PatchSession", true)!
            .GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)!;
        return (IDisposable)entry.Invoke(null, new object[] { game, bridge })!;
    }

    private static IDisposable LoadBundledControls()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "MobileLauncher.Controls.dll");
        var controls = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        var initialize = controls.GetType("MobileJoystick.MobileControlsSession", true)!
            .GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)!;
        return (IDisposable)initialize.Invoke(null, null)!;
    }

    private static void LogPlatformConfiguration(IAndroidBridge bridge, bool servicesConfigured)
    {
        var platformType = Type.GetType("ReLogic.OS.Platform, ReLogic", throwOnError: false);
        var isAndroid = platformType?.GetProperty("IsAndroid", BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null) as bool? ?? false;
        Console.WriteLine(
            $"[MobileLauncher.Bootstrap] PlatformType={(isAndroid ? "Android" : "Other")}; " +
            $"files={bridge.FilesDirectory}; cache={bridge.CacheDirectory}; " +
            $"platformServicesConfigured={servicesConfigured}");
    }

    private static void ApplyAndroidCompatibilityPatch(Assembly assembly)
    {
        if (!IsAndroidRuntime())
        {
            return;
        }

        ConfigureMonoModArchitecture(RuntimeInformation.ProcessArchitecture);
        var loggingHooks = assembly.GetType("Terraria.ModLoader.Engine.LoggingHooks");
        var initialize = loggingHooks?.GetMethod(
            "Init",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (initialize is null)
        {
            throw new BootstrapStageException(BootstrapStage.Runtime, "tModLoader LoggingHooks.Init was not found.");
        }

        _loggingHooksBypass = new Hook(initialize, new Action<Action>(SkipUnsupportedAndroidLoggingHooks));
        Console.WriteLine("[MobileLauncher.Bootstrap] Applied Android LoggingHooks MonoMod hook.");
    }

    private static void SkipUnsupportedAndroidLoggingHooks(Action original)
    {
    }

    private static void ConfigureMonoModArchitecture(Architecture processArchitecture)
    {
        var expected = processArchitecture switch
        {
            Architecture.Arm64 => ArchitectureKind.Arm64,
            Architecture.X64 => ArchitectureKind.x86_64,
            _ => throw new PlatformNotSupportedException($"Unsupported Android process architecture: {processArchitecture}")
        };
        var detected = PlatformDetection.Architecture;
        if (detected == expected) return;

        // The bundled MonoMod caches the kernel's platform string before creating
        // its first PlatformTriple. The running CoreCLR knows its own instruction set.
        var field = typeof(PlatformDetection).GetField("arch", BindingFlags.NonPublic | BindingFlags.Static);
        if (field?.FieldType != typeof(ArchitectureKind))
            throw new PlatformNotSupportedException($"Cannot correct MonoMod architecture {detected} to {expected}.");
        field.SetValue(null, expected);
        Console.WriteLine($"[MobileLauncher.Bootstrap] Corrected MonoMod architecture {detected} to {expected}.");
    }

    private static bool IsAndroidRuntime() => OperatingSystem.IsAndroid() ||
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANDROID_ROOT"));
}
