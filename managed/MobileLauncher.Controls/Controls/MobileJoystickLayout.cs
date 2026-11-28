using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;

namespace MobileJoystick;

internal readonly struct MobileHotbarLayout
{
	public const int SlotCount = 10;
	private readonly MobileInterfaceLayoutProfile profile;
	private readonly Rectangle viewport;
	private readonly Rectangle defaultBounds;
	private readonly Rectangle usableBounds;
	private readonly MobileHotbarOrientation orientation;

	public MobileHotbarLayout(float scale, int slotSize, int padding, int startX, int startY, int sideButtonSize,
		MobileInterfaceLayoutProfile profile = null, int width = 0, int height = 0, int safeInset = 0,
		Rectangle? usableBoundsOverride = null, MobileHotbarOrientation orientation = MobileHotbarOrientation.Vertical)
	{
		Scale = scale;
		SlotSize = slotSize;
		Padding = padding;
		StartX = startX;
		StartY = startY;
		SideButtonSize = sideButtonSize;
		this.orientation = orientation;
		viewport = new Rectangle(0, 0, width, height);
		usableBounds = usableBoundsOverride ?? new Rectangle(safeInset, 4, Math.Max(1, width - safeInset * 2), Math.Max(1, height - 8));
		Rectangle buttons = Rectangle.Union(InventoryButton, SettingsButton);
		Rectangle slots = Rectangle.Union(GetSlot(0), GetSlot(SlotCount - 1));
		defaultBounds = Rectangle.Union(buttons, slots);
		this.profile = profile;
	}

	public float Scale { get; }

	public int SlotSize { get; }

	public int Padding { get; }

	public int StartX { get; }

	public int StartY { get; }

	public int SideButtonSize { get; }
	public int Rows => orientation == MobileHotbarOrientation.Horizontal ? 1 : 5;

	public Rectangle InventoryButton => Apply(new(StartX - Padding - SideButtonSize, StartY, SideButtonSize, SideButtonSize));

	public Rectangle SettingsButton => Apply(new(StartX - Padding - SideButtonSize,
		StartY + (SlotSize + Padding) * (orientation == MobileHotbarOrientation.Horizontal ? 1 : 4), SideButtonSize, SideButtonSize));

	public Rectangle GetSlot(int slotIndex)
	{
		int column = orientation == MobileHotbarOrientation.Horizontal ? slotIndex : slotIndex / Rows;
		int row = orientation == MobileHotbarOrientation.Horizontal ? 0 : slotIndex % Rows;
		return Apply(new Rectangle(StartX + column * (SlotSize + Padding), StartY + row * (SlotSize + Padding), SlotSize, SlotSize));
	}

	public float EffectiveScale => Scale * (profile != null && profile.Has(MobileInterfaceRegion.Hotbar)
		? profile.Get(MobileInterfaceRegion.Hotbar).Scale : 1f);

	private Rectangle Apply(Rectangle rectangle)
	{
		if (profile == null || !profile.Has(MobileInterfaceRegion.Hotbar)) return rectangle;
		return MobileInterfaceLayoutProfile.Transform(profile.Get(MobileInterfaceRegion.Hotbar), rectangle, defaultBounds, viewport, usableBounds);
	}
}

internal static class MobileJoystickLayout
{
	public static float GetControlScale(MobileJoystickConfig config)
		=> GetControlScale(config, Main.screenWidth, Main.screenHeight);

	internal static float GetControlScale(MobileJoystickConfig config, int width, int height)
	{
		float screenScale = Math.Min(height / 1080f, width / 1920f);
		return (config?.ControlScale ?? 1.8f) * screenScale;
	}

	public static MobileHotbarLayout CreateHotbarLayout(MobileJoystickConfig config)
		=> CreateHotbarLayout(config, Main.screenWidth, Main.screenHeight);

	internal static MobileHotbarLayout CreateHotbarLayout(MobileJoystickConfig config, int width, int height)
		=> CreateHotbarLayout(config, width, height, true);

	internal static MobileHotbarLayout CreateHotbarLayout(MobileJoystickConfig config, int width, int height, bool applyInterfaceLayout)
		=> CreateHotbarLayout(config, width, height, applyInterfaceLayout, null);

	internal static MobileHotbarLayout CreateHotbarLayout(MobileJoystickConfig config, int width, int height,
		bool applyInterfaceLayout, MobileInterfaceLayoutProfile profileOverride)
		=> CreateHotbarLayout(config, width, height, applyInterfaceLayout, profileOverride, null);

