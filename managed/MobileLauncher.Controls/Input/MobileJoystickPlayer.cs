using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;

namespace MobileJoystick;

public class MobileJoystickPlayer
{
	public static void SetControls(Player player)
	{
		if (player.whoAmI == Main.myPlayer && MobileJoystickConfig.Instance?.EnableMobileControls == true
			&& (Main.mapFullscreen || player.talkNPC >= 0
				|| Main.ingameOptionsWindow || Main.inFancyUI || Main.playerInventory && !MobileInventorySystem.Active)) {
			player.controlUseItem = player.controlUseTile = player.controlHook = player.controlMount = false;
			player.controlLeft = player.controlRight = player.controlUp = player.controlDown = player.controlJump = false;
			return;
		}
		if (player.whoAmI != Main.myPlayer || !MobileJoystickInput.EnabledThisFrame) {
			return;
		}

		MobileJoystickConfig config = MobileJoystickConfig.Instance;
		// Vanilla processes these before ModPlayer.SetControls and skips that
		// phase while chat is writing. Dispatch only touch press edges here.
		if (MobileChatSystem.IsOpen && Main.drawingPlayerChat) {
			if (MobileJoystickInput.WasActionPressed(MobileControlAction.Heal)) player.QuickHeal();
			if (MobileJoystickInput.WasActionPressed(MobileControlAction.Mana)) player.QuickMana();
			if (MobileJoystickInput.WasActionPressed(MobileControlAction.Mount)
				|| MobileJoystickInput.WasActionPressed(MobileControlAction.QuickMount)) player.QuickMount();
		}
		Vector2 move = MobileJoystickInput.JoystickVector;
		if (move.X < -0.25f) {
			player.controlLeft = true;
		}

		if (move.X > 0.25f) {
			player.controlRight = true;
		}

		if (move.Y > 0.45f) {
			player.controlDown = true;
		}

		if (move.Y < -0.45f) {
			player.controlUp = true;
		}

		// Native FindPulley refuses to grab a rope while jumping.
		Point rope = new Vector2(player.Center.X, player.position.Y - 8f).ToTileCoordinates();
		bool climbingRope = player.pulley || WorldGen.InWorld(rope.X, rope.Y, 2) && WorldGen.IsRope(rope.X, rope.Y);
		if (config != null && config.JoystickUpJumps && move.Y < -0.7f && !climbingRope) {
			player.controlJump = true;
		}

		if (MobileJoystickInput.IsActionDown(MobileControlAction.Jump)) {
			// A separate button press must work even while the stick still holds Up.
			if (MobileJoystickInput.WasActionPressed(MobileControlAction.Jump)) player.releaseJump = true;
			player.controlJump = true;
		}
		if (MobileJoystickInput.IsActionDown(MobileControlAction.SwapUseInteract)) {
			bool hasInteractionTarget = Main.SmartInteractShowingGenuine || Main.SmartInteractShowingFake || Main.HoveringOverAnNPC;
			if (hasInteractionTarget) player.controlUseTile = true;
			else {
				ApplyTouchAim();
				player.controlUseItem = true;
			}
		}

		// Vanilla sees the fallback mouse press too. Preserve independent keyboard use.
		if ((MobileJoystickInput.HasAnyControlTouch || MobileJoystickInput.HasExternalUiTouch) && !KeyboardUseItemHeld()) {
			player.controlUseItem = false;
		}
		if (Main.playerInventory) player.controlUseItem = player.controlUseTile = false;

		bool canUseWorldItem = Main.mouseItem.IsAir && !Main.playerInventory && !MobileInventorySystem.BlocksWorldUse;
		if (MobileInventorySystem.ConsumeSelectedUse() && Main.mouseItem.IsAir) {
			// This is an explicit UI action; vanilla's previous UI hover must not
			// discard its one-update press through delayUseItem.
			player.delayUseItem = false;
			player.releaseUseItem = true;
			ApplyTouchAim();
			player.controlUseItem = true;
			player.mouseInterface = false;
			Main.blockMouse = false;
		}
		if (MobileJoystickInput.AttackHeld && canUseWorldItem) {
			ApplyTouchAim();
		}

		if (canUseWorldItem && ((MobileJoystickInput.AttackHeld && MobileJoystickInput.AimAndUseMode)
			|| MobileJoystickInput.IsActionDown(MobileControlAction.UseItem))) {
			// A movement touch can leave vanilla's UI delay latched. An explicit
			// tool-use contact must clear it regardless of which stick was first.
			player.delayUseItem = false;
			if (MobileJoystickInput.AttackJustPressed || MobileJoystickInput.WasActionPressed(MobileControlAction.UseItem)
				|| MobileJoystickInput.WasActionPressed(MobileControlAction.AimModeToggle)) player.releaseUseItem = true;
			ApplyTouchAim();
			player.controlUseItem = true;
			player.mouseInterface = false;
			Main.blockMouse = false;
		}

		if (MobileJoystickInput.IsActionDown(MobileControlAction.Grapple)) {
			player.releaseHook = true;
			player.controlHook = true;
		}
		if (MobileJoystickInput.IsActionDown(MobileControlAction.SmartInteract)) player.controlUseTile = true;

		// QuickMount is consumed before SetControls; injected in PostUpdateInput.
		if (MobileJoystickInput.IsActionDown(MobileControlAction.AutoSelect)) PlayerInput.Triggers.Current.SmartSelect = true;

		// The mobile toggle owns both states, including the off transition.
		Main.SmartCursorWanted_Mouse = MobileJoystickInput.SmartCursorEnabled && !Main.playerInventory;
		player.controlSmart = Main.SmartCursorWanted_Mouse;

		if (MobileJoystickInput.WasActionPressed(MobileControlAction.Buff)) {
			player.QuickBuff();
		}
		MobileWorldInteraction.SetControls(player);
		if (MobileCalamityUiSystem.Dragging && !MobileJoystickInput.AttackHeld && !MobileJoystickInput.IsActionDown(MobileControlAction.UseItem))
			player.controlUseItem = player.controlUseTile = false;
	}

