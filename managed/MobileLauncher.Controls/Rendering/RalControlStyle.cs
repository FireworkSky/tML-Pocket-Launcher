using System;
using Microsoft.Xna.Framework;
using Newtonsoft.Json.Linq;

namespace MobileJoystick;

// Mirrors RAL's Canvas paints. Android Paint.alpha replaces the ARGB alpha;
// SpriteBatch instead needs premultiplied colors before linear filtering.
internal static class RalControlStyle
{
	internal static readonly Color Knob = new(125, 125, 125);
	internal static readonly Color PressedKnob = new(98, 0, 238);
	internal static Color Paint(Color color, float opacity)
		=> new Color(color.R, color.G, color.B, (byte)255) * MathHelper.Clamp(opacity, 0, 1);
	internal static Color Over(Color foreground, Color background)
	{
		float remaining = 1 - foreground.A / 255f;
		return new Color((byte)Math.Min(255, foreground.R + background.R * remaining),
			(byte)Math.Min(255, foreground.G + background.G * remaining),
			(byte)Math.Min(255, foreground.B + background.B * remaining),
			(byte)Math.Min(255, foreground.A + background.A * remaining));
	}
	internal static float Coverage(float distance, float footprint)
		=> MathHelper.Clamp(.5f - distance / Math.Max(.001f, footprint), 0, 1);
	internal static bool Dark(Color color)
	{
		float Linear(byte component) { float c = component / 255f; return c <= .04045f ? c / 12.92f : MathF.Pow((c + .055f) / 1.055f, 2.4f); }
		return .2126f * Linear(color.R) + .7152f * Linear(color.G) + .0722f * Linear(color.B) < .5f;
	}
	internal static float Distance(JObject data, Vector2 uv, Vector2 dimensions)
	{
		Vector2 p = (uv - new Vector2(.5f)) * dimensions;
		float radius = Math.Min(dimensions.X, dimensions.Y) / 2;
		string type = data.Value<string>("type"), shape = data.Value<string>("shape");
		if (type is "joystick" or "radialmenu" || shape == "CIRCLE") return p.Length() - radius;
		if (shape == "POLYGON" && data["polygonPoints"] is JArray points && points.Count >= 3) {
			float distance = float.MaxValue; bool inside = false;
			for (int i = 0, j = points.Count - 1; i < points.Count; j = i++) {
				Vector2 a = new(points[i].Value<float>("x"), points[i].Value<float>("y"));
				Vector2 b = new(points[j].Value<float>("x"), points[j].Value<float>("y"));
				if ((a.Y > uv.Y) != (b.Y > uv.Y) && uv.X < (b.X - a.X) * (uv.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
				a = (a - new Vector2(.5f)) * dimensions; b = (b - new Vector2(.5f)) * dimensions;
				Vector2 edge = b - a;
				float t = edge.LengthSquared() == 0 ? 0 : MathHelper.Clamp(Vector2.Dot(p - a, edge) / edge.LengthSquared(), 0, 1);
				distance = Math.Min(distance, Vector2.Distance(p, a + edge * t));
			}
			return inside ? -distance : distance;
		}
		return Box(p, dimensions / 2, Math.Min(data.Value<float?>("cornerRadius") ?? 8, radius));
	}
	private static float Box(Vector2 p, Vector2 half, float radius)
	{
		Vector2 q = new Vector2(Math.Abs(p.X), Math.Abs(p.Y)) - half + new Vector2(radius);
		return Vector2.Max(q, Vector2.Zero).Length() + Math.Min(Math.Max(q.X, q.Y), 0) - radius;
	}
	internal static Color Sample(JObject data, Vector2 uv, Vector2 dimensions, float footprint, bool pressed = false)
	{
		float distance = Distance(data, uv, dimensions);
		float coverage = Coverage(distance, footprint);
		if (coverage == 0) return Color.Transparent;
		Color background = LauncherControlArtwork.ColorValue(data, "bgColor", 0x4D000000);
		Color stroke = LauncherControlArtwork.ColorValue(data, "strokeColor", 0x66FFFFFF);
		bool userStroke = stroke.A > 0, joystick = data.Value<string>("type") == "joystick";
		float opacity = data.Value<float?>("opacity") ?? .5f;
		float borderOpacity = data.Value<float?>("borderOpacity") ?? 1;
		float multiplier = pressed ? 1.2f : 1;
		float width = data.Value<float?>("strokeWidth") ?? 1;
		if (!userStroke) {
			stroke = Dark(background) ? Color.White : Color.Black;
			width = joystick ? 1.5f : 1;
			borderOpacity = joystick ? opacity * .3f : borderOpacity * .6f;
		}
		if (joystick) { opacity *= .8f; multiplier = 1; }
		Color fill = Paint(background, opacity * multiplier) * coverage;
		float border = width <= 0 ? 0 : Math.Min(coverage, Coverage(-distance - width, footprint));
		return Over(Paint(stroke, borderOpacity * multiplier) * border, fill);
	}
}