	internal static MobileHotbarLayout CreateHotbarLayout(MobileJoystickConfig config, int width, int height,
		bool applyInterfaceLayout, MobileInterfaceLayoutProfile profileOverride, Rectangle? usableBoundsOverride)
	{
		float scale = GetControlScale(config, width, height);
		int slotSize = Math.Max(16, (int)(50f * scale));
		int padding = Math.Max(2, (int)(3f * scale));
		int sideButtonSize = Math.Max(16, (int)(38f * scale));
		int edge = Math.Max(4, (int)((config?.EdgePadding ?? 18) * scale * 0.5f));
		int startX = Math.Max(edge, (int)(width * .047f)) + sideButtonSize + padding;
		int startY = Math.Max(4, (int)(6f * scale));
		MobileCustomPreset preset = GetCustomPreset(config);
		if (preset != null) {
			foreach (MobileCustomControl control in preset.Controls) {
				if (control.Kind != MobileCustomControlKind.Button || control.Action != MobileControlAction.Inventory) continue;
				float radius = GetCustomControlRadius(control, scale);
				Vector2 center = ClampCustomControlCenter(new(control.X * width, control.Y * height), radius, width, height, config);
				Rectangle bounds = new MobileControlButton(control.Action, "", center, radius).Bounds;
				// Reserve space for a saved left-side backpack, including its minimum touch size.
				if (bounds.Left < startX && bounds.Right > edge && bounds.Top < startY + (slotSize + padding) * 5 && bounds.Bottom > startY)
					startX = Math.Max(startX, bounds.Right + padding);
			}
		}

		return new MobileHotbarLayout(scale, slotSize, padding, startX, startY, sideButtonSize,
			applyInterfaceLayout ? profileOverride ?? config?.InterfaceLayout : null, width, height,
			Math.Max(4, (int)((config?.EdgePadding ?? 18) * scale * .5f)), usableBoundsOverride,
			config?.HotbarOrientation ?? MobileHotbarOrientation.Vertical);
	}

	internal static MobileCustomPreset GetCustomPreset(MobileJoystickConfig config)
	{
		if (config?.ControlLayout == MobileControlLayoutMode.Custom
			&& config.CustomPresets != null && config.CustomPresets.Count > 0) {
			int selectedPreset = Math.Clamp(config.ActiveCustomPresetIndex, 0, config.CustomPresets.Count - 1);
			return config.CustomPresets[selectedPreset];
		}
		return null;
	}

	public static MobileControlLayout CreateControlsLayout(MobileJoystickConfig config, int width, int height, MobileCustomPreset customPreset = null)
	{
		float scale = GetControlScale(config, width, height);
		customPreset ??= GetCustomPreset(config);
		if (customPreset != null) return CreateCustomControlsLayout(customPreset, width, height, scale, config);

		float edge = (config?.EdgePadding ?? 18) * scale * .5f;
		MobileHotbarLayout hotbar = CreateHotbarLayout(config, width, height);
		// Match the phone layout while keeping the clusters clear of the screen edges.
		float leftInset = Math.Max(width * .19f, 166f * scale + edge);
		float rightInset = Math.Max(width * .145f, 166f * scale + edge);
		float centerY = height - 104f * scale - edge;
		MobileJoystickConfig activeConfig = config ?? MobileJoystickConfig.Instance;
		Vector2 move = new(leftInset + (activeConfig?.LeftStickOffsetX ?? 0f), centerY + (activeConfig?.LeftStickOffsetY ?? 0f));
		Vector2 attack = new(width - rightInset + (activeConfig?.RightStickOffsetX ?? 0f), centerY + (activeConfig?.RightStickOffsetY ?? 0f));
		float large = 32f * scale;
		float medium = 25f * scale;
		float small = 24f * scale;
		List<MobileControlButton> buttons = new() {
			new(MobileControlAction.Grapple, "GrappleHook", attack + new Vector2(-106f, -84f) * scale, large),
			new(MobileControlAction.Jump, "Jump", attack + new Vector2(122f, -84f) * scale, large),
			new(MobileControlAction.Mount, "Mount", attack + new Vector2(126f, 7f) * scale, small),
			new(MobileControlAction.TargetLockToggle, "TargetLockOn", attack + new Vector2(-104f, 80f) * scale, small),
			new(MobileControlAction.SmartCursor, "SmartCursor", attack + new Vector2(104f, 80f) * scale, small),
			new(MobileControlAction.Interact, "HeldItem", move + new Vector2(-106f, -84f) * scale, large),
			new(MobileControlAction.AimModeToggle, "AimUseMode", move + new Vector2(-96f, 80f) * scale, small),
			new(MobileControlAction.UseItem, "HeldItem", attack + new Vector2(-126f, 7f) * scale, medium),
			new(MobileControlAction.Inventory, "BackpackIcon", hotbar.InventoryButton.Center.ToVector2(), hotbar.InventoryButton.Width * .5f),
			new(MobileControlAction.Settings, "Settings", hotbar.SettingsButton.Center.ToVector2(), hotbar.SettingsButton.Width * .5f)
		};

		if (config.ControlLayout == MobileControlLayoutMode.Advanced) {
			buttons.Add(new(MobileControlAction.QuickPotions, "QuickPotions", move + new Vector2(110f, -84f) * scale, large));
			buttons.Add(new(MobileControlAction.Zoom, "ZoomPair", move + new Vector2(104f, 80f) * scale, small));
		}
		else {
			buttons.Add(new(MobileControlAction.Heal, "Heal", move + new Vector2(110f, -84f) * scale, large));
			buttons.Add(new(MobileControlAction.Mana, "Mana", move + new Vector2(128f, 7f) * scale, small));
			buttons.Add(new(MobileControlAction.Buff, "Buff", move + new Vector2(106f, 80f) * scale, small));
			buttons.Add(new(MobileControlAction.ZoomOut, "ZoomOut", move + new Vector2(176f, 80f) * scale, 16f * scale));
			buttons.Add(new(MobileControlAction.ZoomIn, "ZoomIn", attack + new Vector2(-176f, 80f) * scale, 16f * scale));
		}

		buttons.Add(CreatePerformanceButton(config, width, height));
		if (config.ShowTopButtons) {
			for (int i = 0; i < 3; i++) {
				Rectangle bounds = GetToolbarButton(config, i, width, height);
				MobileControlAction action = i == 0 ? MobileControlAction.Chat : i == 1 ? MobileControlAction.MapStyle : MobileControlAction.Map;
				buttons.Add(new MobileControlButton(action, i == 0 ? "Chat" : i == 1 ? "MapStyle" : "Map", bounds.Center.ToVector2(), bounds.Width * 0.5f));
			}
		}

		return new MobileControlLayout(move, 66f * scale, attack, 66f * scale, buttons.ToArray(), scale);
	}

