using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.Graphics;
using Terraria.Localization;
using Terraria.UI;
using Terraria.ModLoader.Config;
using Terraria.GameContent.UI.BigProgressBar;
using Terraria.Social;
using System.Linq;

namespace MobileJoystick;

internal enum MobileSettingKind { Toggle, Slider, Choice, Action }

internal sealed class MobileSetting
{
	public Func<string> LabelText;
	public string Label => LabelText();
	public MobileSettingKind Kind;
	public Func<float> Read;
	public Action<float> Write;
	public Func<string> Display;
	public Action Activate;
	public Func<string[]> ChoiceTexts;
	private GameCulture choicesCulture;
	private string[] choices;
	public string[] Choices {
		get {
			if (choices == null || choicesCulture != Language.ActiveCulture) {
				choices = ChoiceTexts();
				choicesCulture = Language.ActiveCulture;
			}
			return choices;
		}
	}
	public float Min, Max;
	public bool ModSetting;
}

internal static class MobileSettingsCatalog
{
	// tModLoader resolves the current client language and falls back to en-US.
	internal static string Text(string key, params object[] args) => MobileControlsText.Get(key, args);
	internal static readonly int[] VisibleCategories = { 0, 1, 2, 3, 4, 5, 7 };
	private static readonly GameCulture.CultureName[] GameLanguages = Enum.GetValues<GameCulture.CultureName>()
		.Where(name => name != GameCulture.CultureName.Unknown).ToArray();
	internal static string Heading(int category) => category switch {
		0 => Text("Settings"), 3 => Text("AudioHeading"),
		1 => Text("Interface"), 2 => Text("Video"), 4 => Text("Controls"), 5 => Text("Touch"),
		8 => Text("ModConfiguration"), 9 => Text("CursorColors"),
		10 => Text("LauncherControlPacks"),
		_ => Text("Mod")
	};

