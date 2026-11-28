using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Newtonsoft.Json.Linq;

namespace MobileJoystick;

// Keep the launcher schema intact, including fields added by newer pack authors.
// Coordinates in the mod remain centers; launcher width and height use screen height.
public sealed class MobileLauncherControl
{
	private static long nextRenderId;
	internal readonly long RenderId = System.Threading.Interlocked.Increment(ref nextRenderId);
	public JObject Data { get; set; } = new();
	public MobileLauncherControl Clone() => new() { Data = (JObject)Data.DeepClone() };
	internal string Type => String("type", "button");
	internal float Number(string key, float fallback = 0) => Data.Value<float?>(key) ?? fallback;
	internal string String(string key, string fallback = "") => Data.Value<string>(key) ?? fallback;
	internal bool Flag(string key, bool fallback = false) => Data.Value<bool?>(key) ?? fallback;
	internal Vector2 Dimensions(int height, float size) => new(Number("width", .2f) * height * size, Number("height", .2f) * height * size);
	internal Vector2 Center(MobileCustomControl control, int width, int height)
		=> new Vector2(control.X * width, control.Y * height) + Dimensions(height, control.Size) * .5f;
	internal Vector2 Local(Vector2 point, Vector2 center)
		=> Vector2.Transform(point - center, Matrix.CreateRotationZ(-MathHelper.ToRadians(Number("rotation"))));
	internal bool Contains(Vector2 point, Vector2 center, Vector2 dimensions)
	{
		Vector2 local = Local(point, center);
		Vector2 uv = local / dimensions + new Vector2(.5f);
		if (uv.X < 0 || uv.Y < 0 || uv.X > 1 || uv.Y > 1) return false;
		if (String("shape") == "CIRCLE" || Type is "joystick" or "radialmenu")
			return local.LengthSquared() <= MathF.Pow(Math.Min(dimensions.X, dimensions.Y) / 2, 2);
		if (String("shape") == "POLYGON" && Data["polygonPoints"] is JArray points && points.Count >= 3) {
			bool inside = false;
			for (int i = 0, j = points.Count - 1; i < points.Count; j = i++) {
				float ax = points[i].Value<float>("x"), ay = points[i].Value<float>("y");
				float bx = points[j].Value<float>("x"), by = points[j].Value<float>("y");
				if ((ay > uv.Y) != (by > uv.Y) && uv.X < (bx - ax) * (uv.Y - ay) / (by - ay) + ax) inside = !inside;
			}
			return inside;
		}
		return true;
	}
	internal static MobileCustomControlKind Kind(string type) => type switch {
		"button" => MobileCustomControlKind.Button, "joystick" => MobileCustomControlKind.Joystick,
		"radialmenu" => MobileCustomControlKind.RadialMenu, _ => throw new InvalidDataException("Unknown control type: " + type)
	};
	internal static JObject Defaults(string type)
	{
		var data = new JObject {
			["type"] = type, ["name"] = type, ["width"] = .16, ["height"] = .16,
			["rotation"] = 0, ["opacity"] = .35, ["borderOpacity"] = .5, ["textOpacity"] = .9,
			["textColor"] = -1, ["bgColor"] = unchecked((int)0xFF2A2A2A), ["strokeColor"] = unchecked((int)0xFFAAAAAA),
			["strokeWidth"] = 1.5, ["cornerRadius"] = 8, ["isVisible"] = true, ["isPassThrough"] = false, ["isSizeRatioLocked"] = true, ["multitouch"] = true
		};
		switch (type) {
			case "button": data["mode"] = "KEYBOARD"; data["keycode"] = "UNKNOWN"; data["isToggle"] = false;
				data["shape"] = "RECTANGLE"; data["polygonPoints"] = new JArray(); break;
			case "joystick": data["mode"] = "KEYBOARD"; data["joystickKeys"] = new JArray("KEYBOARD_W", "KEYBOARD_D", "KEYBOARD_S", "KEYBOARD_A");
				data["stickKnobSize"] = .4; data["stickOpacity"] = .7; break;
			case "radialmenu": data["sectorCount"] = 8; data["expandedScale"] = 2.5; data["deadZoneRatio"] = .3;
				data["expandDuration"] = 150; data["showDividers"] = true; data["dividerColor"] = 0x66FFFFFF;
				data["selectedColor"] = unchecked((int)0x99FFFFFF);
				data["sectors"] = new JArray(Enumerable.Range(1, 8).Select(i => new JObject { ["keycode"] = "KEYBOARD_" + i, ["label"] = i.ToString() })); break;
		}
		return data;
	}
	internal static void FillDefaults(JObject data)
	{
		JObject defaults = Defaults(data.Value<string>("type") ?? "button");
		Merge(data, defaults);
		if (data.Value<string>("type") == "radialmenu" && data["sectors"] is JArray sectors) {
			int count = data.Value<int?>("sectorCount") ?? 8;
			while (sectors.Count < Math.Min(12, count)) sectors.Add(new JObject { ["keycode"] = "UNKNOWN", ["label"] = "" });
			foreach (JObject sector in sectors.OfType<JObject>()) Merge(sector, new JObject { ["keycode"] = "UNKNOWN", ["label"] = "" });
		}
	}
	private static void Merge(JObject target, JObject defaults)
	{
		foreach (JProperty property in defaults.Properties()) {
			if (target[property.Name] == null) target[property.Name] = property.Value.DeepClone();
			else if (target[property.Name] is JObject child && property.Value is JObject template) Merge(child, template);
		}
	}
	internal static bool SupportsMode(JObject data)
		=> data?["mode"] == null || data.Value<string>("mode") == "KEYBOARD"
			|| data.Value<string>("type") == "joystick" && data.Value<string>("mode") == "MOUSE";
	internal static void Validate(JObject data)
	{
		if (data == null) throw new InvalidDataException("Missing control data");
		Kind(data.Value<string>("type"));
		data.Remove("isRightStick");
		data.Remove("texture");
		data.Remove("useTextureAlphaHitTest");
		void ClearPointerBinding(JObject source)
		{
			if (LauncherControlBindings.RemovedPointerBinding(source.Value<string>("keycode")))
				source["keycode"] = "UNKNOWN";
		}
		ClearPointerBinding(data);
		if (data["sectors"] is JArray oldSectors)
			foreach (JObject sector in oldSectors.OfType<JObject>()) {
				sector.Remove("iconPath");
				ClearPointerBinding(sector);
			}
		if (data["joystickKeys"] is JArray oldKeys)
			for (int index = 0; index < oldKeys.Count; index++)
				if (oldKeys[index].Type == JTokenType.String && LauncherControlBindings.RemovedPointerBinding(oldKeys[index].Value<string>()))
					oldKeys[index] = "UNKNOWN";
		if (LauncherControlBindings.ControlKeys(data).Any(LauncherControlBindings.RemovedBinding))
			throw new InvalidDataException("Unsupported control binding");
		FillDefaults(data);
		void Types(JObject value, JObject template) {
			foreach (JProperty property in template.Properties()) {
				JToken actual = value[property.Name];
				if (property.Value.Type is JTokenType.Float or JTokenType.Integer) {
					if (actual?.Type is not (JTokenType.Float or JTokenType.Integer)) throw new InvalidDataException("Expected number: " + property.Name);
				}
				else if (actual?.Type != property.Value.Type) throw new InvalidDataException("Invalid field type: " + property.Name);
				if (actual is JObject child && property.Value is JObject defaults) Types(child, defaults);
			}
		}
		Types(data, Defaults(data.Value<string>("type")));
		foreach (JValue value in data.Descendants().OfType<JValue>()) {
			if (value.Type is JTokenType.Float or JTokenType.Integer && !double.IsFinite(value.Value<double>())) throw new InvalidDataException("Non-finite number: " + value.Path);
			if (value.Type == JTokenType.String && value.Value<string>().Length > 4096) throw new InvalidDataException("Control text too long");
		}
		void Range(string key, double min, double max) {
			double value = data.Value<double>(key);
			if (data[key].Type is not (JTokenType.Float or JTokenType.Integer) || value < min || value > max) throw new InvalidDataException("Invalid " + key);
		}
		Range("width", .005, 3); Range("height", .005, 3); Range("rotation", -3600, 3600);
		foreach (string key in new[] { "opacity", "borderOpacity", "textOpacity" }) Range(key, 0, 1);
		Range("strokeWidth", 0, 100); Range("cornerRadius", 0, 500);
		foreach (string color in new[] { "textColor", "bgColor", "strokeColor", "activeColor", "dividerColor", "selectedColor" }) {
			if (data[color] != null && (data[color].Type != JTokenType.Integer || data.Value<long>(color) < int.MinValue || data.Value<long>(color) > uint.MaxValue)) throw new InvalidDataException("Invalid ARGB: " + color);
		}
		void OneOf(string field, params string[] options) { if (data[field] != null && !options.Contains(data.Value<string>(field))) throw new InvalidDataException("Invalid " + field); }
		if (!SupportsMode(data)) throw new InvalidDataException("Unsupported control mode: " + data.Value<string>("mode"));
		OneOf("shape", "RECTANGLE", "CIRCLE", "POLYGON");
		foreach (string key in new[] { "isVisible", "isPassThrough", "isSizeRatioLocked" }) if (data[key].Type != JTokenType.Boolean) throw new InvalidDataException("Invalid " + key);
		if (data.Value<string>("type") == "radialmenu") {
			Range("sectorCount", 4, 12); Range("expandedScale", 1, 8); Range("deadZoneRatio", 0, 1); Range("expandDuration", 0, 3000);
			if (data["sectorCount"].Type != JTokenType.Integer) throw new InvalidDataException("Sector count must be an integer");
			if (data["sectors"] is not JArray sectors || sectors.Count > 12 || sectors.Any(s => s is not JObject)) throw new InvalidDataException("Invalid sectors");
			foreach (JObject sector in sectors) Types(sector, new JObject { ["keycode"] = "", ["label"] = "" });
		}
		if (data.Value<string>("type") == "joystick") {
			Range("stickKnobSize", .05, 1); Range("stickOpacity", 0, 1);
			if (data["joystickKeys"] is not JArray keys || keys.Any(k => k.Type != JTokenType.String)
				|| data.Value<string>("mode") == "KEYBOARD" && keys.Count != 4) throw new InvalidDataException("Expected four joystick keys");
		}
		if (data["polygonPoints"] is JArray points && (points.Count > 64 || points.Any(p => p is not JObject || p.Value<double?>("x") is not >= 0 or > 1 || p.Value<double?>("y") is not >= 0 or > 1))) throw new InvalidDataException("Invalid polygon");
	}
}