	private static MobileControlLayout CreateCustomControlsLayout(MobileCustomPreset preset, int width, int height, float scale, MobileJoystickConfig config)
	{
		Vector2 move = Vector2.Zero;
		Vector2 aim = Vector2.Zero;
		float moveRadius = 0f;
		float aimRadius = 0f;
		List<MobileControlButton> buttons = new();
		List<MobileControlStick> sticks = new();
		foreach (MobileCustomControl control in preset.Controls) {
			float radius = GetCustomControlRadius(control, scale, height);
			Vector2 center = GetCustomControlCenter(control, width, height, radius);
			if (control.Launcher != null) {
				if (control.Launcher.Flag("isVisible", true)) buttons.Add(new MobileControlButton(MobileControlAction.LauncherControl, "", center, radius, control.Id, control.Multitouch,
					launcher: control.Launcher, dimensions: control.Launcher.Dimensions(height, control.Size), customTexture: control.CustomTexture));
				continue;
			}
			if (control.Kind == MobileCustomControlKind.Joystick) {
				float joystickRadius = radius;
				sticks.Add(new(control.Id, control.JoystickRole, center, radius, control.TextureName, control.CustomTexture));
				if (control.JoystickRole == MobileJoystickRole.Grapple)
					buttons.Add(new(MobileControlAction.Grapple, "GrappleHook", center, radius, control.Id, customTexture: control.CustomTexture));
				if (control.JoystickRole == MobileJoystickRole.Move && moveRadius == 0f) {
					move = center;
					moveRadius = joystickRadius;
				}
				else if (control.JoystickRole == MobileJoystickRole.Aim && aimRadius == 0f) {
					aim = center;
					aimRadius = joystickRadius;
				}
				continue;
			}

			MobileControlAction action = control.Kind == MobileCustomControlKind.RadialMenu
				? MobileControlAction.RadialMenu : control.Action;
			string textureName = string.IsNullOrWhiteSpace(control.TextureName) ? GetDefaultTexture(action) : control.TextureName;
			float buttonRadius = radius;
			buttons.Add(new MobileControlButton(action, textureName, center, buttonRadius, control.Id, control.Multitouch,
				control.Kind == MobileCustomControlKind.RadialMenu ? control.RadialActions : null, control.ModKeybind, control.RadialModKeybinds,
				keyboardKey: control.KeyboardKey, radialKeyboardKeys: control.RadialKeyboardKeys, customTexture: control.CustomTexture));
		}

		if (!buttons.Exists(button => button.Action == MobileControlAction.PerformanceDisplay))
			buttons.Add(CreatePerformanceButton(config, width, height));
		return new MobileControlLayout(move, moveRadius, aim, aimRadius, buttons.ToArray(), scale, sticks);
	}