	private static bool KeyboardUseItemHeld()
	{
		var bindings = PlayerInput.CurrentProfile.InputModes[InputMode.Keyboard].KeyStatus["MouseLeft"];
		foreach (string binding in bindings) {
			if (System.Enum.TryParse(binding, out Keys key) && Main.keyState.IsKeyDown(key)) return true;
		}
		return false;
	}

	internal static void ApplyTouchAim() => MobileWorldInteraction.SetPointer(GetTouchAimPointer());

	internal static Vector2 GetTouchAimPointer()
	{
		Player player = Main.LocalPlayer;
		MobileJoystickConfig config = MobileJoystickConfig.Instance;
		float aimDistance = config?.AimDistance ?? 440f;
		Vector2 playerScreenCenter = MobileWorldInteraction.ToScreen(player.MountedCenter);
		Vector2 cursor;
		if (MobileTrackpad.Enabled && !MobileJoystickInput.AttackHeld && !MobileJoystickInput.HasExplicitTouchAim) {
			cursor = MobileTrackpad.ScreenPosition;
		}
		else if (MobileJoystickInput.HasTouchAimPosition) {
			cursor = MobileJoystickInput.TouchAimPosition;
		}
		else {
			Vector2 aim = new(player.direction == 0 ? 1f : player.direction, 0f);
			cursor = playerScreenCenter + aim * aimDistance * Main.GameViewMatrix.Zoom;
		}

		return ProjectSmartBuildingAim(cursor);
	}

	internal static Vector2 ProjectSmartBuildingAim(Vector2 screen)
	{
		Player player = Main.LocalPlayer;
		Item item = player.HeldItem;
		// Native smart block placement only searches when its input is inside
		// the reachable rectangle. Preserve the stick's direction at its edge.
		if (!MobileJoystickInput.SmartCursorEnabled || !Player.SmartCursorSettings.SmartBlocksEnabled
			|| item.createTile <= -1 || !Main.tileSolid[item.createTile] || Main.tileSolidTop[item.createTile]
			|| Main.tileFrameImportant[item.createTile] || ItemID.Sets.GrassSeeds[item.type]
			|| item.type is ItemID.StaffofRegrowth or ItemID.AcornAxe) return screen;
		Vector2 world = MobileWorldInteraction.ToWorld(screen);
		Vector2 origin = player.Center;
		Vector2 delta = world - origin;
		float left = ((int)(player.position.X / 16f) - Player.tileRangeX - item.tileBoost + 1) * 16f;
		float right = ((int)(player.Right.X / 16f) + Player.tileRangeX + item.tileBoost) * 16f - 1f;
		float top = ((int)(player.position.Y / 16f) - Player.tileRangeY - item.tileBoost + 1) * 16f;
		float bottom = ((int)(player.Bottom.Y / 16f) + Player.tileRangeY + item.tileBoost - 1) * 16f - 1f;
		float fraction = 1f;
		if (delta.X > 0) fraction = System.Math.Min(fraction, (right - origin.X) / delta.X);
		else if (delta.X < 0) fraction = System.Math.Min(fraction, (left - origin.X) / delta.X);
		if (delta.Y > 0) fraction = System.Math.Min(fraction, (bottom - origin.Y) / delta.Y);
		else if (delta.Y < 0) fraction = System.Math.Min(fraction, (top - origin.Y) / delta.Y);
		return MobileWorldInteraction.ToScreen(origin + delta * MathHelper.Clamp(fraction, 0f, 1f));
	}
}
