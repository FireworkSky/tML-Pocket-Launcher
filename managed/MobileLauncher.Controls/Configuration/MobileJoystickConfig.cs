using System;
using Terraria;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace MobileJoystick;

public enum MobileControlLayoutMode { Simple, Advanced, Custom }
public enum MobileHotbarOrientation { Vertical, Horizontal }

public class MobileJoystickConfig
{
	internal const MobileControlLayoutMode DefaultControlLayout = MobileControlLayoutMode.Advanced;
	private const int MaxProfileBytes = 2 * 1024 * 1024;

	public static MobileJoystickConfig Instance { get; private set; }


	private static string ConfigPath => Path.Combine(Main.SavePath, "MobileControls.json");
	[JsonExtensionData]
	private IDictionary<string, Newtonsoft.Json.Linq.JToken> Extra { get; set; }

	internal static void Load()
	{
		string legacy = Path.Combine(Main.SavePath, "ModConfigs", "MobileJoystick_MobileJoystickConfig.json");
		string path = File.Exists(ConfigPath) ? ConfigPath : legacy;
		var config = new MobileJoystickConfig();
		try {
			if (File.Exists(path) && new FileInfo(path).Length <= MaxProfileBytes)
				JsonConvert.PopulateObject(File.ReadAllText(path), config, new JsonSerializerSettings { MaxDepth = 40 });
		}
		catch (Exception error) when (error is IOException or JsonException) {
			MobileControlsSession.Logger.Warn("Could not read mobile controls settings", error);
			config = new MobileJoystickConfig();
		}
		config.Apply();
	}

	internal void SaveFromMobileMenu()
	{
		string path = ConfigPath;
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try {
			byte[] profile = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(this, Formatting.None));
			if (profile.Length > MaxProfileBytes) throw new IOException("The mobile control profile exceeds 2 MiB.");
			File.WriteAllBytes(temporary, profile);
			File.Move(temporary, path, true);
		}
		finally { if (File.Exists(temporary)) File.Delete(temporary); }
	}

	[JsonIgnore]
	public bool EnableMobileControls { get; set; } = true;

	[JsonIgnore]
	public bool UseMobileInventory { get; set; } = true;

	public bool EnableTouchInput { get; set; } = true;

	public bool EnableMouseFallback { get; set; } = true;

	private bool showMouseCursor;
	public bool ShowMouseCursor {
		get => showMouseCursor;
		set {
			showMouseCursor = value;
			// The bundled FNA patch also guards cursor draws that bypass MobileJoystickSystem.
			AppContext.SetSwitch("MobileLauncher.ShowMouseCursor", value);
		}
	}

	public bool EnablePinchZoom { get; set; } = true;

	public bool HideGameControls { get; set; } = false;

	public bool QuickStackToNearbyChests { get; set; } = true;

	public bool JoystickUpJumps { get; set; } = true;

	public bool ShowTopButtons { get; set; } = true;

	[JsonIgnore]
	public bool UseMobileMinimap { get; set; } = true;

	public MobileControlLayoutMode ControlLayout { get; set; } = DefaultControlLayout;

	public MobileHotbarOrientation HotbarOrientation { get; set; } = MobileHotbarOrientation.Vertical;

	public int ActiveCustomPresetIndex { get; set; } = 0;

	public System.Collections.Generic.List<MobileCustomPreset> CustomPresets { get; set; } = new();

	public MobileInterfaceLayoutProfile InterfaceLayout { get; set; }

	public float ControlScale { get; set; } = 1.8f;

	public float Opacity { get; set; } = 0.78f;

	// Multiplies every menu, inventory and HUD label. The phone reference uses a
	// larger body size than the old 0.65-0.9 multipliers produced.
	public float UIFontScale { get; set; } = 1.18f;

	public int EdgePadding { get; set; } = 18;


	public float LeftStickOffsetX { get; set; } = 0;

	public float LeftStickOffsetY { get; set; } = 0;

	public float RightStickOffsetX { get; set; } = 0;

	public float RightStickOffsetY { get; set; } = 0;

	public float JoystickDeadzone { get; set; } = 0.25f;

	public int AimDistance { get; set; } = 440;

	public float CursorSensitivity { get; set; } = 1f;

	public float CursorOpacity { get; set; } = 1f;

	public float AimReticleSize { get; set; } = 28f;

	public float AimCrosshairSize { get; set; } = 36f;

	internal void Apply()
	{
		AppContext.SetSwitch("MobileLauncher.ShowMouseCursor", ShowMouseCursor);
		EdgePadding = Math.Clamp(EdgePadding, 0, 96);
		CursorSensitivity = Math.Clamp(CursorSensitivity, .25f, 3f);
		CursorOpacity = Math.Clamp(CursorOpacity, .4f, 1f);
		AimReticleSize = Math.Clamp(AimReticleSize, 12f, 80f);
		AimCrosshairSize = Math.Clamp(AimCrosshairSize, 12f, 80f);
		EnableMobileControls = true;
		UseMobileInventory = true;
		UseMobileMinimap = true;
		if (ControlScale > 1.8f) {
			ControlScale = 1.8f;
		}

		if (ControlScale < 0.6f) {
			ControlScale = 0.6f;
		}

		UIFontScale = Math.Clamp(UIFontScale, 0.9f, 1.4f);

		LeftStickOffsetX = Math.Clamp(LeftStickOffsetX, -800f, 800f);
		LeftStickOffsetY = Math.Clamp(LeftStickOffsetY, -800f, 800f);
		RightStickOffsetX = Math.Clamp(RightStickOffsetX, -800f, 800f);
		RightStickOffsetY = Math.Clamp(RightStickOffsetY, -800f, 800f);

		Instance = this;
		InterfaceLayout?.Validate();
		MobileCustomControls.EnsureInitialized(this);
	}
}
