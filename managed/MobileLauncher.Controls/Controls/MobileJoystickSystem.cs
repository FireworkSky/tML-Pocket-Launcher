using System.Collections.Generic;
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameInput;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace MobileJoystick;

public partial class MobileJoystickSystem : MobileSystem
{
	private IDisposable androidBackKeyboardHook;

	public override void Load()
	{
		if (Main.dedServ) {
			return;
		}
		MobileTouchInput.Load();
		androidBackKeyboardHook = MobileKeyboardBindings.InstallBackInput();
		On_Main.DoUpdate_HandleInput += MobileKeyboardBindings.HandleInput;
		On_Main.DrawCursor += DrawNativeCursor;
		On_Main.DrawThickCursor += DrawNativeCursorBorder;
		On_Main.Draw += DrawTitleCursor;
		On_Player.QuickGrapple += MobileJoystickInput.AimNativeGrapple;
		On_Player.LookForTileInteractions += MobileWorldInteraction.LookForInteractions;
		On_Player.ItemCheck += EnsureSmartCursorMiningInput;
		On_Main.HoverOverNPCs += MobileWorldInteraction.HoverOverNpcs;

	}

	public override void Unload()
	{
		MobileTouchInput.Unload();
		if (!Main.dedServ) {
			On_Main.DoUpdate_HandleInput -= MobileKeyboardBindings.HandleInput;
			androidBackKeyboardHook?.Dispose();
			On_Main.DrawCursor -= DrawNativeCursor;
			On_Main.DrawThickCursor -= DrawNativeCursorBorder;
			On_Main.Draw -= DrawTitleCursor;
		}
		if (!Main.dedServ) On_Player.QuickGrapple -= MobileJoystickInput.AimNativeGrapple;
		if (!Main.dedServ) On_Player.LookForTileInteractions -= MobileWorldInteraction.LookForInteractions;
		if (!Main.dedServ) On_Player.ItemCheck -= EnsureSmartCursorMiningInput;
		if (!Main.dedServ) On_Main.HoverOverNPCs -= MobileWorldInteraction.HoverOverNpcs;
		MobileWorldInteraction.Reset();
		MobileJoystickInput.Reset();
		NativeControlTheme.Unload();
		UiTextureAssets.Unload();
		DefaultControlTextures.Unload();
	}

	public override void PostSetupContent()
	{
		if (!Main.dedServ) DefaultControlTextures.Load();
	}

	public override void OnWorldUnload() { MobileJoystickInput.Reset(); MobileTouchInput.Reset(); MobileInteractionHighlight.Clear(); }

	public override void PostUpdateInput()
	{
		if (!Main.hasFocus) MobileTouchInput.Reset();
		MobileJoystickInput.Update();
		if (MobileInventorySystem.Enabled)
			MobileControlsSession.Get<MobileInventorySystem>().ReadPointer();
		MobileWorldInteraction.UpdateInput();
		MobileModKeybinds.Apply();
		MobileJoystickInput.ApplyGameplayTriggers();
	}

	private static void EnsureSmartCursorMiningInput(On_Player.orig_ItemCheck orig, Player player)
	{
		if (player.whoAmI == Main.myPlayer && MobileJoystickInput.EnabledThisFrame
			&& MobileJoystickInput.SmartCursorToolUseHeld && !Main.playerInventory && !Main.mapFullscreen
			&& !Main.ingameOptionsWindow && !Main.inFancyUI && player.talkNPC < 0
			&& Main.mouseItem.IsAir && !MobileInventorySystem.BlocksWorldUse) {
			player.delayUseItem = false;
			player.controlUseItem = true;
			if (MobileJoystickInput.AttackJustPressed) player.releaseUseItem = true;
			player.mouseInterface = false;
			Main.blockMouse = false;
		}
		orig(player);
	}

