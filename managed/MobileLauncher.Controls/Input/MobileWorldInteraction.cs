using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input.Touch;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace MobileJoystick;

// Touch owns the gesture; Terraria owns the interaction and its networking.
internal static class MobileWorldInteraction
{
	private const int NoContact = int.MinValue;
	private static int contactId = NoContact;
	private static Vector2 target;
	private static bool tilePending, syntheticWasDown, rightDown, rightPressed;
	private static bool directToolPress, directToolStarted;
	private static double toolPressStart;
	internal static bool DirectUseHeld => OwnsWorldPress && directToolPress;
	internal static bool PrecisionHeld => DirectUseHeld && IsPrecisionItem(Main.LocalPlayer.HeldItem);
	internal static Vector2 PrecisionPosition => target;
	internal static bool ShowMagnifier => PrecisionHeld && MobilePrecisionMagnifier.IsLongPress(toolPressStart,
		System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency);
	internal static bool IsPrecisionItem(Item item) => item is { IsAir: false }
		&& (item.createTile > -1 || item.createWall > 0 || item.pick > 0 || item.axe > 0 || item.hammer > 0);
	private static bool rawLeft, rawRight, rawLeftPressed, rawLeftReleased;
	internal static bool OwnsWorldPress => contactId != NoContact;
	internal static bool UsesRightTrigger => rightDown;

	internal static void Reset()
	{
		MobileInteractionTarget.Reset();
		contactId = NoContact;
		directToolPress = directToolStarted = false;
		tilePending = rightDown = rightPressed = false;
		if (syntheticWasDown && !rawRight) {
			Main.mouseRight = false;
			Main.mouseRightRelease = true;
			PlayerInput.Triggers.Current.MouseRight = false;
			PlayerInput.Triggers.JustPressed.MouseRight = false;
		}
		syntheticWasDown = false;
	}

	internal static void UpdateInput()
	{
		rawLeft = PlayerInput.Triggers.Current.MouseLeft;
		rawRight = Main.hasFocus && PlayerInput.Triggers.Current.MouseRight;
		rawLeftPressed = PlayerInput.Triggers.JustPressed.MouseLeft;
		rawLeftReleased = PlayerInput.Triggers.JustReleased.MouseLeft;
		bool wasDown = syntheticWasDown;
		directToolStarted = false;
		rightDown = rightPressed = false;
		if (!MobileJoystickInput.EnabledThisFrame || Main.playerInventory || Main.LocalPlayer.talkNPC >= 0 || Main.mapFullscreen
			|| !Main.mouseItem.IsAir || Main.ingameOptionsWindow || Main.inFancyUI) {
			Reset();
			return;
		}

		bool found = false;
		foreach (MobileTouchContact contact in MobileJoystickInput.ActiveContacts) {
			if (contact.Id != contactId) continue;
			found = !contact.IsReleased;
			if (found && directToolPress) target = contact.Position;
			break;
		}
		if (!found) { contactId = NoContact; tilePending = directToolPress = false; }

		bool startedContact = false;
		if (!MobileTrackpad.Enabled && contactId == NoContact) foreach (MobileTouchContact contact in MobileJoystickInput.ActiveContacts) {
			if (contact.State != TouchLocationState.Pressed || MobileJoystickInput.ContactBelongsToControl(contact)
				|| contact.Position.X < 0 || contact.Position.Y < 0 || contact.Position.X >= PlayerInput.RealScreenWidth || contact.Position.Y >= PlayerInput.RealScreenHeight
				|| MobileInventorySystem.BlocksControl(contact.Position) || OverUi(contact.Position)) continue;
			Vector2 world = ToWorld(contact.Position);
			Point tile = world.ToTileCoordinates();
			bool nonTile = HasChatTarget(world) || HasProjectileTarget(world);
			bool hasTile = WorldGen.InWorld(tile.X, tile.Y, 10) && Main.tile[tile.X, tile.Y].HasTile;
			if (!nonTile && !hasTile && Main.LocalPlayer.HeldItem.IsAir) continue;
			contactId = contact.Id;
			startedContact = true;
			target = contact.Position;
			tilePending = !nonTile && hasTile;
			// World use belongs to this finger, independent of the movement stick
			// and the launcher's single primary-mouse contact. Interaction targets
			// still get their existing native right-click pass first.
			directToolPress = !nonTile && !hasTile;
			directToolStarted = directToolPress;
			toolPressStart = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
			break;
		}

		bool aimRight = MobileJoystickInput.AimRightClickHeld;
		bool buttonRight = MobileJoystickInput.IsActionDown(MobileControlAction.Interact);
		if (MobileCalamityMechInput.HasActiveMech) {
			aimRight &= MobileJoystickInput.AttackJustPressed || MobileJoystickInput.WasActionPressed(MobileControlAction.AimModeToggle);
			buttonRight &= MobileJoystickInput.WasActionPressed(MobileControlAction.Interact);
		}
		bool explicitRight = aimRight || buttonRight
			|| MobileJoystickInput.IsActionDown(MobileControlAction.SmartInteract)
			|| (MobileJoystickInput.IsActionDown(MobileControlAction.SwapUseInteract)
				&& (Main.HasSmartInteractTarget || Main.HoveringOverAnNPC));
		if (DirectUseHeld) SetPointer(target);
		rightDown = OwnsWorldPress && !directToolPress || (explicitRight && !MobileInventorySystem.BlocksWorldUse);
		rightPressed = rightDown && (!wasDown || startedContact);
		if (rightDown) {
			if (OwnsWorldPress) SetPointer(target);
			else if (MobileJoystickInput.AimRightClickHeld) MobileJoystickPlayer.ApplyTouchAim();
			else if (MobileJoystickInput.HasTouchAimPosition) SetPointer(MobileJoystickInput.TouchAimPosition);
			else if (Main.HasSmartInteractTarget && Main.SmartInteractNPC >= 0)
				SetPointer(ToScreen(Main.npc[Main.SmartInteractNPC].Center));
			else if (Main.HasSmartInteractTarget && Main.SmartInteractX >= 0)
				SetPointer(ToScreen(new Vector2(Main.SmartInteractX * 16 + 8, Main.SmartInteractY * 16 + 8)));
			else SetPointer(ToScreen(Main.LocalPlayer.Center + new Vector2(Main.LocalPlayer.direction * 80, 0)));
			Main.mouseRight = PlayerInput.Triggers.Current.MouseRight = true;
			Main.mouseRightRelease = rightPressed || rawRight && Main.mouseRightRelease;
			PlayerInput.Triggers.JustPressed.MouseRight |= rightPressed;
			PlayerInput.Triggers.JustReleased.MouseRight = false;
			if (rightPressed) Main.npcChatRelease = true;
			Main.mouseLeft = PlayerInput.Triggers.Current.MouseLeft = false;
			PlayerInput.Triggers.JustPressed.MouseLeft = false;
		}
		else if (wasDown && !rawRight) {
			Main.mouseRight = false;
			Main.mouseRightRelease = true;
			PlayerInput.Triggers.Current.MouseRight = false;
			PlayerInput.Triggers.JustPressed.MouseRight = false;
			PlayerInput.Triggers.JustReleased.MouseRight = true;
		}
		syntheticWasDown = rightDown;
	}