	public static List<MobileSetting> Create(int category, Action keybinds, Action modConfigs, Action cursorColors, Action<System.Action> displayChange, bool includeInterfaceLayoutEditor = true)
	{
		var items = new List<MobileSetting>();
		MobileJoystickConfig c = MobileJoystickConfig.Instance;
		void Toggle(string key, Func<bool> get, Action<bool> set, bool mod = false) => items.Add(new MobileSetting {
			LabelText = () => Text(key), Kind = MobileSettingKind.Toggle, Read = () => get() ? 1 : 0,
			Write = v => set(v > .5f), ModSetting = mod });
		void Slider(string key, Func<float> get, Action<float> set, float min, float max, bool mod = false, bool percent = true) => items.Add(new MobileSetting {
			LabelText = () => Text(key), Kind = MobileSettingKind.Slider, Read = get, Write = set, Min = min, Max = max,
			Display = () => percent ? $"{get() * 100:0}%" : $"{get():0}", ModSetting = mod });
		void Choice(string key, Func<string[]> choices, Func<int> get, Action<int> set) => items.Add(new MobileSetting {
			LabelText = () => Text(key), Kind = MobileSettingKind.Choice, ChoiceTexts = choices,
			Read = () => get(), Write = v => set((int)v) });
		void Action(string key, System.Action action) => items.Add(new MobileSetting {
			LabelText = () => Text(key), Kind = MobileSettingKind.Action, Activate = action });

		switch (category) {
			case 0:
				Toggle("Autosave", () => Main.autoSave, v => Main.autoSave = v);
				Toggle("AutopauseWithInventory", () => Main.autoPause, v => Main.autoPause = v);
				Toggle("EnableMap", () => Main.mapEnabled, v => Main.mapEnabled = v);
				Toggle("Autofire", () => Main.SettingsEnabled_AutoReuseAllItems, v => Main.SettingsEnabled_AutoReuseAllItems = v);
				Toggle("ReverseArmorSetKey", () => Main.ReversedUpDownArmorSetBonuses, v => Main.ReversedUpDownArmorSetBonuses = v);
				Choice("SmartDoors", () => new[] { Text("Off"), Text("AllInputs") },
					() => DoorOpeningHelper.PreferenceSettings == DoorOpeningHelper.DoorAutoOpeningPreference.EnabledForEverything ? 1 : 0,
					v => { for(int i=0;i<3;i++) { int current=DoorOpeningHelper.PreferenceSettings == DoorOpeningHelper.DoorAutoOpeningPreference.EnabledForEverything ? 1 : 0; if(current==v) break; DoorOpeningHelper.CyclePreferences(); } });
				Choice("HoverControl", () => new[]{Text("Hold"),Text("Toggle")}, () => Player.Settings.HoverControl == Player.Settings.HoverControlMode.Hold ? 0 : 1,
					v => { if ((Player.Settings.HoverControl == Player.Settings.HoverControlMode.Hold ? 0 : 1) != v) Player.Settings.CycleHoverControl(); });
				Action("ModConfigurationAllMods", modConfigs);
				Action("KeyBindings", keybinds);
				if(SocialAPI.Network?.CanInvite()==true) Action("InviteFriends", () => SocialAPI.Network.OpenInviteInterface());
				break;
			case 1:
				Choice("GameLanguage",
					() => GameLanguages.Select(name => Language.GetTextValue("Language." + name)).ToArray(),
					() => Array.FindIndex(GameLanguages, name => GameCulture.FromCultureName(name).IsActive),
					v => displayChange(() => LanguageManager.Instance.SetLanguage(GameCulture.FromCultureName(GameLanguages[v]))));
				if (includeInterfaceLayoutEditor) Action("InterfaceLayoutEditor", MobileMenuSystem.OpenInterfaceLayoutEditor);
				Slider("UIScale", () => Main.UIScaleWanted, v => Main.UIScale = v, .5f, 2f);
				Slider("UIFontSize", () => c.UIFontScale, v => c.UIFontScale = v, .9f, 1.4f, true);
				Slider("GameZoom", () => Main.GameZoomTarget, v => Main.GameZoomTarget = v, 1f, 2f);
				Slider("MinimapScale", () => Main.MapScale, v => Main.MapScale = v, .5f, 1f);
				Toggle("ItemNames", () => Main.showItemText, v => Main.showItemText = v);
				Toggle("HighlightNewItems", () => ItemSlot.Options.HighlightNewItems, v => ItemSlot.Options.HighlightNewItems = v);
				Toggle("OpaqueTooltips", () => Main.SettingsEnabled_OpaqueBoxBehindTooltips, v => Main.SettingsEnabled_OpaqueBoxBehindTooltips = v);
				Action("HealthAndManaStyle", () => Main.ResourceSetsManager.CycleResourceSet());
				items[^1].Display = () => Main.ResourceSetsManager.ActiveSet.DisplayedName;
				Choice("InvasionProgress", () => Enumerable.Range(124,3).Select(i => Lang.menu[i].Value).ToArray(), () => Main.invasionProgressMode, v => Main.invasionProgressMode=v);
				Toggle("PlacementPreview", () => Main.placementPreview, v => Main.placementPreview=v);
				Toggle("BuildingGrid", () => Main.MouseShowBuildingGrid, v => Main.MouseShowBuildingGrid=v);
				Action("BossBarStyle", MobileNativeSettings.CycleBossBar);
				items[^1].Display = MobileNativeSettings.BossBarLabel;
				Action("NativeMinimapBorder", () => Main.MinimapFrameManagerInstance.CycleSelection());
				items[^1].Display = () => Language.GetTextValue("UI.MinimapFrame_"+Main.MinimapFrameManagerInstance.ActiveSelectionKeyName);
				Toggle("BossHealthText", () => BigProgressBarSystem.ShowText, v => { if(BigProgressBarSystem.ShowText!=v) BigProgressBarSystem.ToggleShowText(); });
				break;
			case 2:
				Choice("LightingMode", () => new[] { "White", "Retro", "Trippy", "Color" }.Select(mode => Language.GetTextValue("UI.LightMode_" + mode)).ToArray(), () => (int)Lighting.Mode, v => { for (int i=0; i<4 && (int)Lighting.Mode != v; i++) Lighting.NextLightMode(); });
				Toggle("Background", () => Main.BackgroundEnabled, v => Main.BackgroundEnabled = v);
				Toggle("BloodAndGore", () => ChildSafety.Disabled, v => ChildSafety.Disabled=v);
				Choice("Quality", () => new[] { Text("Auto"), Text("Low"), Text("Medium"), Text("High") }, () => Main.qaStyle, v => Main.qaStyle = v);
				Slider("Parallax", () => Main.bgScroll, v => { Main.bgScroll = (int)v; Main.caveParallax = 1f - Main.bgScroll / 500f; }, 0, 100, percent:false);
				Toggle("HeatDistortion", () => Main.UseHeatDistortion, v => Main.UseHeatDistortion = v);
				Toggle("StormEffects", () => Main.UseStormEffects, v => Main.UseStormEffects = v);
				Toggle("WindSway", () => Main.SettingsEnabled_TilesSwayInWind, v => Main.SettingsEnabled_TilesSwayInWind = v);
				Choice("Waves", () => new[] { Text("Off"), Text("Low"), Text("Medium"), Text("High") }, () => Main.WaveQuality, v => Main.WaveQuality = v);
				Toggle("Fullscreen", () => Main.graphics.IsFullScreen, v => { if(Main.graphics.IsFullScreen!=v) displayChange(Main.ToggleFullScreen); });
				Action("ResolutionClickToCycle", () => displayChange(MobileNativeSettings.CycleResolution));
				items[^1].Display = () => Main.PendingResolutionWidth+" × "+Main.PendingResolutionHeight;
				Choice("FrameSkip", () => Enumerable.Range(247,3).Select(i => Lang.menu[i].Value).ToArray(), () => (int)Main.FrameSkipMode,
					v => { for(int i=0;i<3 && (int)Main.FrameSkipMode!=v;i++) Main.CycleFrameSkipMode(); });
				break;
			case 3:
				Slider("Music", () => Main.musicVolume, v => Main.musicVolume = v, 0, 1);
				Slider("Sound", () => Main.soundVolume, v => Main.soundVolume = v, 0, 1);
				Slider("Ambient", () => Main.ambientVolume, v => Main.ambientVolume = v, 0, 1);
				break;
			case 4:
				Action("LauncherControlPacks", MobileMenuSystem.OpenLauncherControlPacks);
				Toggle("PlacementPreview", () => Main.placementPreview, v => Main.placementPreview = v);
				Toggle("ToggleSmartCursor", () => Main.cSmartCursorModeIsToggleAndNotHold, v => Main.cSmartCursorModeIsToggleAndNotHold = v);
				Toggle("SmartBlockPlacement", () => Player.SmartCursorSettings.SmartBlocksEnabled, v => Player.SmartCursorSettings.SmartBlocksEnabled = v);
				Toggle("SmartAxeAfterPickaxe", () => Player.SmartCursorSettings.SmartAxeAfterPickaxe, v => Player.SmartCursorSettings.SmartAxeAfterPickaxe = v);
				Toggle("LeftStickUpToJump", () => c.JoystickUpJumps, v => c.JoystickUpJumps = v, true);
				Action("KeyBindings", keybinds);
				Action("CursorAndBorderColors", cursorColors);
				Choice("TargetLockMode", () => Enumerable.Range(232,3).Select(i => Lang.menu[i].Value).ToArray(), () => (int)LockOnHelper.UseMode,
					v => { for(int i=0;i<3 && (int)LockOnHelper.UseMode!=v;i++) LockOnHelper.CycleUseModes(); });
				break;
			case 5:
				Action("TouchControls", () => MobileMenuSystem.OpenTouchEditor());
				Action("LauncherControlPacks", MobileMenuSystem.OpenLauncherControlPacks);
				Toggle("TouchInput", () => c.EnableTouchInput, v => c.EnableTouchInput = v, true);
				Toggle("MouseTouchSimulation", () => c.EnableMouseFallback, v => c.EnableMouseFallback = v, true);
				Toggle("ShowMouseCursor", () => c.ShowMouseCursor, v => c.ShowMouseCursor = v, true);
				Toggle("PinchZoom", () => c.EnablePinchZoom, v => c.EnablePinchZoom = v, true);
				Choice("HotbarOrientation", () => new[] { Text("Vertical"), Text("Horizontal") },
					() => (int)c.HotbarOrientation, v => c.HotbarOrientation = (MobileHotbarOrientation)v);
				Toggle("HideGameControls", () => c.HideGameControls, v => c.HideGameControls = v, true);
				Slider("ControlSize", () => c.ControlScale, v => c.ControlScale = MathF.Round(v*20)/20, .6f, 1.8f, true);
				Slider("ControlOpacity", () => c.Opacity, v => c.Opacity = v, .35f, 1, true);
				Slider("StickDeadzone", () => c.JoystickDeadzone, v => c.JoystickDeadzone = v, .05f, .55f, true);
				Slider("CursorSensitivity", () => c.CursorSensitivity, v => c.CursorSensitivity = v, .25f, 3f, true);
				Slider("CursorOpacity", () => c.CursorOpacity, v => c.CursorOpacity = v, .4f, 1f, true);
				Slider("ReticleSize", () => c.AimReticleSize, v => c.AimReticleSize = MathF.Round(v / 2f) * 2f, 12, 80, true, false);
				Slider("CrosshairSize", () => c.AimCrosshairSize, v => c.AimCrosshairSize = MathF.Round(v / 2f) * 2f, 12, 80, true, false);
				Slider("AimDistance", () => c.AimDistance, v => c.AimDistance = (int)v, 160, 720, true, false);
				Slider("EdgePadding", () => c.EdgePadding, v => c.EdgePadding = (int)v, 0, 96, true, false);
				break;
			case 7:
				Action("ModConfigurationAllMods", modConfigs);
				if (MobileCalamityUiSystem.Available) Toggle("LockCalamityMeters", () => MobileCalamityUiSystem.Locked, v => MobileCalamityUiSystem.Locked = v);
				Toggle("TopShortcuts", () => c.ShowTopButtons, v => c.ShowTopButtons=v, true);
				Slider("ControlSize", () => c.ControlScale, v => c.ControlScale=MathF.Round(v*20)/20, .6f, 1.8f, true);
				Slider("Opacity", () => c.Opacity, v => c.Opacity=v, .35f, 1, true);
				break;
			case 9:
				foreach (bool border in new[]{false,true}) {
					for(int component=0;component<3;component++) {
						int axis=component;
						string key = (border ? "Border" : "Cursor") + (component == 0 ? "Hue" : component == 1 ? "Saturation" : "Lightness");
						Slider(key, () => MobileNativeSettings.CursorComponent(border,axis), v => MobileNativeSettings.SetCursorComponent(border,axis,v), !border && axis==2 ? .15f : 0,1);
					}
				}
				Slider("BorderOpacity", () => Main.mouseBorderColorSlider.Alpha, v => {Main.mouseBorderColorSlider.Alpha=v; Main.MouseBorderColor=Main.mouseBorderColorSlider.GetColor();},0,1);
				break;
		}
		return items;
	}

	internal static List<MobileSetting> ModConfigs(Action<ModConfig> open) => MobileNativeSettings.Configs()
		.OrderBy(c => c.Mod.DisplayName).ThenBy(c => c.DisplayName.Value)
		.Select(c => new MobileSetting { LabelText=()=>c.Mod.DisplayName+" · "+c.DisplayName.Value, Kind=MobileSettingKind.Action,
			Display=()=>Text("Open"), Activate=()=>open(c) }).ToList();
}
