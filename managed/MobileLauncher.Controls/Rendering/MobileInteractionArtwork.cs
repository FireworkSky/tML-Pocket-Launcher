using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace MobileJoystick;

// Presentation uses the same aim and reach rules as the native interaction pass.
internal static class MobileInteractionArtwork
{
	internal static NPC TargetNpc()
	{
		int index = Main.LocalPlayer.talkNPC;
		if (index < 0 && MobileInteractionTarget.HasAim)
			return MobileInteractionTarget.NpcAt(MobileWorldInteraction.ToWorld(MobileJoystickInput.TouchAimPosition));
		if (index < 0 && !Main.playerInventory) return MobileInteractionTarget.NativeNpcTarget();
		return index >= 0 && index < Main.maxNPCs && Main.npc[index] is { active: true } npc ? npc : null;
	}

	internal static bool DrawTarget(SpriteBatch batch, Vector2 center, float size, Color color)
	{
		if (TargetNpc() is NPC npc) return DrawNpc(batch, npc, center, size, color);
		Item item = TargetItem();
		if (item == null) return false;
		MobileJoystickSystem.DrawItemIcon(batch, item, center, size, color);
		return true;
	}

	internal static bool HasTarget => TargetNpc() != null || TargetItem() != null;

	internal static Item TargetItem()
	{
		if (Main.playerInventory) return null;
		int type = MobileInteractionTarget.HasAim ? MobileInteractionTarget.ItemAtAim()
			: Main.LocalPlayer.cursorItemIconEnabled ? Main.LocalPlayer.cursorItemIconID : 0;
		if (!MobileInteractionTarget.HasAim && type <= 0 && Main.HasSmartInteractTarget && WorldGen.InWorld(Main.SmartInteractX, Main.SmartInteractY)) {
			Tile tile = Main.tile[Main.SmartInteractX, Main.SmartInteractY];
			if (tile.HasTile) type = tile.TileType switch {
				TileID.Switches => ItemID.Switch, TileID.Lever => ItemID.Lever,
				_ => 0
			};
		}
		return ContentSamples.ItemsByType.TryGetValue(type, out Item item) && !item.IsAir ? item : null;
	}

	internal static bool DrawNpc(SpriteBatch batch, NPC npc, Vector2 center, float size, Color color)
	{
		// Packs can supply a larger portrait; modded NPCs retain their registered head as fallback.
		if (DefaultControlTextures.TryDraw(batch, "NpcPortrait" + npc.type, center, size, color)) return true;
		int head = TownNPCProfiles.GetHeadIndexSafe(npc);
		if (head <= 0 || head >= TextureAssets.NpcHead.Length) return false;
		Texture2D texture = TextureAssets.NpcHead[head].Value;
		batch.Draw(texture, center, null, color, 0f, texture.Size() * .5f,
			size / System.Math.Max(texture.Width, texture.Height), SpriteEffects.None, 0f);
		return true;
	}
}
