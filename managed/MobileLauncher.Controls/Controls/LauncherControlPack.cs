using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Terraria;

namespace MobileJoystick;

internal sealed class LauncherControlPack
{
	internal MobileCustomPreset Preset { get; private set; }
	internal readonly List<string> Warnings = new();
	internal static string ImportRoot => Path.Combine(Main.SavePath, "ModConfigs", "MobileJoystick", "ControlPacks");
	internal static string[] Sources()
	{
		Directory.CreateDirectory(ImportRoot);
		return Directory.GetFiles(ImportRoot).Where(path => new[] { ".zip", ".json" }.Contains(Path.GetExtension(path).ToLowerInvariant()) && !Path.GetFileName(path).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
			.Concat(Directory.GetDirectories(ImportRoot).Where(path => File.Exists(Path.Combine(path, "layout.json"))))
			.OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToArray();
	}
	internal static void OpenDirectory()
	{
		Directory.CreateDirectory(ImportRoot);
		Process.Start(new ProcessStartInfo { FileName = ImportRoot, UseShellExecute = true });
	}
	internal static LauncherControlPack Read(string path)
	{
		if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)) {
			using var archive = new LauncherControlPackArchive(File.OpenRead(path));
			return Parse(archive.ReadFile("layout.json", 2_000_000), archive.ReadFile("manifest.json", 1_000_000));
		}
		string folder = Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path));
		string layout = Directory.Exists(path) ? Path.Combine(path, "layout.json") : path;
		string manifest = Path.Combine(folder, "manifest.json");
		return Parse(ReadBounded(layout, 2_000_000), File.Exists(manifest) ? ReadBounded(manifest, 1_000_000) : null);
	}

	internal static JObject Json(byte[] bytes)
	{
		using var reader = new JsonTextReader(new StringReader(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'))) { MaxDepth = 40, DateParseHandling = DateParseHandling.None };
		JObject result = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
		if (reader.Read()) throw new InvalidDataException("Trailing JSON data");
		return result;
	}
	internal static LauncherControlPack Parse(byte[] layoutBytes, byte[] manifestBytes)
	{
		if (layoutBytes.Length > 2_000_000 || manifestBytes?.Length > 1_000_000) throw new InvalidDataException("Control pack JSON is too large");
		JObject layout = Json(layoutBytes);
		int version = layout.Value<int?>("version") ?? 1;
		if (version < 1 || version > 3) throw new InvalidDataException("Unsupported layout version: " + version);
		JObject manifest = manifestBytes == null ? null : Json(manifestBytes);
		if (layout["controls"] is not JArray controls || controls.Count == 0 || controls.Count > 256) throw new InvalidDataException("Expected 1–256 controls");
		var pack = new LauncherControlPack { Preset = new MobileCustomPreset { Name = manifest?.Value<string>("name") ?? layout.Value<string>("name") ?? "RotatingartLauncher" } };
		foreach (JToken token in controls) {
			if (token is not JObject raw) throw new InvalidDataException("Invalid control");
			var data = (JObject)raw.DeepClone();
			if (data.Value<string>("type") is "dpad" or "touchpad" or "mousewheel" or "text") continue;
			MobileLauncherControl.Validate(data);
			float x = data.Value<float?>("x") ?? 0, y = data.Value<float?>("y") ?? 0;
			if (!float.IsFinite(x) || !float.IsFinite(y) || x < 0 || x > 1 || y < 0 || y > 1) throw new InvalidDataException("Invalid control position");
			var control = new MobileCustomControl {
				Id = "ral-" + Guid.NewGuid().ToString("N"), Kind = MobileLauncherControl.Kind(data.Value<string>("type")),
				X = x, Y = y, Size = 1, Action = MobileControlAction.LauncherControl,
				JoystickRole = MobileJoystickRole.Unbound, Multitouch = data.Value<bool>("multitouch"), Launcher = new MobileLauncherControl { Data = data }
			};
			foreach (string key in LauncherControlBindings.ControlKeys(data).Distinct())
				if (!LauncherControlBindings.Supported(key)) pack.Warnings.Add(control.Launcher.String("name") + ": " + key);
			if (control.Launcher.Flag("isPassThrough")) pack.Warnings.Add(control.Launcher.String("name") + ": " + MobileSettingsCatalog.Text("PackPassThroughWarning"));
			pack.Preset.Controls.Add(control);
		}
		MobileCustomControls.Validate(pack.Preset);
		if (pack.Preset.Controls.Count == 0) throw new InvalidDataException("The layout contains no supported controls");
		return pack;
	}
	private static byte[] ReadBounded(string path, int maximum)
	{
		using var stream = File.OpenRead(path);
		if (stream.Length > maximum) throw new InvalidDataException("File too large: " + Path.GetFileName(path));
		byte[] bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); return bytes;
	}
	internal void Install(int slot)
	{
		MobileJoystickConfig config = MobileJoystickConfig.Instance;
		if (slot < 0 || slot >= MobileCustomControls.PresetCount) throw new ArgumentOutOfRangeException(nameof(slot));
		MobileCustomControls.EnsureInitialized(config);
		var previous = config.CustomPresets[slot];
		var mode = config.ControlLayout; int selected = config.ActiveCustomPresetIndex;
		try {
			config.CustomPresets[slot] = MobileCustomControls.Clone(Preset);
			config.ActiveCustomPresetIndex = slot;
			config.ControlLayout = MobileControlLayoutMode.Custom;
			config.SaveFromMobileMenu();
			MobileJoystickInput.Reset();
		}
		catch { config.CustomPresets[slot] = previous; config.ActiveCustomPresetIndex = selected; config.ControlLayout = mode; throw; }
	}
}
