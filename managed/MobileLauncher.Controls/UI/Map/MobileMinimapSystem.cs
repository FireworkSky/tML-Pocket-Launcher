using System;
using Microsoft.Xna.Framework;
using MonoMod.Cil;
using Terraria;
using Terraria.GameContent.UI.Minimap;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace MobileJoystick;

// Keep vanilla minimap artwork and map layers while adapting its mobile viewport.
public partial class MobileMinimapSystem : MobileSystem
{
	private bool viewportHookApplied;

	private bool UseMobileMap => viewportHookApplied && MobileJoystickInput.EnabledThisFrame
		&& MobileJoystickConfig.Instance.UseMobileMinimap && !Main.mapFullscreen && Main.mapStyle == 1;

	public override void Load()
	{
		if (Main.dedServ) {
			return;
		}

		IL_Main.DrawMap += ModifyMinimapViewport;
		On_Main.DrawMap += DrawMap;
		On_Main.DrawMapFullscreenBackground += DrawFullscreenBackground;
		On_MinimapFrame.Update += UpdateFrame;
	}

	public override void Unload()
	{
		if (!Main.dedServ) {
			IL_Main.DrawMap -= ModifyMinimapViewport;
			On_Main.DrawMap -= DrawMap;
			On_Main.DrawMapFullscreenBackground -= DrawFullscreenBackground;
			On_MinimapFrame.Update -= UpdateFrame;
		}
		viewportHookApplied = false;
	}

	private void DrawMap(On_Main.orig_DrawMap orig, Main self, GameTime time)
	{
		// IngameOptions also calls DrawMap directly, bypassing interface layers.
		if (MobileMenuSystem.IsPauseMenuOpen || MobileMenuSystem.EditingCustomControls || MobileChatSystem.IsOpen) return;
		if (!UseMobileFullscreen) { orig(self, time); return; }
		DrawFullscreenMapInput(orig, self, time);
		if (Main.mapFullscreen) DrawFullscreenControls();
	}

	private static void DrawFullscreenMapInput(On_Main.orig_DrawMap orig, Main self, GameTime time)
	{
		bool left = Main.mouseLeft, leftRelease = Main.mouseLeftRelease;
		bool right = Main.mouseRight, rightRelease = Main.mouseRightRelease;
		int x = Main.mouseX, y = Main.mouseY, rawX = 0, rawY = 0;
		Vector2? press = MobileJoystickInput.ConsumeMapPress(out bool rightClick);
		bool inject = press.HasValue && Main.hasFocus;
		try {
			Main.mouseLeft = Main.mouseLeftRelease = inject && !rightClick;
			if (inject) {
				PlayerInput.SetZoom_Unscaled();
				rawX = Main.mouseX; rawY = Main.mouseY;
				Main.mouseX = (int)press.Value.X; Main.mouseY = (int)press.Value.Y;
				PlayerInput.CacheMousePositionForZoom();
				if (rightClick) Main.mouseRight = Main.mouseRightRelease = true;
			}
			orig(self, time);
		}
		finally {
			if (inject) {
				Main.mouseX = rawX; Main.mouseY = rawY;
				PlayerInput.CacheMousePositionForZoom();
			}
			if (inject && rightClick) { Main.mouseRight = right; Main.mouseRightRelease = rightRelease; }
			Main.mouseLeft = left; Main.mouseLeftRelease = leftRelease;
			Main.mouseX = x; Main.mouseY = y;
		}
	}

	private void ModifyMinimapViewport(ILContext il)
	{
		RemoveDesktopMapClose(il);
		ILCursor cursor = new(il);
		// The second assignment follows vanilla's map-scale conversion. Override all
		// four viewport fields here, before any tile ranges or icon bounds are read.
		if (!cursor.TryGotoNext(MoveType.After, instruction => instruction.MatchStsfld<Main>(nameof(Main.miniMapY)))
			|| !cursor.TryGotoNext(MoveType.After, instruction => instruction.MatchStsfld<Main>(nameof(Main.miniMapY)))) {
			MobileControlsSession.Logger.Warn("Mobile minimap viewport hook unavailable; retaining the vanilla minimap.");
			return;
		}
		cursor.EmitDelegate<Action>(ApplyViewport);
		viewportHookApplied = true;
	}

	private void ApplyViewport()
	{
		if (!UseMobileMap) {
			return;
		}
		Rectangle bounds = MobileJoystickLayout.GetMinimapBounds(MobileJoystickConfig.Instance,
			PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight);
		float drawScale = Main.UIScale * Main.MapScale;
		float mapSize = 240f * drawScale;
		Main.miniMapX = (int)((bounds.Center.X - mapSize * .5f) / drawScale);
		Main.miniMapY = (int)((bounds.Center.Y - mapSize * .5f) / drawScale);
	}

	private void UpdateFrame(On_MinimapFrame.orig_Update orig, MinimapFrame self)
	{
		// The mobile minimap uses the HUD toolbar and touch gestures.
		if (!UseMobileMap) {
			orig(self);
		}
	}

}
