using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;

namespace MobileJoystick;

// Optional adapters keep foreign UI input ahead of the inventory underneath.
// Recipe Browser's query slot is a ghost reference, not a destination container.
public sealed partial class MobileExternalUiSystem : MobileSystem
{
	private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
	private static FieldInfo recipeBrowserInstance;
	private static Type querySlotType;
	private static MethodInfo replaceQuery;
	private static PropertyInfo browserVisible;
	private static UIElement RecipeBrowserRoot => recipeBrowserInstance?.GetValue(null) as UIElement;
	internal static bool HasVisibleBrowser => RecipeBrowserRoot is UIElement root && browserVisible?.GetValue(root) is true;

	public override void PostSetupContent()
	{
		if (Main.dedServ) return;
		FindForeignPanelTypes();
		if (!ModLoader.TryGetMod("RecipeBrowser", out Mod browser)) return;
		recipeBrowserInstance = browser.Code.GetType("RecipeBrowser.RecipeBrowserUI")?.GetField("instance", Flags);
		browserVisible = browser.Code.GetType("RecipeBrowser.RecipeBrowserUI")?.GetProperty("ShowRecipeBrowser", Flags);
		querySlotType = browser.Code.GetType("RecipeBrowser.UIElements.UIQueryItemSlot");
		replaceQuery = querySlotType?.GetMethod("ReplaceWithFake", Flags);
		FindBrowserLayout(browser.Code);
	}

	private static UIElement BrowserElement(Vector2 point)
	{
		UIElement root = RecipeBrowserRoot;
		UIElement element = root?.GetElementAt(point / Main.UIScale);
		return element == root ? null : element;
	}

	internal static bool BlocksInventory(Vector2 point) => BrowserElement(point) != null || HitsForeignInterface(point);

	internal static Rectangle QueryDropBounds(Vector2 point) => PanelBounds(FindQueryDropTarget(point));

	internal static bool TryQueryItem(Vector2 point, Item item)
	{
		if (item == null || item.IsAir) return false;
		UIElement target = FindQueryDropTarget(point);
		if (target == null) return false;
		// The browser returns any previous real item and updates its own recipe
		// filters/history. The dragged stack stays in its original inventory slot.
		replaceQuery.Invoke(target, new object[] { item.type });
		Main.LocalPlayer.mouseInterface = true;
		return true;
	}

	private static UIElement FindQueryDropTarget(Vector2 screenPoint)
	{
		UIElement root = RecipeBrowserRoot;
		if (root == null || browserVisible?.GetValue(root) is not true || querySlotType == null || replaceQuery == null) return null;
		Vector2 point = screenPoint / Main.UIScale;
		UIElement hovered = root.GetElementAt(point);
		UIElement target = QuerySlotAncestor(hovered);
		if (target != null) return target;

		float distanceSquared = 16 * 16;
		FindNearbyQuerySlot(hovered ?? root, root, point, ref target, ref distanceSquared);
		return target;
	}

	private static UIElement QuerySlotAncestor(UIElement element)
	{
		while (element != null && !querySlotType.IsInstanceOfType(element)) element = element.Parent;
		return element;
	}

	private static void FindNearbyQuerySlot(UIElement parent, UIElement root, Vector2 point, ref UIElement target, ref float distanceSquared)
	{
		foreach (UIElement child in parent.Children) {
			if (child.IgnoresMouseInteraction) continue;
			CalculatedStyle bounds = child.GetDimensions();
			if (bounds.Width <= 1 || bounds.Height <= 1) continue;
			Vector2 topLeft = bounds.Position(), bottomRight = topLeft + new Vector2(bounds.Width, bounds.Height);
			Vector2 nearest = Vector2.Clamp(point, topLeft, bottomRight);
			float distance = Vector2.DistanceSquared(point, nearest);
			if (distance > distanceSquared) continue;
			if (!querySlotType.IsInstanceOfType(child)) {
				FindNearbyQuerySlot(child, root, point, ref target, ref distanceSquared);
				continue;
			}
			Vector2 inset = new(.5f);
			Vector2 inside = Vector2.Clamp(point, topLeft + inset, bottomRight - inset);
			if (QuerySlotAncestor(root.GetElementAt(inside)) != child) continue;
			target = child;
			distanceSquared = distance;
		}
	}

	public override void Unload() {
		UnloadBrowserLayout();
		UnloadForeignInterfaces();
		recipeBrowserInstance = null; querySlotType = null; replaceQuery = null; browserVisible = null;
	}
}