	private static bool HideNativeCursor => MobileJoystickConfig.Instance?.ShowMouseCursor != true;
	private static bool drawTitleCursor;
	private static bool CanDrawNativeCursor => !HideNativeCursor && (!Main.gameMenu || drawTitleCursor);
	private static void DrawNativeCursor(On_Main.orig_DrawCursor orig, Vector2 bonus, bool smart)
	{
		if (CanDrawNativeCursor) orig(bonus, smart);
	}
	private static Vector2 DrawNativeCursorBorder(On_Main.orig_DrawThickCursor orig, bool smart)
		=> CanDrawNativeCursor ? orig(smart) : Vector2.Zero;

	private static void DrawTitleCursor(On_Main.orig_Draw orig, Main main, GameTime time)
	{
		orig(main, time);
		MobileKeyboardButton.Draw(Main.spriteBatch);
		if (!Main.gameMenu || HideNativeCursor || TextureAssets.Cursors[0]?.IsLoaded != true) return;
		Vector2 cursor = MobileTrackpad.ScreenPosition;
		int oldX = Main.mouseX, oldY = Main.mouseY;
		Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
			DepthStencilState.None, RasterizerState.CullCounterClockwise);
		try {
			drawTitleCursor = true;
			Main.mouseX = (int)cursor.X;
			Main.mouseY = (int)cursor.Y;
			Main.DrawCursor(Main.DrawThickCursor());
		}
		finally {
			drawTitleCursor = false;
			Main.mouseX = oldX;
			Main.mouseY = oldY;
			Main.spriteBatch.End();
		}
	}

	public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
	{
		if (HideNativeCursor) foreach (var layer in layers)
			if (layer.Name == "Vanilla: Cursor") layer.Active = false;
		if (MobileMenuSystem.EditingCustomControls) {
			foreach (var layer in layers)
				if (layer.Name is "Vanilla: Map / Minimap" or "Vanilla: Hotbar" or "Vanilla: Resource Bars") layer.Active = false;
		}
		MobileJoystickConfig config = MobileJoystickConfig.Instance;
		if (config != null && config.EnableMobileControls && !Main.playerInventory && !Main.gameMenu) {
			int hotbarIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Hotbar"));
			if (hotbarIndex != -1) {
				layers[hotbarIndex] = new LegacyGameInterfaceLayer(
					"MobileJoystick: Mobile Hotbar",
					() => {
						DrawMobileHotbar();
						return true;
					},
					InterfaceScaleType.None);
			}
		}

		int mouseTextIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Mouse Text"));
		if (mouseTextIndex == -1) {
			return;
		}

		layers.Insert(mouseTextIndex, new LegacyGameInterfaceLayer(
			"MobileJoystick: Mobile Touch Controls",
			() => {
				DrawControls();
				MobilePerformanceHud.Draw(Main.spriteBatch);
				return true;
			},
			InterfaceScaleType.None));
	}

	private void DrawControls()
	{
		MobileJoystickConfig config = MobileJoystickConfig.Instance;
		if (!MobileJoystickInput.EnabledThisFrame || config == null) {
			return;
		}

		SpriteBatch spriteBatch = Main.spriteBatch;
		BeginSmoothControls(spriteBatch);
		MobileControlLayout layout = MobileJoystickInput.Layout;
		Color color = Color.White * config.Opacity;

        foreach (MobileControlStick stick in layout.Sticks) {
			if (!MobileJoystickInput.IsStickAvailable(stick) || stick.Role == MobileJoystickRole.Grapple) continue;
            bool moving = stick.Role == MobileJoystickRole.Move;
            bool active = moving ? MobileJoystickInput.MoveStick == stick : MobileJoystickInput.AimStick == stick;
            Vector2 offset = !active ? Vector2.Zero : moving ? MobileJoystickInput.JoystickVector : MobileJoystickInput.AttackKnobOffset;
			if (CustomControlTextures.Draw(spriteBatch, stick.CustomTexture, stick.Center + offset * stick.Radius * .46f,
				new Vector2(stick.Radius * 2), active ? Color.White : color)) continue;
			NativeControlTheme.Stick(spriteBatch, stick.Role, stick.Center, stick.Radius, offset, color, active, textureName: stick.TextureName);
            if (moving && config.JoystickUpJumps)
                NativeControlTheme.Legacy(spriteBatch, "MoveJumpHint", stick.Center - new Vector2(0, stick.Radius * .76f), 25f * layout.Scale, color);
        }

		foreach (MobileControlButton button in layout.Buttons) {
			if (!MobileJoystickInput.IsButtonAvailable(button)) continue;
			if (button.Launcher != null) { LauncherControlArtwork.Draw(spriteBatch, button, color); continue; }
			if (button.Id == null && button.Action is MobileControlAction.Inventory or MobileControlAction.Settings) {
				continue;
			}

			bool active = button.Action switch {
				MobileControlAction.ModKeybind => MobileModKeybinds.IsHeld(button.ModKeybind),
				MobileControlAction.KeyboardKey => MobileKeyboardBindings.IsHeld(button.KeyboardKey),
				MobileControlAction.PerformanceDisplay => MobilePerformanceHud.Visible,
				_ => MobileJoystickInput.IsActionDown(button.Action)
			};
			if (CustomControlTextures.Draw(spriteBatch, button.CustomTexture, button.Center, new Vector2(button.Radius * 2), active ? Color.White : color)) continue;
			if (MobileHudArtwork.DrawToolbar(button, MobileJoystickInput.IsActionDown(button.Action))) continue;
			if (button.Action == MobileControlAction.KeyboardKey) {
				DrawTexture(spriteBatch, button.TextureName, button.Center, button.Radius * 2f, active ? Color.White : color);
				MobileKeyboardBindings.DrawLabel(spriteBatch, button.KeyboardKey, button.Center, button.Radius * 1.65f,
					.65f * layout.Scale, active ? Color.White : color);
				continue;
			}
			if (button.Action == MobileControlAction.ModKeybind) {
				DrawTexture(spriteBatch, "ItemButtonFrame", button.Center, button.Radius * 2f, active ? Color.White : color);
				string label = MobileModKeybinds.Label(button.ModKeybind);
				float textScale = Math.Min(.55f * layout.Scale, button.Radius * 1.65f / Math.Max(1f, FontAssets.MouseText.Value.MeasureString(label).X));
				Utils.DrawBorderString(spriteBatch, label, button.Center, active ? Color.White : color, textScale, .5f, .5f);
				continue;
			}
			if (button.Action == MobileControlAction.Grapple) {
				var grapple = MobileJoystickInput.ActiveGrapple;
				bool held = grapple.Active && (button.Id != null ? grapple.Button.Id == button.Id : grapple.Button.Center == button.Center);
				Vector2 center = button.Center;
				if (held) {
					DrawTexture(spriteBatch, "PhoneGrapplePad", center, button.Radius * 2.6f, new Color(26, 40, 48) * (color.A / 255f));
					center += grapple.KnobOffset * button.Radius;
				}
				MobileControlArtwork.DrawButton(spriteBatch, button.Action, center, button.Radius, held ? Color.White : color,
					held, textureName: button.TextureName);
				continue;
			}
			if (button.Action == MobileControlAction.Interact && MobileJoystickInput.InteractHeld
				&& MobileJoystickInput.InteractButton.Equals(button)) {
				if (MobileInteractionArtwork.HasTarget) {
					MobileControlArtwork.DrawButton(spriteBatch, button.Action, button.Center, button.Radius, Color.White, true, textureName: button.TextureName);
					continue;
				}
				NativeControlTheme.Stick(spriteBatch, MobileJoystickRole.Aim, button.Center, button.Radius * 1.4f,
					MobileJoystickInput.InteractKnobOffset, Color.White, true,
					itemOverride: MobileJoystickInput.GetPriorityRightClickOrHeldItem());
				continue;
			}
			if (button.Action != MobileControlAction.Heal && MobileControlArtwork.DrawButton(spriteBatch, button.Action, button.Center, button.Radius, color,
				active, textureName: GetButtonTexture(button))) continue;
			if (button.Action == MobileControlAction.Interact) {
				DrawItemButton(spriteBatch, button.Center, button.Radius, MobileJoystickInput.GetPriorityRightClickOrHeldItem(), active ? Color.White : color);
				continue;
			}

			if (button.Action == MobileControlAction.UseItem) {
				DrawItemButton(spriteBatch, button.Center, button.Radius, MobileJoystickInput.GetHeldItem(), active ? Color.White : color);
				continue;
			}

			if (button.Action == MobileControlAction.Heal) {
				Item heal = FindHealingItem();
				if (heal != null) {
					MobileControlArtwork.DrawButton(spriteBatch, button.Action, button.Center, button.Radius, color, active, heal, GetButtonTexture(button));
				}
				else {
					MobileControlArtwork.DrawButton(spriteBatch, button.Action, button.Center, button.Radius, color * .45f);
				}
				continue;
			}

			DrawTexture(spriteBatch, GetButtonTexture(button), button.Center, button.Radius * 2f, active ? Color.White : color);
		}

		if (MobileJoystickInput.ShowTouchCrosshair) {
			Vector2 crosshairPosition = MobileJoystickInput.TouchCrosshairPosition;
			MobilePrecisionMagnifier.DrawAimMarker(spriteBatch, crosshairPosition, layout.Scale * 1.08f);
		}
		if (MobileJoystickInput.GrappleHeld) {
			Vector2 origin = MobileWorldInteraction.ToScreen(Main.LocalPlayer.MountedCenter);
			Vector2 target = origin + MobileJoystickInput.ActiveGrapple.Direction * MobileJoystickInput.CharacterAimRadius;
			target.X = MathHelper.Clamp(target.X, 0f, Main.screenWidth - 1f);
			target.Y = MathHelper.Clamp(target.Y, 0f, Main.screenHeight - 1f);
			DrawTouchCrosshair(spriteBatch, target, layout.Scale, Color.White);
		}

		DrawRadialControl(spriteBatch, color);
		DrawStatusText(spriteBatch, layout);
	}

	private void DrawMobileHotbar()
	{
		if (Main.mapFullscreen) return;
		if (Main.myPlayer < 0 || Main.myPlayer >= Main.maxPlayers) {
			return;
		}

		Player player = Main.LocalPlayer;
		if (!player.active || player.dead || player.ghost) {
			return;
		}

		SpriteBatch spriteBatch = Main.spriteBatch;
		BeginSmoothControls(spriteBatch);
		MobileJoystickConfig config = MobileJoystickConfig.Instance;
		MobileHotbarLayout layout = MobileJoystickLayout.CreateHotbarLayout(config);
		float scale = layout.EffectiveScale;
		Color buttonColor = Color.White * (config?.Opacity ?? 0.78f);

		if (config?.ControlLayout != MobileControlLayoutMode.Custom) {
			if (!MobileControlAvoidance.Obscures(layout.InventoryButton))
				DrawHotbarIcon(spriteBatch, layout.InventoryButton, "BackpackIcon", MobileJoystickInput.IsActionDown(MobileControlAction.Inventory), buttonColor);
			if (!MobileControlAvoidance.Obscures(layout.SettingsButton))
				DrawHotbarIcon(spriteBatch, layout.SettingsButton, "Settings", MobileJoystickInput.IsActionDown(MobileControlAction.Settings), buttonColor);
		}

		for (int i = 0; i < 10; i++) {
			Rectangle slot = layout.GetSlot(i);
			if (MobileControlAvoidance.Obscures(slot)) continue;
			bool selected = player.selectedItem == i;
			Item item = player.inventory[i];
			MobileInventorySystem.DrawMobileHotbarBackground(spriteBatch, slot, selected, item);
			if (!item.IsAir) {
				DrawItemInSlot(spriteBatch, item, slot, scale);
			}

			if (slot.Contains(Main.mouseX, Main.mouseY)) {
				player.mouseInterface = true;
				if (!item.IsAir && !MobileTouchInput.UsingTouch) {
					Main.HoverItem = item.Clone();
					Main.hoverItemName = item.Name;
				}
			}
		}
	}

	private void DrawHotbarIcon(SpriteBatch spriteBatch, Rectangle bounds, string textureName, bool active, Color color)
	{
		DrawTexture(spriteBatch, textureName, bounds.Center.ToVector2(), bounds.Width * (active ? 0.88f : 0.8f), active ? Color.White : color);
	}

	private static void BeginSmoothControls(SpriteBatch spriteBatch)
	{
		// These layers use physical screen coordinates. Linear sampling hides the
		// uneven stair steps that fractional scaling would create with point sampling.
		spriteBatch.End();
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
			DepthStencilState.None, RasterizerState.CullCounterClockwise);
	}

	private void DrawItemButton(SpriteBatch spriteBatch, Vector2 center, float radius, Item item, Color color)
	{
		DrawTexture(spriteBatch, "ItemButtonFrame", center, radius * 2.12f, color);
		DrawItemIcon(spriteBatch, item, center, radius * 1.35f, color);
	}

	private bool DrawTexture(SpriteBatch spriteBatch, string name, Vector2 center, float maxSize, Color color)
        => NativeControlTheme.Legacy(spriteBatch, name, center, maxSize, color);

	private void DrawTouchCrosshair(SpriteBatch spriteBatch, Vector2 center, float scale, Color color)
	{
		DrawTexture(spriteBatch, "AimReticle", center, 28f * scale, color);
	}

	private static string GetButtonTexture(MobileControlButton button) => button.TextureName;

	private static void DrawStatusText(SpriteBatch spriteBatch, MobileControlLayout layout)
	{
		string text = MobileJoystickInput.StatusText;
		if (string.IsNullOrWhiteSpace(text) || MobileJoystickInput.StatusTextOpacity <= 0f) {
			return;
		}

		float textScale = MathHelper.Clamp(layout.Scale * 0.5f, .78f, 1.02f) * MobileUiMetrics.FontScale;
		Vector2 position = new(Main.screenWidth * 0.5f, Main.screenHeight * 0.705f);
		Utils.DrawBorderString(spriteBatch, text, position, Color.White * MobileJoystickInput.StatusTextOpacity, textScale, 0.5f, 0.5f);
	}

	private static void DrawSelectedItem(SpriteBatch spriteBatch, Vector2 center, float maxSize, Color color)
	{
		if (Main.myPlayer < 0 || Main.myPlayer >= Main.maxPlayers) {
			return;
		}

		DrawItemIcon(spriteBatch, Main.LocalPlayer.HeldItem, center, maxSize, color);
	}

	private static Item FindHealingItem()
	{
		Item best = null;
		foreach (Item item in Main.LocalPlayer.inventory) {
			if (item != null && !item.IsAir && item.healLife > 0 && (best == null || item.healLife > best.healLife)) {
				best = item;
			}
		}
		return best;
	}

	internal static void DrawItemIcon(SpriteBatch spriteBatch, Item item, Vector2 center, float maxSize, Color color)
	{
		if (item == null) {
			return;
		}

		if (item.IsAir) {
			return;
		}

		Main.instance.LoadItem(item.type);
		Texture2D texture = TextureAssets.Item[item.type].Value;
		Rectangle frame = Main.itemAnimations[item.type]?.GetFrame(texture) ?? texture.Frame();
		float scale = Math.Min(maxSize / frame.Width, maxSize / frame.Height);
		Vector2 origin = frame.Size() * 0.5f;
		spriteBatch.Draw(texture, center, frame, color, 0f, origin, scale, SpriteEffects.None, 0f);
	}

	private static void DrawItemInSlot(SpriteBatch spriteBatch, Item item, Rectangle slot, float scale)
	{
		Vector2 center = slot.Center.ToVector2();
		ItemSlot.DrawItemIcon(item, ItemSlot.Context.InventoryItem, spriteBatch, center, slot.Width / 52f * .9f, 32f, Color.White);

		if (item.stack > 1) {
			Utils.DrawBorderString(spriteBatch, item.stack.ToString(), new Vector2(slot.X + 3f * scale, slot.Bottom - 15f * scale), Color.White, 0.72f * scale);
		}
	}

}
