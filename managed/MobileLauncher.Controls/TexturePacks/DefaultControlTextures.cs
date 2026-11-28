using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;

namespace MobileJoystick;

internal static class DefaultControlTextures
{
	private const string BuiltInManifest = "Assets/TexturePacks/AndroidMobileControls.json";
	private static readonly Dictionary<string, Texture2D> builtInTextures = new(StringComparer.Ordinal);
	private static readonly HashSet<string> warnedMissingDefaults = new(StringComparer.Ordinal);
	private static ControlTexturePackManifest builtInManifest;

	internal static bool TryDrawDefault(SpriteBatch batch, string name, Vector2 center, float size, Color color)
	{
		if (TryDraw(batch, name, center, size, color)) return true;
		if (!string.IsNullOrWhiteSpace(name) && warnedMissingDefaults.Add(name))
			MobileControlsSession.Logger.Warn($"Default touch-control texture '{name}' is unavailable; skipping its artwork.");
		return false;
	}

	internal static void Load()
	{
		Unload();
		if (!UiTextureAssets.Available) return;
		// This manifest ships with the patch. Resolve each file independently so
		// a partial extraction still supplies all of its usable textures.
		builtInManifest = ControlTexturePackManifest.Parse(UiTextureAssets.ReadText(BuiltInManifest),
			Path.GetFullPath("."), _ => true);
		foreach (ControlTextureEntry entry in builtInManifest.Textures.Values) {
			string name = Path.ChangeExtension(entry.File, null);
			if (UiTextureAssets.HasAsset(name))
				builtInTextures[entry.Id] = UiTextureAssets.Request(name).Value;
		}
	}

	internal static void Unload()
	{
		if (!Program.IsMainThread) {
			Main.RunOnMainThread(UnloadOnMainThread).GetAwaiter().GetResult();
			return;
		}

		UnloadOnMainThread();
	}

	private static void UnloadOnMainThread()
	{
		LauncherControlArtwork.Unload();
		CustomControlTextures.Unload();
		// UiTextureAssets owns the shared textures.
		builtInTextures.Clear();
		builtInManifest = null;
		warnedMissingDefaults.Clear();
	}

	internal static ControlTextureEntry FindTexture(string name)
	{
		if (builtInManifest == null || string.IsNullOrWhiteSpace(name)) return null;
		string id = builtInManifest.Textures.ContainsKey(name) ? name
			: builtInManifest.Mappings.TryGetValue(name, out string mapped) ? mapped : null;
		return id != null && builtInManifest.Textures.TryGetValue(id, out ControlTextureEntry entry) ? entry : null;
	}

	internal static bool TryDraw(SpriteBatch batch, string name, Vector2 center, float size, Color color)
	{
		if (builtInManifest == null || string.IsNullOrWhiteSpace(name)) return false;
		string id = builtInManifest.Textures.ContainsKey(name) ? name
			: builtInManifest.Mappings.TryGetValue(name, out string mapped) ? mapped : null;
		if (id == null || !builtInManifest.Textures.TryGetValue(id, out ControlTextureEntry entry)) return false;
		Texture2D texture;
		try {
			if (!builtInTextures.TryGetValue(id, out texture)) return false;
			Rectangle source = texture.Bounds;
			if (entry.Source != null) {
				int[] crop = entry.Source;
				if ((long)crop[0] + crop[2] > texture.Width || (long)crop[1] + crop[3] > texture.Height) return false;
				source = new Rectangle(crop[0], crop[1], crop[2], crop[3]);
			}
			Vector2 position = center + new Vector2(entry.OffsetX, entry.OffsetY) * size;
			float drawScale = size * entry.Scale / Math.Max(source.Width, source.Height);
			batch.Draw(texture, position, source, color, 0f, source.Size() * .5f, drawScale, SpriteEffects.None, 0f);
			return true;
		}
		catch { return false; }
	}

	internal static bool TryDrawFrame(SpriteBatch batch, string name, Rectangle bounds, int corner,
		float scale, Color color, bool frameOnly = false)
	{
		ControlTextureEntry entry = FindTexture(name);
		Texture2D texture = GetTexture(name);
		if (entry == null || texture == null) return false;
		Rectangle source = entry.Source is int[] crop ? new Rectangle(crop[0], crop[1], crop[2], crop[3]) : texture.Bounds;
		if (!texture.Bounds.Contains(source)) return false;
		Vector2 center = bounds.Center.ToVector2() + new Vector2(entry.OffsetX * bounds.Width, entry.OffsetY * bounds.Height);
		int width = Math.Max(1, (int)(bounds.Width * entry.Scale)), height = Math.Max(1, (int)(bounds.Height * entry.Scale));
		bounds = new Rectangle((int)(center.X - width / 2f), (int)(center.Y - height / 2f), width, height);
		int cx = Math.Min(corner, source.Width / 2), cy = Math.Min(corner, source.Height / 2);
		int ex = Math.Min(Math.Max(1, (int)(cx * scale)), bounds.Width / 2);
		int ey = Math.Min(Math.Max(1, (int)(cy * scale)), bounds.Height / 2);
		int[] sx = { source.Left, source.Left + cx, source.Right - cx, source.Right };
		int[] sy = { source.Top, source.Top + cy, source.Bottom - cy, source.Bottom };
		int[] dx = { bounds.Left, bounds.Left + ex, bounds.Right - ex, bounds.Right };
		int[] dy = { bounds.Top, bounds.Top + ey, bounds.Bottom - ey, bounds.Bottom };
		for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) {
			if (frameOnly && x == 1 && y == 1) continue;
			if (dx[x + 1] <= dx[x] || dy[y + 1] <= dy[y] || sx[x + 1] <= sx[x] || sy[y + 1] <= sy[y]) continue;
			batch.Draw(texture, new Rectangle(dx[x], dy[y], dx[x + 1] - dx[x], dy[y + 1] - dy[y]),
				new Rectangle(sx[x], sy[y], sx[x + 1] - sx[x], sy[y + 1] - sy[y]), color);
		}
		return true;
	}

	internal static Texture2D GetTexture(string name)
	{
		if (builtInManifest == null) return null;
		string id = builtInManifest.Textures.ContainsKey(name) ? name
			: builtInManifest.Mappings.TryGetValue(name, out string mapped) ? mapped : null;
		if (id == null) return null;
		return builtInTextures.TryGetValue(id, out Texture2D texture) ? texture : null;
	}
}
