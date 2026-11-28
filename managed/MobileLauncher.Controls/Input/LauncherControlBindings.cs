using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Input;
using Newtonsoft.Json.Linq;
using Terraria.GameInput;

namespace MobileJoystick;

internal static class LauncherControlBindings
{
	private static readonly Dictionary<string, string> aliases = new(StringComparer.Ordinal) {
		["RETURN"] = "Enter", ["ESCAPE"] = "Escape", ["BACKSPACE"] = "Back", ["SPACE"] = "Space",
		["MINUS"] = "OemMinus", ["EQUALS"] = "OemPlus", ["LEFTBRACKET"] = "OemOpenBrackets", ["RIGHTBRACKET"] = "OemCloseBrackets",
		["BACKSLASH"] = "OemPipe", ["NONUSBACKSLASH"] = "OemBackslash", ["SEMICOLON"] = "OemSemicolon", ["APOSTROPHE"] = "OemQuotes",
		["GRAVE"] = "OemTilde", ["COMMA"] = "OemComma", ["PERIOD"] = "OemPeriod", ["SLASH"] = "OemQuestion",
		["LCTRL"] = "LeftControl", ["RCTRL"] = "RightControl", ["LSHIFT"] = "LeftShift", ["RSHIFT"] = "RightShift",
		["LALT"] = "LeftAlt", ["RALT"] = "RightAlt", ["LGUI"] = "LeftWindows", ["RGUI"] = "RightWindows",
		["KP_ENTER"] = "Enter", ["KP_DIVIDE"] = "Divide", ["KP_MULTIPLY"] = "Multiply", ["KP_MINUS"] = "Subtract", ["KP_PLUS"] = "Add",
		["KP_PERIOD"] = "Decimal", ["NUMLOCKCLEAR"] = "NumLock", ["SCROLLLOCK"] = "Scroll", ["APPLICATION"] = "Apps"
	};
	internal static string InputName(string key)
	{
		if (key == null) return null;
		if (key == "MOUSE_LEFT") return "Mouse1";
		if (key == "MOUSE_RIGHT") return "Mouse2";
		if (key == "MOUSE_MIDDLE") return "Mouse3";
		if (!key.StartsWith("KEYBOARD_", StringComparison.Ordinal)) return null;
		string name = key[9..];
		if (aliases.TryGetValue(name, out string alias)) return alias;
		if (name.Length == 1 && char.IsDigit(name[0])) return "D" + name;
		if (name.StartsWith("KP_") && name.Length == 4 && char.IsDigit(name[3])) return "NumPad" + name[3];
		return Enum.TryParse(name, true, out Keys value) && Enum.IsDefined(value) && value != Keys.None ? value.ToString() : null;
	}
	internal static bool Supported(string key) => key == "UNKNOWN" || InputName(key) != null
		|| key?.StartsWith(MobileKeyboardBindings.LauncherPrefix, StringComparison.Ordinal) == true && MobileKeyboardBindings.Validate(key[MobileKeyboardBindings.LauncherPrefix.Length..]).Length > 0
		|| key?.StartsWith("TRIGGER_", StringComparison.Ordinal) == true && key.Length > 8
		|| key?.StartsWith("ACTION_", StringComparison.Ordinal) == true && Enum.TryParse(key[7..], out MobileControlAction action) && Enum.IsDefined(action) && action is not (MobileControlAction.LauncherControl or MobileControlAction.RadialMenu or MobileControlAction.QuickPotions or MobileControlAction.Zoom or MobileControlAction.KeyboardKey);
	// Recognize obsolete serialized bindings only so old presets can be cleaned.
	internal static bool RemovedBinding(string key) => key?.StartsWith("XBOX_", StringComparison.Ordinal) == true;
	internal static bool RemovedPointerBinding(string key)
		=> key is "SPECIAL_TOUCHPAD_RIGHT_BUTTON" or "MOUSE_WHEEL_UP" or "MOUSE_WHEEL_DOWN";
	internal static IEnumerable<string> Resolve(string key)
	{
		string input = InputName(key);
		if (input == null || PlayerInput.CurrentProfile == null) return Array.Empty<string>();
		return Resolve(input, PlayerInput.CurrentProfile.InputModes[InputMode.Keyboard].KeyStatus);
	}
	internal static string[] Resolve(string input, IDictionary<string, List<string>> bindings)
		=> bindings.Where(pair => pair.Value.Contains(input, StringComparer.OrdinalIgnoreCase)).Select(pair => pair.Key).ToArray();
	internal static MobileControlAction? Action(string trigger) => trigger switch {
		"Jump" => MobileControlAction.Jump, "MouseLeft" => MobileControlAction.UseItem, "MouseRight" => MobileControlAction.Interact,
		"Grapple" => MobileControlAction.Grapple, "QuickHeal" => MobileControlAction.Heal, "QuickMana" => MobileControlAction.Mana,
		"QuickBuff" => MobileControlAction.Buff, "QuickMount" => MobileControlAction.QuickMount, "SmartCursor" => MobileControlAction.SmartCursor,
		"SmartSelect" => MobileControlAction.AutoSelect, "Inventory" => MobileControlAction.Inventory, "MapFull" => MobileControlAction.Map,
		"MapStyle" => MobileControlAction.MapStyle, "MapAlphaCycle" => MobileControlAction.MapAlphaCycle, "Chat" => MobileControlAction.Chat, "Loadout1" => MobileControlAction.Loadout1,
		"LockOn" => MobileControlAction.TargetLockToggle,
		"Loadout2" => MobileControlAction.Loadout2, "Loadout3" => MobileControlAction.Loadout3, "ToggleCreativeMenu" => null,
		_ => null
	};
	internal static IEnumerable<string> ControlKeys(JObject data)
		=> data.Descendants().OfType<JProperty>().Where(p => p.Name.EndsWith("Keycode", StringComparison.Ordinal) || p.Name == "keycode")
			.Select(p => p.Value.Value<string>()).Concat((data["joystickKeys"] as JArray ?? new JArray()).Values<string>());
	internal static int Sector(Microsoft.Xna.Framework.Vector2 offset, int count)
	{
		float angle = MathF.Atan2(offset.X, -offset.Y);
		if (angle < 0) angle += MathF.Tau;
		return (int)MathF.Floor(angle / MathF.Tau * count + .5f) % count;
	}
}
