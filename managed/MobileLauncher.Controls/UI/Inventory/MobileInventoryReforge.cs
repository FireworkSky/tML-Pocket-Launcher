using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace MobileJoystick;

public partial class MobileInventorySystem
{
	private readonly Item[] reforgeSlot = { new Item() };

	private void CloseReforge()
	{
		Main.InReforgeMenu = false;
		Main.LocalPlayer.dropItemCheck();
		reforgeSlot[0] = Main.reforgeItem;
		CancelInventoryGesture();
		Recipe.FindRecipes();
	}

	private void DrawReforge()
	{
		if (TextButton(layout.RightTabDefault(0), "×")) {
			Main.LocalPlayer.SetTalkNPC(-1);
			CloseReforge();
			return;
		}
		reforgeSlot[0] = Main.reforgeItem;
		Slot(reforgeSlot, ItemSlot.Context.PrefixItem, 0, layout.Right(90, 62, 62, 62));
		Item item = Main.reforgeItem = reforgeSlot[0];
		Rectangle info = layout.Right(54, 142, 320, 44);
		if (item.IsAir) {
			Text(Lang.inter[20].Value, info, .8f);
			return;
		}
		Player player = Main.LocalPlayer;
		int price = item.value * item.stack;
		bool discount = true;
		if (ItemLoader.ReforgePrice(item, ref price, ref discount)) {
			if (discount && player.discountAvailable) price = (int)(price * .8);
			price = (int)(price * player.currentShoppingSettings.PriceAdjustment) / 3;
		}
		Text(Lang.inter[46].Value + " " + CoinPrice(Math.Max(1, price)), info);
		if (!TextButton(layout.Right(192, 62, 128, 62), Lang.inter[19].Value)
			|| !player.CanAfford(price) || !ItemLoader.CanReforge(item) || !player.BuyItem(price)) return;
		ItemLoader.PreReforge(item);
		item.ResetPrefix();
		item.Prefix(-2);
		item.Center = player.Center;
		ItemLoader.PostReforge(item);
		PopupText.NewText(PopupTextContext.ItemReforge, item, item.stack, noStack: true);
		SoundEngine.PlaySound(SoundID.Item37);
	}
}
