using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input.Touch;
using Newtonsoft.Json.Linq;
using Terraria;
using Terraria.GameInput;

namespace MobileJoystick;

internal static partial class MobileJoystickInput
{
	internal sealed class LauncherGesture
	{
		internal string ControlId;
		internal Vector2 Offset;
		internal int Sector = -1;
		internal double Started;
	}
	private static double LauncherTime => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
	private static bool launcherCursorActive;
	private static readonly Dictionary<int, LauncherGesture> launcherGestures = new();
	private static readonly HashSet<string> launcherToggles = new(StringComparer.Ordinal);
	private static readonly HashSet<string> launcherHeld = new(StringComparer.Ordinal);
	private static readonly HashSet<string> launcherTriggers = new(StringComparer.Ordinal);
	private static readonly HashSet<string> previousLauncherTriggers = new(StringComparer.Ordinal);
	internal static bool LauncherHeld(string id) => id != null && launcherHeld.Contains(id);
	internal static bool LauncherToggled(string id) => id != null && launcherToggles.Contains(id);
	internal static LauncherGesture LauncherState(string id) => launcherGestures.Values.FirstOrDefault(state => state.ControlId == id);
	private static void ResetLauncherControls()
	{
		launcherGestures.Clear();
		launcherToggles.Clear();
		launcherHeld.Clear();
		launcherTriggers.Clear();
		previousLauncherTriggers.Clear();
		launcherCursorActive = false;
	}