	internal static void SetControls(Player player)
	{
		if (DirectUseHeld) {
			SetPointer(target);
			Main.SmartCursorWanted_Mouse = player.controlSmart = false;
			player.controlUseTile = false;
			player.delayUseItem = false;
			player.controlUseItem = true;
			if (directToolStarted) player.releaseUseItem = true;
			player.mouseInterface = false;
			Main.blockMouse = false;
			return;
		}
		if (!rightDown) return;
		// Native mouse snapshots can be restored between input polling and SetControls.
		if (!OwnsWorldPress) {
			if (MobileJoystickInput.AimRightClickHeld) MobileJoystickPlayer.ApplyTouchAim();
			else if (MobileJoystickInput.HasTouchAimPosition) SetPointer(MobileJoystickInput.TouchAimPosition);
		}
		player.controlUseItem = false;
		player.controlUseTile = !OwnsWorldPress || tilePending;
		if (rightPressed) player.releaseUseTile = true;
		player.mouseInterface = false;
		Main.blockMouse = false;
	}

	internal static void LookForInteractions(On_Player.orig_LookForTileInteractions orig, Player player)
	{
		bool directTile = tilePending && OwnsWorldPress;
		bool aimed = MobileInteractionTarget.HasAim;
		if (player.whoAmI != Main.myPlayer || !directTile && !aimed) { orig(player); return; }
		// A direct screen tap must not select a neighbouring smart-cursor object.
		bool smart = Main.SmartInteractShowingGenuine;
		bool inventoryOpen = Main.playerInventory;
		bool fancyOpen = Main.inFancyUI;
		var ui = Main.InGameUI.CurrentState;
		// SmartCursorLookup has already selected the mining/placement target.
		// Hover and right-click lookup use the logical crosshair temporarily;
		// ItemCheck must still receive the native smart-cursor target afterwards.
		Point toolTarget = new(Player.tileTargetX, Player.tileTargetY);
		Main.SmartInteractShowingGenuine = false;
		Vector2 pointer = directTile ? target : rightDown && !MobileJoystickInput.AimRightClickHeld
			? MobileJoystickInput.TouchAimPosition : MobileJoystickPlayer.GetTouchAimPointer();
		SetPointer(pointer);
		Point tile = ToWorld(pointer).ToTileCoordinates();
		Player.tileTargetX = tile.X;
		Player.tileTargetY = tile.Y;
		if (aimed) player.cursorItemIconEnabled = false;
		try {
			orig(player);
			if (aimed) MobileInteractionTarget.CaptureTile(player, tile);
		}
		finally {
			Main.SmartInteractShowingGenuine = smart;
			if (!directTile) {
				Player.tileTargetX = toolTarget.X;
				Player.tileTargetY = toolTarget.Y;
			}
		}
		if (!directTile) return;
		tilePending = false;
		// GlobalTile.RightClick has no return value. Opening UI or consuming the
		// native press also signals that its callback handled this contact.
		bool handled = player.tileInteractionHappened || !Main.mouseRightRelease
			|| Main.playerInventory != inventoryOpen || Main.inFancyUI != fancyOpen || Main.InGameUI.CurrentState != ui;
		if (handled) {
			player.controlUseItem = false;
			if (Main.playerInventory) MobileInventorySystem.OpenedFromControl();
			return;
		}
		// An unhandled block tap uses the held item, even when the primary
		// mouse belongs to a movement finger. Keep handled chests/doors as UI.
		if (!player.HeldItem.IsAir) directToolPress = directToolStarted = true;
		else contactId = NoContact;
		rightDown = syntheticWasDown = false;
		Main.mouseRight = PlayerInput.Triggers.Current.MouseRight = rawRight;
		PlayerInput.Triggers.JustPressed.MouseRight = false;
		Main.mouseLeft = PlayerInput.Triggers.Current.MouseLeft = rawLeft || DirectUseHeld;
		PlayerInput.Triggers.JustPressed.MouseLeft = rawLeftPressed || DirectUseHeld;
		PlayerInput.Triggers.JustReleased.MouseLeft = rawLeftReleased && !DirectUseHeld;
		player.controlUseTile = rawRight;
		player.controlUseItem = rawLeft && !player.noItems && !player.CCed;
		if (DirectUseHeld) SetControls(player);
	}

