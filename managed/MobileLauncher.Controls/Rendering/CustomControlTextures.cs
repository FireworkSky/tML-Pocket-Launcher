using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;

namespace MobileJoystick;

internal static class CustomControlTextures
{
	private const string Prefix = "data:image/png;base64,";
	private const int MaxBytes = 128 * 1024;
	private sealed class Entry
	{
		internal Asset<Texture2D> Asset;
		internal ulong LastUsed;
	}

	// Layouts retain the profile string, so drawing never hashes the PNG payload.
	private static readonly Dictionary<string, Entry> textures = new(ReferenceEqualityComparer.Instance);
	private static ulong lastCleanup;

	private static Asset<Texture2D> Load(string uri)
	{
		if (uri.Length > Prefix.Length + (MaxBytes + 2) / 3 * 4 || !uri.StartsWith(Prefix, StringComparison.Ordinal))
			return null;
		try {
			byte[] png = Convert.FromBase64String(uri[Prefix.Length..]);
			if (png.Length is < 33 or > MaxBytes
				|| BinaryPrimitives.ReadUInt64BigEndian(png) != 0x89504e470d0a1a0a
				|| BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(8)) != 13
				|| BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(12)) != 0x49484452
				|| BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16)) is < 1 or > 256
				|| BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20)) is < 1 or > 256)
				return null;
			using var stream = new MemoryStream(png, false);
			return Main.Assets.CreateUntracked<Texture2D>(stream, ".png", AssetRequestMode.ImmediateLoad);
		}
		catch (Exception error) {
			MobileControlsSession.Logger.Warn("Could not decode the custom control PNG", error);
			return null;
		}
	}

	internal static bool Draw(SpriteBatch batch, string uri, Vector2 center, Vector2 bounds, Color color, float rotation = 0)
	{
		if (string.IsNullOrEmpty(uri)) return false;
		ulong frame = Main.GameUpdateCount;
		if (frame - lastCleanup > 120) {
			foreach (var pair in textures.Where(pair => frame - pair.Value.LastUsed > 120).ToArray()) {
				pair.Value.Asset?.Dispose();
				textures.Remove(pair.Key);
			}
			lastCleanup = frame;
		}
		if (!textures.TryGetValue(uri, out Entry entry))
			textures.Add(uri, entry = new Entry { Asset = Load(uri) });
		entry.LastUsed = frame;
		Texture2D texture = entry.Asset?.Value;
		if (texture == null) return false;
		float scale = Math.Min(bounds.X / texture.Width, bounds.Y / texture.Height);
		batch.Draw(texture, center, null, color, MathHelper.ToRadians(rotation),
			new Vector2(texture.Width, texture.Height) * .5f, scale, SpriteEffects.None, 0);
		return true;
	}

	internal static void Unload()
	{
		foreach (Entry entry in textures.Values) entry.Asset?.Dispose();
		textures.Clear();
		lastCleanup = 0;
	}
}