	private static void UpdateLauncherControls()
	{
		launcherHeld.Clear();
		previousLauncherTriggers.Clear(); previousLauncherTriggers.UnionWith(launcherTriggers); launcherTriggers.Clear();
		launcherCursorActive = false;
		var buttons = Layout.Buttons.Where(button => button.Launcher != null && IsButtonAvailable(button)).ToDictionary(button => button.Id);
		foreach (int id in launcherGestures.Keys.ToArray())
			if (!buttons.ContainsKey(launcherGestures[id].ControlId) || !Contacts.Any(contact => contact.Id == id)) launcherGestures.Remove(id);
		launcherToggles.RemoveWhere(id => !buttons.ContainsKey(id));
		var consumed = new HashSet<int>();
		foreach (MobileTouchContact contact in Contacts) {
			bool began = false;
			if (!launcherGestures.TryGetValue(contact.Id, out LauncherGesture state)) {
				if (contact.State != TouchLocationState.Pressed || MobileInventorySystem.BlocksControl(contact.Position) || MobileInventorySystem.OwnsContact(contact.Id)) continue;
				// Last painted control has priority.
				MobileControlButton? hit = null;
				foreach (MobileControlButton candidate in buttons.Values.Reverse()) {
					if (!candidate.Contains(contact.Position) || !CanActivateCustomButton(candidate, contact)) continue;
					if (launcherGestures.Values.Any(g => g.ControlId == candidate.Id)) continue;
					hit = candidate; break;
				}
				if (!hit.HasValue) continue;
				state = new LauncherGesture { ControlId = hit.Value.Id, Started = LauncherTime };
				launcherGestures[contact.Id] = state;
				began = true;
			}
			MobileControlButton button = buttons[state.ControlId];
			MobileLauncherControl data = button.Launcher;
			consumed.Add(contact.Id); HasAnyControlTouch = true;
			Vector2 local = data.Local(contact.Position, button.Center);
			Vector2 offset = local / (button.Dimensions * .5f);
			if (offset.LengthSquared() > 1) offset.Normalize();
			state.Offset = offset;
			bool valid = contact.State != TouchLocationState.Invalid;
			bool down = !contact.IsReleased;
			if (down) launcherHeld.Add(button.Id);
			switch (data.Type) {
				case "button":
					if (data.Flag("isToggle")) {
						if (began && !launcherToggles.Add(button.Id)) launcherToggles.Remove(button.Id);
					}
					else if (down && button.Contains(contact.Position)) HoldLauncherKey(data.String("keycode"));
					break;
				case "joystick":
					if (!down) break;
					if (data.String("mode", "KEYBOARD") == "MOUSE") {
						if (!hasTouchAimPosition) InitializeTouchAim(contact.Position);
						// A virtual mouse stick controls cursor velocity, independent of attack.
						MoveLauncherCursor(offset * 10f * MobileJoystickConfig.Instance.CursorSensitivity);
						if (Enum.TryParse(data.String("holdAction"), out MobileControlAction holdAction) && holdAction != MobileControlAction.Unbound) CurrentActions.Add(holdAction);
					}
					else HoldLauncherDirections(offset, ((JArray)data.Data["joystickKeys"]).Values<string>().ToArray(), MobileJoystickConfig.Instance.JoystickDeadzone);
					break;
				case "radialmenu":
					int count = (int)data.Number("sectorCount", 8);
					float dead = button.Radius * data.Number("expandedScale", 2.5f) * data.Number("deadZoneRatio", .3f);
					state.Sector = local.LengthSquared() < dead * dead ? -1 : LauncherControlBindings.Sector(local, count);
					if (valid && contact.State == TouchLocationState.Released && state.Sector >= 0 && data.Data["sectors"] is JArray sectors && state.Sector < sectors.Count)
						HoldLauncherKey(sectors[state.Sector].Value<string>("keycode"));
					break;
			}
			if (contact.IsReleased) launcherGestures.Remove(contact.Id);
		}
		foreach (string id in launcherToggles) { HoldLauncherKey(buttons[id].Launcher.String("keycode")); launcherHeld.Add(id); }
		// Captured contacts cannot also activate the hotbar, native sticks, or world use.
		Contacts.RemoveAll(contact => consumed.Contains(contact.Id));
	}
	private static void MoveLauncherCursor(Vector2 delta)
	{
		launcherCursorActive = true;
		if (!hasTouchAimPosition) InitializeTouchAim(Vector2.Zero);
		touchAimPosition = TouchAimPosition + delta;
		previousAimOrigin = MobileWorldInteraction.ToScreen(Main.LocalPlayer.MountedCenter);
		touchAimPosition = Vector2.Clamp(touchAimPosition, Vector2.Zero, new Vector2(PlayerInput.RealScreenWidth - 1, PlayerInput.RealScreenHeight - 1));
	}
	private static void HoldLauncherDirections(Vector2 offset, string[] keys, float deadzone)
	{
		bool[] directions = { offset.Y < -deadzone, offset.X > deadzone, offset.Y > deadzone, offset.X < -deadzone };
		for (int i = 0; i < 4; i++) if (directions[i]) HoldLauncherKey(keys[i]);
	}
	private static void HoldLauncherKey(string key)
	{
		if (key?.StartsWith(MobileKeyboardBindings.LauncherPrefix, StringComparison.Ordinal) == true) {
			MobileKeyboardBindings.Hold(key[MobileKeyboardBindings.LauncherPrefix.Length..]);
			return;
		}
		if (key?.StartsWith("ACTION_", StringComparison.Ordinal) == true && Enum.TryParse(key[7..], out MobileControlAction action)) { if (action != MobileControlAction.Unbound) CurrentActions.Add(action); return; }
		if (key?.StartsWith("TRIGGER_", StringComparison.Ordinal) == true) { HoldLauncherTrigger(key[8..]); return; }
		if (key == "KEYBOARD_ESCAPE") { CurrentActions.Add(MobileControlAction.Settings); return; }
		foreach (string trigger in LauncherControlBindings.Resolve(key)) HoldLauncherTrigger(trigger);
	}
	private static void HoldLauncherTrigger(string trigger)
	{
		bool pressed = launcherTriggers.Add(trigger) && !previousLauncherTriggers.Contains(trigger);
		if (trigger is "HotbarMinus" or "HotbarPlus") { if (pressed) LauncherScroll(trigger == "HotbarMinus" ? 1 : -1); return; }
		if (trigger.StartsWith("Hotbar", StringComparison.Ordinal) && int.TryParse(trigger[6..], out int slot) && slot >= 1 && slot <= 10) {
			if (pressed && !Main.playerInventory) Main.LocalPlayer.selectedItem = slot - 1;
			return;
		}
		MobileControlAction? action = LauncherControlBindings.Action(trigger);
		if (action.HasValue) CurrentActions.Add(action.Value);
		else MobileModKeybinds.Hold(trigger);
		// SetControls can read movement directly even if native UI input was suppressed.
		JoystickVector += trigger switch { "Left" => -Vector2.UnitX, "Right" => Vector2.UnitX, "Up" => -Vector2.UnitY, "Down" => Vector2.UnitY, _ => Vector2.Zero };
	}
	private static void LauncherScroll(int steps)
	{
		if (Main.playerInventory || Main.mapFullscreen || MobileInventorySystem.BlocksWorldUse) return;
		Main.LocalPlayer.selectedItem = ((Main.LocalPlayer.selectedItem - steps) % 10 + 10) % 10;
	}
}
