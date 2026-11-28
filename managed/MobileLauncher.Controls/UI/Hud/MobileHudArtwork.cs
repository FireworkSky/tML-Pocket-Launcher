using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.ModLoader;

namespace MobileJoystick;

internal static class MobileHudArtwork
{
	internal static Texture2D Texture(string name) => UiTextureAssets.Request(
		"MobileJoystick/Assets/MobileHud/" + name, AssetRequestMode.ImmediateLoad).Value;

	internal static bool DrawToolbar(MobileControlButton button, bool pressed = false)
	{
		string name = button.Action switch {
			MobileControlAction.Chat => MobileChatSystem.IsOpen ? "ChatSelected" : "Chat",
			MobileControlAction.MapStyle => Main.mapStyle == 1 && !Main.mapFullscreen ? "MinimapSelected" : "Minimap",
			MobileControlAction.Map => Main.mapFullscreen ? "MapSelected" : "Map",
			_ => null
		};
		if (name == null) return false;
		if (button.Id != null && !MobileControlArtwork.UsesDefaultTexture(button.TextureName,
			MobileJoystickLayout.GetDefaultTexture(button.Action))) return false;
		Rectangle bounds = button.Bounds;
		if (DefaultControlTextures.TryDrawFrame(Main.spriteBatch, "HudToolbar", bounds, 10, bounds.Width / 100f, Color.White)) {
			if (!DefaultControlTextures.TryDraw(Main.spriteBatch, "Hud" + name, button.Center,
				Math.Min(bounds.Width, bounds.Height) * .72f, pressed ? Color.White : Color.White * .95f))
				MobileControlArtwork.DrawActionIcon(Main.spriteBatch, button.Action, button.Center, button.Radius * 1.35f, Color.White);
			return true;
		}
		bool selected = name.EndsWith("Selected", StringComparison.Ordinal);
		NativeControlTheme.Surface(Main.spriteBatch, button.Center, button.Radius, Color.White, pressed || selected);
		MobileControlArtwork.DrawActionIcon(Main.spriteBatch, button.Action, button.Center,
			button.Radius * 1.35f, selected ? new Color(244,218,106) : Color.White * .95f);
		return true;
	}

	internal static void Icon(string name, Rectangle bounds, Color color, float fraction = .8f)
	{
		Texture2D texture = Texture(name);
		Main.spriteBatch.Draw(texture, bounds.Center.ToVector2(), null, color, 0f, texture.Size() * .5f,
			Math.Min(bounds.Width, bounds.Height) * fraction / Math.Max(texture.Width, texture.Height), SpriteEffects.None, 0f);
	}

	internal static void Nine(string name, Rectangle bounds, Color color, float scale)
	{
		Texture2D texture = Texture(name);
		// Corners follow the UI scale, never the stretched panel dimensions.
		MobileMenuFrame.Draw(Main.spriteBatch, texture, bounds, color, scale, 10);
	}
}
