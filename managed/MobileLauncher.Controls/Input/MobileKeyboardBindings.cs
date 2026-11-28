using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.UI;

namespace MobileJoystick;

internal static class MobileKeyboardBindings
{
	internal const string LauncherPrefix = "NATIVE_KEYBOARD_";
	private static readonly HashSet<Keys> held = new();
	private static readonly Dictionary<string, Keys> byName = Enum.GetValues<Keys>().Distinct()
		.Where(key => key != Keys.None).ToDictionary(key => key.ToString(), StringComparer.Ordinal);
	internal static readonly string[] Names = byName.Values.OrderBy(KeyGroup).ThenBy(key => key).Select(key => key.ToString()).ToArray();
	private static KeyboardState physical, previousPhysical;
	private static bool merged, previouslyMerged;
	private static bool backAsEscape;

	[DllImport("main", EntryPoint = "tml_pocket_bridge_take_back")]
	private static extern int TakeAndroidBack();

	internal static IDisposable InstallBackInput()
		=> new Hook(typeof(Keyboard).GetMethod(nameof(Keyboard.GetState), Type.EmptyTypes),
			(Func<Func<KeyboardState>, KeyboardState>)ReadKeyboard);

	private static KeyboardState ReadKeyboard(Func<KeyboardState> original)
	{
		KeyboardState keyboard = original();
		return backAsEscape && keyboard.IsKeyUp(Keys.Escape)
			? Merge(keyboard, new[] { Keys.Escape }) : keyboard;
	}

	private static bool RouteBack()
	{
		if (Main.gameMenu) {
			if (MobileTitleSettingsSystem.HandleBack()) return true;
			// Escape can leave the title screen. Android Back stays in the game.
			return Main.menuMode == 0;
		}
		if (PlayerInput.WritingText || MobileChatSystem.IsOpen) return false;
		if (Main.ingameOptionsWindow) {
			IngameOptions.Close();
		}
		else if (Main.inFancyUI) {
			IngameFancyUI.Close();
		}
		else if (Main.mapFullscreen) {
			Main.mapFullscreen = false;
		}
		else if (Main.playerInventory || Main.LocalPlayer.chest != -1 || Main.LocalPlayer.talkNPC != -1) {
			return false;
		}
		else IngameOptions.Open();
		return true;
	}

	private static int KeyGroup(Keys key) => key switch {
		>= Keys.A and <= Keys.Z => 0,
		>= Keys.D0 and <= Keys.D9 => 1,
		Keys.Space or Keys.Enter or Keys.Tab or Keys.Escape or Keys.Back or Keys.Delete or Keys.Up or Keys.Down or Keys.Left or Keys.Right => 2,
		>= Keys.F1 and <= Keys.F24 => 3,
		_ => 4
	};

	internal static string Validate(string name) => name != null && byName.ContainsKey(name) ? name : string.Empty;
	internal static void BeginFrame() => held.Clear();
	internal static void Hold(string name) { if (name != null && byName.TryGetValue(name, out Keys key)) held.Add(key); }
	internal static bool IsHeld(string name) => name != null && byName.TryGetValue(name, out Keys key) && held.Contains(key);
	internal static KeyboardState Merge(KeyboardState keyboard, IEnumerable<Keys> keys)
		=> new(keyboard.GetPressedKeys().Union(keys).ToArray());

	internal static void HandleInput(On_Main.orig_DoUpdate_HandleInput orig, Main self)
	{
		if ((merged || previouslyMerged) && !MobileJoystickInput.ShouldUseControls(MobileJoystickConfig.Instance)) Reset();
		backAsEscape = TakeAndroidBack() != 0 && Main.hasFocus && !RouteBack();
		orig(self);
		previousPhysical = merged ? physical : Main.oldKeyState;
		previouslyMerged = merged;
		physical = Main.keyState;
		merged = Main.hasFocus && held.Any(key => physical.IsKeyUp(key));
		if (merged) Main.keyState = Merge(physical, held);
	}

	internal static void Reset()
	{
		if (merged) Main.keyState = physical;
		if (previouslyMerged) Main.oldKeyState = previousPhysical;
		merged = previouslyMerged = false;
		backAsEscape = false;
		held.Clear();
	}

	internal static string Label(string name)
	{
		if (name?.Length == 2 && name[0] == 'D' && char.IsAsciiDigit(name[1])) return name[1..];
		if (name?.StartsWith("NumPad", StringComparison.Ordinal) == true) return "Num " + name[6..];
		return name switch {
			"Escape" => "Esc", "Back" => "Backspace", "CapsLock" => "Caps Lock",
			"LeftControl" => "L Ctrl", "RightControl" => "R Ctrl", "LeftShift" => "L Shift", "RightShift" => "R Shift",
			"LeftAlt" => "L Alt", "RightAlt" => "R Alt", "LeftWindows" => "L Win", "RightWindows" => "R Win",
			"OemMinus" => "-", "OemPlus" => "=", "OemOpenBrackets" => "[", "OemCloseBrackets" => "]",
			"OemBackslash" or "OemPipe" => "\\", "OemSemicolon" => ";", "OemQuotes" => "'", "OemTilde" => "`",
			"OemComma" => ",", "OemPeriod" => ".", "OemQuestion" => "/",
			"Divide" => "Num /", "Multiply" => "Num *", "Subtract" => "Num -", "Add" => "Num +", "Decimal" => "Num .",
			_ => name ?? string.Empty
		};
	}

	internal static void DrawLabel(SpriteBatch batch, string name, Vector2 center, float width, float scale, Color color)
	{
		string label = Label(name);
		scale = Math.Min(scale, width / Math.Max(1f, FontAssets.MouseText.Value.MeasureString(label).X));
		Utils.DrawBorderString(batch, label, center, color, scale, .5f, .5f);
	}
}
