using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace MobileLauncher.Bootstrap.Compatibility;

internal static class ImportedMobileUi
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private const string Minimap = "Terraria.GameContent.UI.Minimap.";
    private const string UiCommon = "Terraria.ModLoader.UI.UICommon";

    public static void Apply(Assembly game, string content, List<IDisposable> hooks)
    {
        if (!File.Exists(Path.Combine(content, "Images", "UI", "ModPackLocation.xnb")))
            return;

        MethodInfo Method(string type, string name) =>
            game.GetType(type, throwOnError: true)!.GetMethod(name, Members)
            ?? throw new MissingMethodException(type, name);

        hooks.Add(new ILHook(Method(Minimap + "MinimapFrameTemplate", "CreateInstance"), il =>
        {
            var cursor = new ILCursor(il);
            if (!cursor.TryGotoNext(MoveType.After, i => i.MatchCallOrCallvirt(Minimap + "MinimapFrame", "set_ConfigKey")))
                throw new InvalidOperationException("Cannot locate minimap button initialization.");
            cursor.Emit(OpCodes.Ret);
        }));
        hooks.Add(new RuntimeMethodHook(Method(Minimap + "MinimapFrame", "Update"), (_, _) => null));
        hooks.Add(new ILHook(Method(Minimap + "MinimapFrame", "DrawForeground"), il =>
        {
            var cursor = new ILCursor(il);
            if (!cursor.TryGotoNext(MoveType.Before, i => i.MatchLdarg(0),
                    i => i.MatchLdfld(Minimap + "MinimapFrame", "_zoomInButton")))
                throw new InvalidOperationException("Cannot locate minimap button drawing.");
            cursor.Emit(OpCodes.Ret);
        }));

        var main = game.GetType("Terraria.Main", throwOnError: true)!;
        var captureModeDisabled = main.GetField("CaptureModeDisabled", Members)!;
        hooks.Add(new RuntimeMethodHook(Method("Terraria.Main", "LoadContent"), (_, next) =>
        {
            captureModeDisabled.SetValue(null, true);
            return next();
        }));
        hooks.Add(new RuntimeMethodHook(Method("Terraria.Graphics.Capture.CaptureInterface", "QuickScreenshot"), (_, _) => null));
        var captureManager = game.GetType("Terraria.Graphics.Capture.CaptureManager", throwOnError: true)!;
        var capture = captureManager.GetMethod("Capture", Members, null,
            [game.GetType("Terraria.Graphics.Capture.CaptureSettings", throwOnError: true)!], null)!;
        hooks.Add(new RuntimeMethodHook(capture, (_, _) => null));
        hooks.Add(new ILHook(Method("Terraria.Main", "DrawPageIcons"), il =>
        {
            var cursor = new ILCursor(il);
            bool FindPosition(int link) => cursor.TryGotoNext(MoveType.After, i => i.MatchLdcI4(link)) &&
                cursor.TryGotoNext(MoveType.After, i => i.MatchCall("Terraria.UI.Gamepad.UILinkPointNavigator", "SetPosition"));
            if (!FindPosition(306))
                throw new InvalidOperationException("Cannot locate the inventory photo control.");
            var beforePhoto = cursor.Next;
            if (!FindPosition(307))
                throw new InvalidOperationException("Cannot locate the end of the inventory photo control.");
            var afterPhoto = cursor.MarkLabel();
            cursor.Goto(beforePhoto);
            cursor.Emit(OpCodes.Br, afterPhoto);
        }));

        hooks.Add(new RuntimeMethodHook(Method(UiCommon, "LoadTextures"), (_, next) =>
        {
            var result = next();
            var icon = game.GetType(UiCommon, throwOnError: true)!.GetProperty("ModLocationModPackIcon", Members)!;
            var assets = main.GetField("Assets", Members)!;
            var request = assets.FieldType.GetMethods().Single(method =>
                method.Name == "Request" && method.IsGenericMethodDefinition && method.GetParameters().Length == 2);
            var mode = Enum.Parse(request.GetParameters()[1].ParameterType, "ImmediateLoad");
            var texture = request.MakeGenericMethod(icon.PropertyType.GenericTypeArguments[0])
                .Invoke(assets.GetValue(null), ["Images/UI/ModPackLocation", mode]);
            icon.SetValue(null, texture);
            return result;
        }));
    }
}