	private static bool OverUi(Vector2 point)
	{
		if (Main.blockMouse || Main.LocalPlayer.lastMouseInterface && !MobileJoystickInput.HasAnyControlTouch) return true;
		return Main.mapStyle == 1 && MobileJoystickLayout.GetMinimapBounds(MobileJoystickConfig.Instance,
			PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight).Contains(point.ToPoint());
	}

	internal static void HoverOverNpcs(On_Main.orig_HoverOverNPCs orig, Main main, Rectangle mouseRectangle)
	{
		if (!MobileInteractionTarget.HasAim) { orig(main, mouseRectangle); return; }
		// NPC hover runs later, during drawing. Native smart interaction can still
		// refer to another NPC; keep preview and right-click dispatch on the crosshair.
		Vector2 point = MobileJoystickInput.TouchAimPosition;
		SetPointer(point);
		Point world = ToWorld(point).ToPoint();
		bool smart = Main.SmartInteractShowingGenuine;
		Main.SmartInteractShowingGenuine = false;
		try { orig(main, new Rectangle(world.X, world.Y, 1, 1)); }
		finally { Main.SmartInteractShowingGenuine = smart; }
	}

	internal static Vector2 ToWorld(Vector2 screen)
	{
		Vector2 size = new(PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight);
		Vector2 local = size * .5f + (screen - size * .5f) / Main.GameViewMatrix.Zoom;
		if (Main.LocalPlayer.gravDir == -1) local.Y = size.Y - local.Y;
		return Main.screenPosition + local;
	}
	internal static Vector2 ToScreen(Vector2 world)
	{
		Vector2 size = new(PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight);
		Vector2 local = world - Main.screenPosition;
		if (Main.LocalPlayer.gravDir == -1) local.Y = size.Y - local.Y;
		return size * .5f + (local - size * .5f) * Main.GameViewMatrix.Zoom;
	}
	internal static void SetPointer(Vector2 screen)
	{
		PlayerInput.SetZoom_Unscaled();
		Main.mouseX = (int)screen.X;
		Main.mouseY = (int)screen.Y;
		PlayerInput.CacheMousePositionForZoom();
		PlayerInput.SetZoom_World();
	}
	private static bool HasChatTarget(Vector2 world)
	{
		foreach (NPC npc in Main.ActiveNPCs) {
			bool canChat = NPCLoader.CanChat(npc) ?? (npc.townNPC || npc.type is 105 or 106 or 123 or 354 or 376 or 579 or 453 or 589 or 685);
			if (!canChat) continue;
			Rectangle bounds = new((int)npc.Bottom.X - npc.frame.Width / 2, (int)npc.Bottom.Y - npc.frame.Height, npc.frame.Width, npc.frame.Height);
			NPCLoader.ModifyHoverBoundingBox(npc, ref bounds);
			if (bounds.Contains(world.ToPoint())) return true;
		}
		return false;
	}
	private static bool HasProjectileTarget(Vector2 world)
	{
		Player player = Main.LocalPlayer;
		Vector2 center = player.Center;
		foreach (Projectile projectile in Main.ActiveProjectiles)
			if (projectile.Hitbox.Contains(world.ToPoint()) && player.IsProjectileInteractibleAndInInteractionRange(projectile, ref center)) return true;
		return false;
	}
}
