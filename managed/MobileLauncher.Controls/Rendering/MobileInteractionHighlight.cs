using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace MobileJoystick;

// Reuse the current NPC frame and profile so movement and animation keep their outline.
public sealed class MobileInteractionHighlight
{
	private static readonly Dictionary<Texture2D, Texture2D> silhouettes = new();
	internal static bool IsTarget(NPC npc) => MobileJoystickConfig.Instance?.EnableMobileControls == true
		&& !Main.gameMenu && !Main.playerInventory && !Main.mapFullscreen
		&& (MobileInteractionTarget.HasAim || MobileNpcDialogSystem.IsTalking)
		&& ReferenceEquals(MobileInteractionArtwork.TargetNpc(), npc);

	public static bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
	{
		if (IsTarget(npc)) DrawOutline(spriteBatch, npc, screenPos);
		return true;
	}

	internal static void DrawOutline(SpriteBatch batch, NPC npc, Vector2 screenPos)
	{
		Texture2D source = TextureAssets.Npc[npc.type].Value;
		Rectangle frame = npc.frame;
		if (frame.Width <= 0 || frame.Height <= 0 || !source.Bounds.Contains(frame)
			|| (long)source.Width * source.Height > 4_000_000) return;
		if (!silhouettes.TryGetValue(source, out Texture2D mask)) {
			if (silhouettes.Count >= 8) Clear();
			Color[] pixels = new Color[source.Width * source.Height];
			source.GetData(pixels);
			for (int i = 0; i < pixels.Length; i++) {
				byte alpha = pixels[i].A;
				pixels[i] = new Color(alpha, alpha, alpha, alpha);
			}
			mask = new Texture2D(source.GraphicsDevice, source.Width, source.Height);
			mask.SetData(pixels);
			silhouettes[source] = mask;
		}
		Vector2 origin = frame.Size() * .5f;
		float extraY = npc.type is 108 or 124 or 625 ? 2f : 0f;
		Vector2 position = npc.Bottom - screenPos + new Vector2(0,
			-frame.Height * npc.scale * .5f + 4f + Main.NPCAddHeight(npc) + extraY + npc.gfxOffY);
		SpriteEffects flip = npc.spriteDirection == 1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
		Color yellow = new Color(255, 235, 60) * ((255 - npc.alpha) / 255f);
		// One world pixel around the alpha silhouette, behind the native NPC draw.
		for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) {
			if (x == 0 && y == 0) continue;
			batch.Draw(mask, position + new Vector2(x, y) * npc.scale, frame, yellow,
				npc.rotation, origin, npc.scale, flip, 0);
		}
	}

	internal static void Clear()
	{
		if (!Program.IsMainThread) {
			Main.QueueMainThreadAction(Clear);
			return;
		}
		foreach (Texture2D texture in silhouettes.Values) texture.Dispose();
		silhouettes.Clear();
	}
}