	internal static Vector2 GetCustomControlCenter(MobileCustomControl control, int width, int height, float radius)
		=> control.Launcher != null ? control.Launcher.Center(control, width, height)
			: ClampCustomControlCenter(new(control.X * width, control.Y * height), radius, width, height);

	internal static float GetCustomControlRadius(MobileCustomControl control, float scale, int height = 0)
		=> control.Launcher != null ? Math.Max(control.Launcher.Number("width", .2f), control.Launcher.Number("height", .2f)) * (height > 0 ? height : Main.screenHeight) * control.Size / 2
			: (control.Kind == MobileCustomControlKind.Joystick ? 66f : 24f) * scale
			* (control.Kind != MobileCustomControlKind.Joystick && control.Action == MobileControlAction.Inventory
				? Math.Max(1f, control.Size) : control.Size);

	internal static Vector2 ClampCustomControlCenter(Vector2 center, float radius, int width, int height, MobileJoystickConfig config = null)
	{
		float insetX = Math.Min(radius, width * .5f);
		float insetY = Math.Min(radius, height * .5f);
		return new Vector2(Math.Clamp(center.X, insetX, width - insetX), Math.Clamp(center.Y, insetY, height - insetY));
	}

	internal static string GetDefaultTexture(MobileControlAction action) => action switch {
		MobileControlAction.Jump => "Jump",
		MobileControlAction.Grapple => "GrappleHook",
		MobileControlAction.Heal => "Heal",
		MobileControlAction.Mana => "Mana",
		MobileControlAction.Buff => "Buff",
		MobileControlAction.Mount => "Mount",
		MobileControlAction.SmartCursor or MobileControlAction.SmartInteract => "SmartCursor",
		MobileControlAction.AimModeToggle => "AimUseMode",
		MobileControlAction.TargetLockToggle => "TargetLockOn",
		MobileControlAction.Interact or MobileControlAction.UseItem => "ItemButtonFrame",
		MobileControlAction.Inventory => "BackpackIcon",
		MobileControlAction.Map => "Map",
		MobileControlAction.MapStyle or MobileControlAction.MapAlphaCycle => "MapStyle",
		MobileControlAction.Settings or MobileControlAction.ToggleGameControls => "Settings",
		MobileControlAction.TogglePointerMode or MobileControlAction.PerformanceDisplay or MobileControlAction.ShowKeyboard => "PhoneTealCircle",
		MobileControlAction.Chat => "ChatBubble",
		MobileControlAction.ZoomIn => "ZoomIn",
		MobileControlAction.ZoomOut => "ZoomOut",
		MobileControlAction.QuickPotions => "QuickPotions",
		MobileControlAction.Zoom => "ZoomPair",
		MobileControlAction.RadialMenu => "DirectionalArrowsOriginal",
		MobileControlAction.SwapUseInteract => "ItemButtonFrame",
		MobileControlAction.CycleUseProfile or MobileControlAction.AutoSelect => "SmartCursor",
		MobileControlAction.QuickMount => "Mount",
		MobileControlAction.Loadout1 or MobileControlAction.Loadout2 or MobileControlAction.Loadout3 or MobileControlAction.SwitchLoadout => "BackpackIcon",
		_ => "ItemButtonFrame"
	};

	private static MobileControlButton CreatePerformanceButton(MobileJoystickConfig config, int width, int height)
	{
		Rectangle bounds = GetToolbarButton(config, -1, width, height);
		return new(MobileControlAction.PerformanceDisplay, "PhoneTealCircle", bounds.Center.ToVector2(), bounds.Width * .5f);
	}

	public static Rectangle GetToolbarButton(MobileJoystickConfig config, int index, int width, int height)
	{
		float scale = GetControlScale(config, width, height);
		int size = (int)(40f * scale);
		int edge = Math.Max(4, (int)(config.EdgePadding * scale * 0.5f));
		return new Rectangle(width - edge - size * (3 - index), Math.Max(4, (int)(6f * scale)), size, size);
	}

	public static Rectangle GetMinimapBounds(MobileJoystickConfig config, int width, int height)
	{
		float scale = GetControlScale(config, width, height);
		int mapWidth = (int)(200f * scale);
		int mapHeight = (int)(128f * scale);
		int edge = Math.Max(4, (int)((config?.EdgePadding ?? 18) * scale * .5f));
		return new Rectangle(width - edge - (int)(40f * scale) - mapWidth, (int)(64f * scale), mapWidth, mapHeight);
	}
}
