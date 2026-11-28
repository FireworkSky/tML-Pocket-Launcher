using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace MobileJoystick;

public partial class MobileInventorySystem
{
	private readonly record struct InventoryGestureSlot(Item[] Items, int Context, int Index, Rectangle Bounds, bool Locked)
	{
		public Item Item => Items[Index];
		public bool SameSlot(InventoryGestureSlot other) => ReferenceEquals(Items, other.Items) && Index == other.Index;
	}

	private readonly List<InventoryGestureSlot> gestureSlots = new();
	private InventoryGestureSlot? dragSource, splitSource;
	private Item dragItem, splitItem;
	private int dragStack, dragAmount, dragType, dragPrefix, splitStack, splitAmount, splitType, splitPrefix;
	private Vector2 dragStart;
	private bool dragging, splitSliding;
	private bool pendingSelectedUse;
	private bool explicitUseActive;
	private int explicitUseGrace;
	internal static bool HasItemGesture => Active && Instance.dragSource.HasValue;

	private bool CanPauseInventoryUse(On_Main.orig_CanPauseGame orig)
	{
		if (MobileChatSystem.IsOpen) return false;
		// Vanilla blocks every mouse UI while itemAnimation > 0. Finish the
		// existing attack before autopause freezes that timer indefinitely.
		if (Active && Main.autoPause && (Main.LocalPlayer.itemAnimation > 0 || Main.LocalPlayer.itemTime > 0)) return false;
		if (Active && (pendingSelectedUse || explicitUseActive)) {
			if (pendingSelectedUse || explicitUseGrace-- > 0 || Main.LocalPlayer.itemAnimation > 0) return false;
			explicitUseActive = false;
		}
		return orig();
	}

	internal static bool ConsumeSelectedUse()
	{
		bool pending = Active && Instance.pendingSelectedUse;
		Instance.pendingSelectedUse = false;
		if (pending) { Instance.explicitUseActive = true; Instance.explicitUseGrace = 2; }
		return pending;
	}

