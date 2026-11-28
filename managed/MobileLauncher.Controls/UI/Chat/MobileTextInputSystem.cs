using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using ReLogic.Localization.IME;
using ReLogic.OS;
using Terraria;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.UI.Chat;

namespace MobileJoystick;

// WritingText routes characters, but FNA must also start the platform input method.
public sealed class MobileTextInputSystem : MobileSystem
{
    private static object owner;
    private static bool started;
    private static bool manualKeyboard;

    [DllImport("main", EntryPoint = "tml_pocket_bridge_take_keyboard_request")]
    private static extern int TakeKeyboardRequest();
    private static bool readByField;
    private static ulong inputFrame, lastReadFrame;
    internal static bool AutoImeEnabled {
        get {
            string value = System.Environment.GetEnvironmentVariable("MOBILEJOYSTICK_AUTO_IME")?.Trim();
            return value != "0" && !string.Equals(value, "false", System.StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "off", System.StringComparison.OrdinalIgnoreCase);
        }
    }

    public override void Load()
    {
        if (!Main.dedServ) {
            On_Main.GetInputText += ReadText;
        }
    }

    private static string ReadText(On_Main.orig_GetInputText orig, string oldString, bool allowMultiLine)
    {
        // Foreign text boxes call this same native entry point. Starting SDL
        // here covers Recipe Browser/Magic Storage without stealing their text.
        if (MobileJoystickConfig.Instance?.EnableMobileControls == true && Main.hasFocus && AutoImeEnabled
            && (owner != null || !Main.ingameOptionsWindow)) {
            lastReadFrame = inputFrame;
            readByField = true;
            if (!started || !TextInputEXT.IsTextInputActive()) TextInputEXT.StartTextInput();
            started = true;
        }
        KeyboardState keyboard = Keyboard.GetState();
        bool control = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        bool alt = keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt);
        bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        bool shortcut = control && !alt
            || shift && (keyboard.IsKeyDown(Keys.Insert) || keyboard.IsKeyDown(Keys.Delete));
        if (MobileJoystickConfig.Instance?.EnableMobileControls != true || !Main.hasFocus
            || keyboard.IsKeyDown(Keys.Back) || shortcut)
            return orig(oldString, allowMultiLine);

        bool backspace = false;
        for (int i = 0; i < Main.keyCount; i++)
            backspace |= Main.keyInt[i] == '\b';
        if (!backspace) return orig(oldString, allowMultiLine);

        // SDL soft keys can be pressed and released within one event poll. FNA
        // still queues their text events, but vanilla only deletes held keys.
        // Replay the text in order so typing + deleting in one frame stays correct.
        StringBuilder text = new(oldString ?? "");
        int retained = 0;
        for (int i = 0; i < Main.keyCount; i++) {
            int key = Main.keyInt[i];
            if (key == '\b') {
                if (text.Length == 0) continue;
                string current = text.ToString();
                var snippets = ChatManager.ParseMessage(current, Color.White);
                int start = snippets.Count > 0 && snippets[^1].DeleteWhole
                    ? current.Length - snippets[^1].TextOriginal.Length
                    : StringInfo.ParseCombiningCharacters(current)[^1];
                text.Length = start;
            }
            else if (key >= 32 && key != 127) text.Append(Main.keyString[i]);
            else {
                // Keep Enter/Escape and other controls for the vanilla handler.
                Main.keyInt[retained] = key;
                Main.keyString[retained++] = Main.keyString[i];
            }
        }
        Main.keyCount = retained;
        return orig(text.ToString(), allowMultiLine);
    }

    internal static void Focus(object field, Rectangle bounds = default)
    {
        owner = field;
        PlayerInput.WritingText = true;
        if (!AutoImeEnabled) {
            Main.instance.HandleIME();
            Stop();
            return;
        }
        RequestKeyboard(bounds);
    }

    internal static void FocusNativeField(Rectangle bounds)
    {
        if (MobileJoystickConfig.Instance?.EnableMobileControls != true || !Main.hasFocus
            || Main.ingameOptionsWindow || !AutoImeEnabled) return;
        readByField = true;
        lastReadFrame = inputFrame;
        PlayerInput.WritingText = true;
        RequestKeyboard(bounds);
    }

    private static void RequestKeyboard(Rectangle bounds)
    {
        if (bounds.Width > 0) TextInputEXT.SetInputRectangle(bounds);
        var ime = Platform.Get<IImeService>();
        bool wasEnabled = ime.IsEnabled;
        Main.instance.HandleIME();
        // Re-arm ReLogic after a dismissal, even when Terraria's writing flag did not change.
        if (wasEnabled || !ime.IsEnabled) ime.Enable();
        if (!TextInputEXT.IsTextInputActive()) TextInputEXT.StartTextInput();
        started = true;
    }

    internal static void ShowKeyboard()
    {
        // Open chat when playing without a focused text field so the IME has a recipient.
        if (owner == null && !HasNativeTextField && !(readByField && inputFrame - lastReadFrame <= 3)
            && !Main.gameMenu && !Main.ingameOptionsWindow && !Main.inFancyUI && !Main.mapFullscreen
            && !Main.playerInventory && !Main.LocalPlayer.dead)
            Main.OpenPlayerChat();

        manualKeyboard = true;
        PlayerInput.WritingText = true;
        RequestKeyboard(default);
    }

    private static bool HasNativeTextField => Main.drawingPlayerChat || Main.editSign || Main.editChest
        || Main.gameMenu && Main.MenuUI?.CurrentState is UIVirtualKeyboard
        || Main.inFancyUI && Main.InGameUI?.CurrentState is UIVirtualKeyboard;

    internal static void Blur(object field)
    {
        if (owner != field) return;
        owner = null;
        Stop();
    }

    private static void Stop()
    {
        if (started) TextInputEXT.StopTextInput();
        started = false;
        manualKeyboard = false;
    }

    public override void PostUpdateInput()
    {
        if (Main.dedServ) return;
        inputFrame++;
        if (TakeKeyboardRequest() != 0 && Main.hasFocus) ShowKeyboard();
        manualKeyboard = manualKeyboard && Platform.Get<IImeService>().IsEnabled;
        bool autoKeyboard = MobileJoystickConfig.Instance?.EnableMobileControls == true && AutoImeEnabled;
        if (!autoKeyboard && !manualKeyboard) { owner = null; readByField = false; Stop(); return; }
        if (Main.hasFocus && (manualKeyboard || owner != null || HasNativeTextField || readByField && inputFrame - lastReadFrame <= 3)) {
            if (!started || !TextInputEXT.IsTextInputActive()) TextInputEXT.StartTextInput();
            started = true;
            PlayerInput.WritingText = true;
        }
        else Stop();
    }

    public override void OnWorldUnload() { owner = null; readByField = false; lastReadFrame = 0; Stop(); }
    public override void Unload()
    {
        if (!Main.dedServ) {
            On_Main.GetInputText -= ReadText;
            owner = null;
            Stop();
        }
    }
}
