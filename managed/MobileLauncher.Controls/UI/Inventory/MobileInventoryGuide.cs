using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.UI;

namespace MobileJoystick;

public partial class MobileInventorySystem
{
	private bool guideSession, guideQuery;
	private int pageBeforeGuide;
	private readonly Item[] guideSlot = { new Item() };
	private static bool TalkingToGuide => Main.LocalPlayer.talkNPC >= 0 && Main.LocalPlayer.talkNPC < Main.maxNPCs
		&& Main.npc[Main.LocalPlayer.talkNPC] is { active: true, type: NPCID.Guide };
	private Rectangle GuideModeButton => layout.CraftAt(76, 354, 218, 36);

	private void UpdateGuideSession()
	{
		// Query mode shares the native slot. Vanilla may already have returned it
		// after closing the bag/changing NPCs; never resurrect our old reference.
		if (guideSession && guideQuery) guideSlot[0] = Main.guideItem;
		bool available = Enabled && Main.playerInventory && TalkingToGuide && Main.InGuideCraftMenu
			&& !Main.InReforgeMenu && Main.npcShop == 0 && Main.LocalPlayer.chest == -1;
		if (!available) {
			if (guideSession) CloseGuideSession();
			return;
		}
		if (guideSession) return;
		guideSession = guideQuery = true;
		guideSlot[0] = Main.guideItem;
		pageBeforeGuide = rightPage;
		rightPage = 0;
		nativeView = false;
		recipeStart = 0;
		selectedRecipe = -1;
		CancelInventoryGesture();
		Recipe.FindRecipes();
	}

	private void CloseGuideSession()
	{
		// The native return routine retains item mod data, handles full bags and
		// synchronizes any overflow. Do not leave a query item filtering recipes
		// after the Guide has closed or the player has changed NPCs.
		if (!guideQuery && !guideSlot[0].IsAir) Main.guideItem = guideSlot[0];
		guideSlot[0] = new Item();
		guideSession = guideQuery = false;
		rightPage = pageBeforeGuide;
		Main.InGuideCraftMenu = false;
		Main.LocalPlayer.dropItemCheck();
		CancelInventoryGesture();
		MobileInventoryNative.ResetCrafting();
		Recipe.FindRecipes();
	}

	private void DrawGuideMode()
	{
		if (TextButton(layout.RightTabDefault(0), "×")) {
			Main.LocalPlayer.SetTalkNPC(-1);
			CloseGuideSession();
			return;
		}
		if (TextButton(GuideModeButton, T(guideQuery ? "SwitchToCrafting" : "SwitchToGuideQuery"), guideQuery)) SwitchGuideMode();
	}

	private void SwitchGuideMode()
	{
		CloseRecipeDetails();
		CancelInventoryGesture();
		guideQuery = !guideQuery;
		Main.guideItem = guideQuery ? guideSlot[0] : new Item();
		recipeStart = 0;
		selectedRecipe = heldCraftRecipe = -1;
		MobileInventoryNative.ResetCrafting();
		Recipe.FindRecipes();
	}

	private void DrawGuideQuerySlot()
	{
		guideSlot[0] = Main.guideItem;
		Slot(guideSlot, ItemSlot.Context.GuideItem, 0, layout.CraftAction);
		SyncGuideQuerySlot();
		Text(T("GuideQuery"), layout.CraftAt(4, 324, 62, 24), .65f);
		if (guideSlot[0].IsAir) Text(T("PlaceGuideQueryItem"), layout.CraftAt(78, 108, 344, 56), .75f);
	}

	private void SyncGuideQuerySlot()
	{
		if (guideSession && guideQuery && !ReferenceEquals(Main.guideItem, guideSlot[0])) {
			Main.guideItem = guideSlot[0];
			Recipe.FindRecipes();
		}
	}
}