	private bool UseNativeInventoryClick => !Main.mouseItem.IsAir || Main.mouseRight
		|| Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift)
		|| Main.keyState.IsKeyDown(Keys.LeftControl) || Main.keyState.IsKeyDown(Keys.RightControl)
		|| Main.keyState.IsKeyDown(Keys.LeftAlt) || Main.keyState.IsKeyDown(Keys.RightAlt);
	private Rectangle SplitPanel => layout.At(104, 200, 258, 60);
	private Rectangle SplitOutput => layout.At(314, 208, 44, 44);
	private Rectangle SplitTrack => layout.At(162, 225, 142, 12);

	private void CancelInventoryGesture()
	{
		selectedShopSlot = selectedShopNpc = -1;
		selectedShopStock = null;
		heldShopItem = null;
		inventoryToolsOpen = false;
		heldHint = null;
		CloseRecipeDetails();
		dragSource = splitSource = null;
		dragItem = splitItem = null;
		dragging = splitSliding = false;
		pendingSelectedUse = false;
		explicitUseActive = false;
		explicitUseGrace = 0;
		heldCraftRecipe = -1;
		craftingCategoryOpen = false;
	}

	private void BeginInventoryDrag(InventoryGestureSlot source, int amount = 0)
	{
		if (source.Item.IsAir) return;
		dragSource = source;
		dragItem = source.Item;
		dragStack = source.Item.stack;
		dragType = source.Item.type;
		dragPrefix = source.Item.prefix;
		dragAmount = amount;
		dragStart = pointer;
		dragging = false;
	}

	private void BeginInventoryGestureFrame(bool fresh)
	{
		gestureSlots.Clear();
		if (!fresh) return;
		if (dragSource.HasValue && (!ReferenceEquals(dragSource.Value.Item, dragItem) || dragItem.stack != dragStack
			|| dragItem.type != dragType || dragItem.prefix != dragPrefix))
			dragSource = null;
		if (splitSource.HasValue && (!ReferenceEquals(splitSource.Value.Item, splitItem) || splitItem.stack != splitStack
			|| splitItem.type != splitType || splitItem.prefix != splitPrefix))
			splitSource = null;
		Vector2 dragPosition = pointerReleased ? pointerReleasePosition : pointer;
		if (dragSource.HasValue && (pointerDown || pointerReleased)
			&& Vector2.DistanceSquared(dragPosition, dragStart) >= 64 * layout.Scale * layout.Scale) {
			dragging = true;
			CloseRecipeDetails();
		}
		if (!splitSource.HasValue) return;
		if (pointerPressed && SplitOutput.Contains(pointer.ToPoint()) && Main.mouseItem.IsAir) {
			BeginInventoryDrag(splitSource.Value, splitAmount);
			used = true;
		}
		if (pointerPressed && layout.At(158, 208, 150, 44).Contains(pointer.ToPoint())) splitSliding = true;
		if (splitSliding && pointerDown) {
			splitAmount = Math.Clamp((int)MathF.Round((pointer.X - SplitTrack.X) / SplitTrack.Width * splitStack), 1, splitStack - 1);
			used = true;
		}
		if (!pointerDown) splitSliding = false;
		if (SplitPanel.Contains(pointer.ToPoint())) used = true;
	}

	private void DrawSplitButton()
	{
		bool available = selectedSlot >= 0 && selectedSlot < 50 && Main.LocalPlayer.inventory[selectedSlot].stack > 1;
		if (!IconButton(layout.LeftAction(2), "Split", splitSource.HasValue ? T("CancelSplit") : T("SplitStack"), splitSource.HasValue, available || splitSource.HasValue)) return;
		if (splitSource.HasValue) { splitSource = null; return; }
		Rectangle r = layout.Inventory(selectedSlot);
		splitSource = new InventoryGestureSlot(Main.LocalPlayer.inventory, 0, selectedSlot, r, false);
		splitItem = splitSource.Value.Item;
		splitStack = splitItem.stack;
		splitType = splitItem.type;
		splitPrefix = splitItem.prefix;
		splitAmount = Math.Clamp(splitStack / 2, 1, splitStack - 1);
	}

	private void RightClickSelectedItem()
	{
		if (!Active || leftPage != 0 || (uint)selectedSlot >= 50 || touchId >= 0 || dragSource.HasValue || splitSource.HasValue
			|| inventoryToolsOpen || craftingCategoryOpen || !Main.mouseItem.IsAir) return;
		Player player = Main.LocalPlayer;
		if (ItemLoader.CanRightClick(player.inventory[selectedSlot]))
			MobileJoystickInput.UseRightClickItem(player, selectedSlot);
	}

	private void DrawSelectedItemAction()
	{
		if (MobileShop) { DrawShopSellAction(); return; }
		if (selectedSlot < 0 || selectedSlot >= 50) return;
		Item item = Main.LocalPlayer.inventory[selectedSlot];
		bool rightClick = ItemLoader.CanRightClick(item);
		bool equip = item.accessory || item.headSlot >= 0 || item.bodySlot >= 0 || item.legSlot >= 0
			|| item.mountType != MountID.None || (item.shoot > ProjectileID.None && Main.projHook[item.shoot]);
		Rectangle r = layout.Apply(MobileInterfaceRegion.InventoryActions, layout.At(8, 260, 62, 62));
		InventoryButtonSurface(r, "SlotPurple", item.IsAir ? Color.White * .3f : Color.White * .86f, !item.IsAir);
		Rectangle icon = layout.Apply(MobileInterfaceRegion.InventoryActions, layout.At(18, 264, 42, 42));
		SetSlotScale(icon);
		ItemSlot.Draw(Main.spriteBatch, ref item, 14, icon.TopLeft());
		Text(rightClick ? T("OpenUse") : equip ? T("Equip") : T("Use"), layout.Apply(MobileInterfaceRegion.InventoryActions, layout.At(8, 302, 62, 20)), rightClick ? .65f : .8f);
		if (item.IsAir || !Click(r)) return;
		if (rightClick) { MobileJoystickInput.UseRightClickItem(Main.LocalPlayer, selectedSlot); Recipe.FindRecipes(); }
		else if (equip) { ItemSlot.SwapEquip(Main.LocalPlayer.inventory, 0, selectedSlot); Recipe.FindRecipes(); }
		else { Main.LocalPlayer.selectedItem = selectedSlot; pendingSelectedUse = true; }
	}

	private void DrawInventoryGesture(bool fresh)
	{
		if (splitSource.HasValue) {
			MobileMenuFrame.Draw(Main.spriteBatch, Texture("Tool"), SplitPanel, Color.White, layout.Scale, 6);
			DrawSplitPreview(layout.At(108, 208, 44, 44), splitStack - splitAmount);
			DrawSplitPreview(SplitOutput, splitAmount);
			Main.spriteBatch.Draw(Texture("SliderTrack"), SplitTrack, Color.White);
			Rectangle thumb = layout.At(0, 0, 18, 24);
			thumb.X = SplitTrack.X + (int)(SplitTrack.Width * splitAmount / (float)splitStack) - thumb.Width / 2;
			thumb.Y = SplitTrack.Center.Y - thumb.Height / 2;
			Main.spriteBatch.Draw(Texture("SliderKnob"), thumb, Color.White);
		}
		if (!dragSource.HasValue) return;
		if (!fresh || !pointerReleased) return;
		Vector2 dropPosition = pointerReleasePosition;
		Point dropPoint = dropPosition.ToPoint();
		if (splitSource.HasValue && !dragging && SplitOutput.Contains(dropPoint) && Main.mouseItem.IsAir) {
			InventoryGestureSlot source = splitSource.Value;
			Main.mouseItem = ItemLoader.TransferWithLimit(source.Item, splitAmount);
			splitSource = null;
			CloseRecipeDetails();
			Recipe.FindRecipes();
		}
		if (!dragging && dragSource.Value.Context is ItemSlot.Context.ModdedAccessorySlot
			or ItemSlot.Context.ModdedVanityAccessorySlot or ItemSlot.Context.ModdedDyeSlot) {
			var source = dragSource.Value;
			foreach (var slot in gestureSlots) {
				if (!slot.Locked && source.SameSlot(slot) && slot.Bounds.Contains(dropPoint)
					&& !MobileExternalUiSystem.BlocksInventory(dropPosition) && Main.mouseItem.IsAir) {
					NativeTouchClick(() => ItemSlot.LeftClick(slot.Items, slot.Context, slot.Index));
					break;
				}
			}
		}
		if (dragging && !(splitSource.HasValue && SplitPanel.Contains(dropPoint))) {
			if (MobileExternalUiSystem.TryQueryItem(dropPosition, dragSource.Value.Item)) {
				dragSource = null; dragging = false;
				CloseRecipeDetails();
				return;
			}
			// An unknown foreign panel is never an instruction to discard a stack.
			if (MobileExternalUiSystem.BlocksInventory(dropPosition)) { dragSource = null; dragging = false; return; }
			if (MobileShop && layout.ShopArea.Contains(dropPoint)) {
				SellInventoryItem(dragSource.Value, dragAmount);
				dragSource = null; dragging = false;
				return;
			}
			bool overSlot = false;
			foreach (var target in gestureSlots) {
				if (target.Bounds.Contains(dropPoint) && !target.Locked) {
					overSlot = true;
					CommitInventoryDrag(target);
					break;
				}
			}
			if (!overSlot && !HitArea(dropPosition)) DropInventoryItem(dragSource.Value, dragAmount);
		}
		dragSource = null;
		dragging = false;
	}

	private void DrawInventoryDragPreview()
	{
		if (!dragging || !dragSource.HasValue || dragItem == null || dragItem.IsAir) return;
		Rectangle queryBounds = MobileExternalUiSystem.QueryDropBounds(pointer);
		if (!queryBounds.IsEmpty) Border(queryBounds, Color.Gold);
		Item preview = dragItem.Clone();
		if (dragAmount > 0) preview.stack = dragAmount;
		Rectangle r = layout.At(0, 0, 48, 48);
		r.X = (int)pointer.X - r.Width / 2; r.Y = (int)pointer.Y - r.Height / 2;
		float previousScale = Main.inventoryScale;
		try {
			SetSlotScale(r);
			ItemSlot.Draw(Main.spriteBatch, ref preview, ItemSlot.Context.ChatItem, r.TopLeft());
		}
		finally { Main.inventoryScale = previousScale; }
	}

	// Use Terraria's drop routine to retain mod data, animation and multiplayer sync.
	private void DropInventoryItem(InventoryGestureSlot source, int amount = 0)
	{
		if (source.Locked || !ReferenceEquals(source.Items, Main.LocalPlayer.inventory) || source.Index >= 58 || source.Item.IsAir
			|| source.Item.favorited || !Main.mouseItem.IsAir || Main.LocalPlayer.itemAnimation > 0) return;
		if (amount > 0 && amount < source.Item.stack) {
			Item part = ItemLoader.TransferWithLimit(source.Item, amount);
			Main.LocalPlayer.DropSelectedItem(source.Index, ref part);
		}
		else Main.LocalPlayer.DropSelectedItem(source.Index, ref source.Items[source.Index]);
		splitSource = null;
		CloseRecipeDetails();
	}

	private void DrawSplitPreview(Rectangle r, int amount)
	{
		Item preview = splitItem.Clone();
		preview.stack = amount;
		SetSlotScale(r);
		InventorySurface(r, "Slot", Color.White * .86f);
		ItemSlot.Draw(Main.spriteBatch, ref preview, 14, r.TopLeft());
	}

	private void CommitInventoryDrag(InventoryGestureSlot target)
	{
		var source = dragSource.Value;
		if (source.SameSlot(target) || !Main.mouseItem.IsAir || Main.LocalPlayer.itemAnimation != 0 || Main.LocalPlayer.itemTime != 0) return;
		if (ItemSlot.PickItemMovementAction(target.Items, target.Context, target.Index, source.Item) < 0) return;
		// A swap also needs to fit back into its source equipment/ammo slot.
		if (!target.Item.IsAir && target.Item.type != source.Item.type
			&& (source.Locked || ItemSlot.PickItemMovementAction(source.Items, source.Context, source.Index, target.Item) < 0)) return;
		bool oldLeft = Main.mouseLeft, oldRelease = Main.mouseLeftRelease;
		int oldOverride = Main.cursorOverride;
		try {
			Main.mouseLeft = Main.mouseLeftRelease = true;
			Main.cursorOverride = 0;
			if (dragAmount > 0) Main.mouseItem = ItemLoader.TransferWithLimit(source.Item, dragAmount);
			else ItemSlot.LeftClick(source.Items, source.Context, source.Index);
			if (Main.mouseItem.IsAir) return;
			ItemSlot.LeftClick(target.Items, target.Context, target.Index);
			if (!Main.mouseItem.IsAir) ItemSlot.LeftClick(source.Items, source.Context, source.Index);
			if (ReferenceEquals(target.Items, Main.LocalPlayer.inventory) && target.Index < 50) {
				selectedSlot = target.Index;
				Main.LocalPlayer.selectedItem = target.Index;
			}
			splitSource = null;
			Recipe.FindRecipes();
		}
		finally {
			Main.mouseLeft = oldLeft; Main.mouseLeftRelease = oldRelease;
			Main.cursorOverride = oldOverride;
		}
	}
}
