using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;
using Terraria;
using Terraria.GameContent;

namespace MobileJoystick;

internal static class LauncherControlArtwork
{
	private sealed record Surface(Texture2D Texture, int PixelCount) { internal long LastUsed; }
	private static readonly Dictionary<string, Surface> surfaces = new(StringComparer.Ordinal);
	private static long useCounter;
	private static int cachedPixels;
	internal static void Unload() { foreach (var surface in surfaces.Values) surface.Texture.Dispose(); surfaces.Clear(); cachedPixels = 0; }
	internal static Color ColorValue(JObject data, string key, uint fallback)
	{
		uint value = unchecked((uint)(data.Value<long?>(key) ?? fallback));
		return new Color((byte)(value >> 16), (byte)(value >> 8), (byte)value, (byte)(value >> 24));
	}
	private static Surface Cached(string key, int width, int height, Func<int, int, Color> pixel)
	{
		if (surfaces.TryGetValue(key, out Surface surface)) { surface.LastUsed = ++useCounter; return surface; }
		// Editor drags change dimensions repeatedly. Bound GPU memory for generated masks.
		while (surfaces.Count > 0 && (surfaces.Count >= 256 || cachedPixels + width * height > 16_000_000)) {
			var oldest = surfaces.MinBy(pair => pair.Value.LastUsed);
			cachedPixels -= oldest.Value.PixelCount; oldest.Value.Texture.Dispose(); surfaces.Remove(oldest.Key);
		}
		var pixels = new Color[width * height];
		for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) pixels[y * width + x] = pixel(x, y);
		var texture = new Texture2D(Main.instance.GraphicsDevice, width, height); texture.SetData(pixels);
		cachedPixels += pixels.Length;
		return surfaces[key] = new Surface(texture, pixels.Length) { LastUsed = ++useCounter };
	}
	private static Point RasterSize(Vector2 dimensions)
	{
		float ratio = Math.Min(1f, 512 / Math.Max(dimensions.X, dimensions.Y));
		return new Point(Math.Max(1, (int)(dimensions.X * ratio)), Math.Max(1, (int)(dimensions.Y * ratio)));
	}
	private static void Sprite(SpriteBatch batch, Texture2D texture, Vector2 center, Vector2 dimensions, float rotation, Color color)
		=> batch.Draw(texture, center, null, color, MathHelper.ToRadians(rotation), new Vector2(texture.Width, texture.Height) / 2,
			dimensions / new Vector2(texture.Width, texture.Height), SpriteEffects.None, 0);
	private static Surface SurfaceFor(MobileLauncherControl control, Vector2 dimensions, int sector = -1, int count = 8, bool pressed = false)
	{
		Point size = RasterSize(dimensions);
		string key = $"{control.RenderId}|{size}|{dimensions}|{sector}/{count}/{pressed}";
		float footprint = Math.Max(dimensions.X / size.X, dimensions.Y / size.Y);
		return Cached(key, size.X, size.Y, (x, y) => {
			Vector2 uv = new((x + .5f) / size.X, (y + .5f) / size.Y);
			if (sector < 0) return RalControlStyle.Sample(control.Data, uv, dimensions, footprint, pressed);
			if (LauncherControlBindings.Sector(uv - new Vector2(.5f), count) != sector) return Color.Transparent;
			return Color.White * RalControlStyle.Coverage(RalControlStyle.Distance(control.Data, uv, dimensions), footprint);
		});
	}
	internal static void Draw(SpriteBatch batch, MobileControlButton button, Color color, bool selected = false)
	{
		MobileLauncherControl data = button.Launcher;
		bool held = MobileJoystickInput.LauncherHeld(button.Id), toggled = MobileJoystickInput.LauncherToggled(button.Id);
		var gesture = MobileJoystickInput.LauncherState(button.Id);
		Vector2 dimensions = button.Dimensions;
		if (selected) color = Color.White;
		Vector2 center = button.Center;
		float rotation = data.Number("rotation");
		bool custom = CustomControlTextures.Draw(batch, button.CustomTexture, center, dimensions, held || toggled ? Color.White : color, rotation);
		if (!custom && data.Type != "joystick")
			Sprite(batch, SurfaceFor(data, dimensions, pressed: held || toggled).Texture, center, dimensions, rotation, color);
		if (!custom && data.String("nativeTexture") is { Length: > 0 } nativeTexture && data.Type != "joystick")
			NativeControlTheme.Legacy(batch, nativeTexture, center, Math.Min(dimensions.X, dimensions.Y), color);
		else if (!custom && data.Type == "radialmenu" && gesture == null && !selected)
			NativeControlTheme.Legacy(batch, "PhoneRadialGlyph", center, Math.Min(dimensions.X, dimensions.Y) * .7f, color);
		if (!custom && data.Type == "joystick") {
			float radius = Math.Min(dimensions.X, dimensions.Y) / 2;
			Vector2 displacement = gesture?.Offset ?? Vector2.Zero;
			if (displacement.LengthSquared() > .75f * .75f) displacement = Vector2.Normalize(displacement) * .75f;
			displacement = Vector2.Transform(displacement, Matrix.CreateRotationZ(MathHelper.ToRadians(rotation)));
			Color bg = ColorValue(data.Data, "bgColor", 0xFF2A2A2A), stroke = ColorValue(data.Data, "strokeColor", 0xFFAAAAAA);
			bool userStroke = stroke.A > 0;
			NativeControlTheme.RalJoystick(batch, center, radius, displacement, color, held, data.Number("stickKnobSize", .4f),
				bg, userStroke ? stroke : RalControlStyle.Dark(bg) ? Color.White : Color.Black,
				data.Number("opacity", .35f), userStroke ? data.Number("borderOpacity", .5f) : data.Number("opacity", .35f) * .3f,
				data.Number("stickOpacity", .7f), strokeWidth: userStroke ? data.Number("strokeWidth", 1.5f) : 1.5f);
		}
		if (data.Type == "radialmenu" && (gesture != null || selected)) {
			int count = (int)data.Number("sectorCount", 8);
			float progress = gesture == null || data.Number("expandDuration") <= 0 ? 1 : Math.Clamp((float)((System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency - gesture.Started) * 1000 / data.Number("expandDuration")), 0, 1);
			progress = progress * progress * (3 - 2 * progress);
			Vector2 expanded = dimensions * MathHelper.Lerp(1, data.Number("expandedScale", 2.5f), progress);
			Vector2 fullDimensions = dimensions * data.Number("expandedScale", 2.5f);
			Sprite(batch, SurfaceFor(data, dimensions).Texture, center, expanded, rotation, color);
			if (gesture?.Sector is >= 0) {
				Surface highlight = SurfaceFor(data, fullDimensions, sector: gesture.Sector, count: count);
				Sprite(batch, highlight.Texture, center, expanded, rotation, ColorValue(data.Data, "selectedColor", 0x99FFFFFF));
			}
			JArray sectors = (JArray)data.Data["sectors"];
			for (int i = 0; i < count; i++) {
				Vector2 direction = Vector2.Transform(Direction(i, count), Matrix.CreateRotationZ(MathHelper.ToRadians(rotation)));
				Vector2 position = center + direction * Math.Min(expanded.X, expanded.Y) * .36f;
				if (i < sectors.Count) Label(batch, sectors[i].Value<string>("label") ?? (i + 1).ToString(), position, dimensions * .7f, color);
				if (data.Flag("showDividers", true)) {
					float angle = (i + .5f) * MathHelper.TwoPi / count + MathHelper.ToRadians(rotation) - MathHelper.PiOver2;
					batch.Draw(TextureAssets.MagicPixel.Value, center, null, ColorValue(data.Data, "dividerColor", 0x66FFFFFF), angle, Vector2.Zero,
						new Vector2(Math.Min(expanded.X, expanded.Y) / 2, 1), SpriteEffects.None, 0);
				}
			}
		}
		string text = data.Type == "button" ? data.String("name") : "";
		if (!custom && !string.IsNullOrWhiteSpace(text))
			Label(batch, text, center, dimensions * .8f, RalControlStyle.Paint(ColorValue(data.Data, "textColor", 0xFFFFFFFF), data.Number("textOpacity", .9f)) * (color.A / 255f));
	}
	internal static Vector2 Direction(int slot, int count) => new(MathF.Sin(slot * MathHelper.TwoPi / count), -MathF.Cos(slot * MathHelper.TwoPi / count));
	internal static void Label(SpriteBatch batch, string text, Vector2 center, Vector2 dimensions, Color color)
	{
		Vector2 measure = FontAssets.MouseText.Value.MeasureString(text);
		float scale = Math.Min(.8f, Math.Min(dimensions.X / Math.Max(1, measure.X), dimensions.Y / Math.Max(1, measure.Y)));
		Utils.DrawBorderString(batch, text, center, color, scale, .5f, .5f);
	}
}
