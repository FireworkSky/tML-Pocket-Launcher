using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameInput;
using Terraria.UI;

namespace MobileJoystick;

public sealed partial class MobileExternalUiSystem
{
	private static FieldInfo browserPanel, favoritesPanel, browserConfig, browserInterface;
	private static PropertyInfo browserPosition, browserSize, favoritesPosition;
	private static MethodInfo saveBrowserConfig;
	private static Type browserTextBox;
	private static UIElement subscribedBrowser;
	private static UIElement Panel(FieldInfo field) => field?.GetValue(RecipeBrowserRoot) as UIElement;

	public override void Load()
	{
		if (Main.dedServ) return;
		On_UserInterface.Update += UpdateExternalInterface;
		LoadForeignInterfaces();
	}

	private static void FindBrowserLayout(Assembly code)
	{
		Type ui = code.GetType("RecipeBrowser.RecipeBrowserUI"), config = code.GetType("RecipeBrowser.RecipeBrowserClientConfig");
		browserPanel = ui?.GetField("mainPanel", Flags);
		favoritesPanel = ui?.GetField("favoritePanel", Flags);
		browserInterface = ui?.GetField("userInterface", Flags);
		browserConfig = config?.GetField("Instance", Flags);
		browserPosition = config?.GetProperty("RecipeBrowserPosition", Flags);
		browserSize = config?.GetProperty("RecipeBrowserSize", Flags);
		favoritesPosition = config?.GetProperty("FavoritedRecipePanelPosition", Flags);
		saveBrowserConfig = config?.GetMethod("SaveConfig", Flags);
		browserTextBox = code.GetType("RecipeBrowser.NewUITextBox");
	}

	// All optional mods have created their UI by this stage, regardless of
	// PostSetupContent ordering. Drawing and hit tests share the real UIElement.
	public override void PostAddRecipes()
	{
		RegisterBrowserLayout();
		RegisterBossLogButton();
	}

	private static void RegisterBrowserLayout()
	{
		UIElement root = RecipeBrowserRoot;
		if (root == null) return;
		if (subscribedBrowser != root) {
			if (subscribedBrowser != null) subscribedBrowser.OnLeftClick -= BrowserClicked;
			root.OnLeftClick += BrowserClicked;
			subscribedBrowser = root;
		}
		if (browserConfig?.GetValue(null) == null || saveBrowserConfig == null) return;
		RegisterPanel("RecipeBrowser.MainPanel", "RecipeBrowserPanel", browserPanel, browserPosition);
		RegisterPanel("RecipeBrowser.Favorites", "RecipeBrowserFavorites", favoritesPanel, favoritesPosition);
	}

	private static void RegisterPanel(string id, string label, FieldInfo field, PropertyInfo position)
	{
		if (Panel(field) == null || position?.CanWrite != true) return;
		ExternalUiLayoutRegistry.Register(id, MobileSettingsCatalog.Text(label),
			() => Panel(field)?.Parent == RecipeBrowserRoot, () => PanelBounds(Panel(field)),
			center => MovePanel(Panel(field), center), null, null, () => SavePanel(field, position), LockBrowserInput);
	}

	private static Rectangle PanelBounds(UIElement panel)
	{
		if (panel == null) return Rectangle.Empty;
		CalculatedStyle bounds = panel.GetOuterDimensions();
		float scale = Main.UIScale;
		int x = (int)MathF.Floor(bounds.X * scale), y = (int)MathF.Floor(bounds.Y * scale);
		return new Rectangle(x, y, (int)MathF.Ceiling((bounds.X + bounds.Width) * scale) - x,
			(int)MathF.Ceiling((bounds.Y + bounds.Height) * scale) - y);
	}

	private static void MovePanel(UIElement panel, Vector2 center)
	{
		if (panel == null) return;
		CalculatedStyle bounds = panel.GetOuterDimensions();
		Vector2 delta = center / Main.UIScale - new Vector2(bounds.X + bounds.Width * .5f, bounds.Y + bounds.Height * .5f);
		panel.Left.Set(panel.Left.Pixels + delta.X, panel.Left.Percent);
		panel.Top.Set(panel.Top.Pixels + delta.Y, panel.Top.Percent);
		panel.Recalculate();
	}

	private static void SavePanel(FieldInfo field, PropertyInfo position)
	{
		UIElement panel = Panel(field);
		object config = browserConfig?.GetValue(null);
		if (panel == null || config == null) return;
		position.SetValue(config, new Vector2(panel.Left.Pixels, panel.Top.Pixels));
		if (field == browserPanel && browserSize?.CanWrite == true) {
			CalculatedStyle bounds = panel.GetOuterDimensions();
			browserSize.SetValue(config, new Vector2(bounds.Width, bounds.Height));
		}
		saveBrowserConfig.Invoke(null, null);
	}

	private static void LockBrowserInput(bool editing)
	{
		// Native dragging is performed during drawing, so clearing both latches
		// is required even while its normal UserInterface.Update is suspended.
		foreach (FieldInfo field in new[] { browserPanel, favoritesPanel }) {
			UIElement panel = Panel(field);
			panel?.GetType().GetField("dragging", Flags)?.SetValue(panel, false);
			panel?.GetType().GetField("resizeing", Flags)?.SetValue(panel, false);
		}
	}

	private static void UpdateExternalInterface(On_UserInterface.orig_Update orig, UserInterface self, GameTime time)
	{
		if (Main.ingameOptionsWindow && IsForeignState(self.CurrentState) && MobileMenuSystem.EditingInterfaceLayout) return;
		if (RecipeBrowserRoot is UIElement root && ReferenceEquals(browserInterface?.GetValue(root), self)
			&& (MobileMenuSystem.EditingInterfaceLayout || MobileInventorySystem.HasItemGesture)) return;
		orig(self, time);
	}

	private static void BrowserClicked(UIMouseEvent evt, UIElement listeningElement)
	{
		for (UIElement element = evt.Target; element != null; element = element.Parent) {
			if (browserTextBox?.IsInstanceOfType(element) != true) continue;
			MobileTextInputSystem.FocusNativeField(PanelBounds(element));
			return;
		}
	}

	private static void UnloadBrowserLayout()
	{
		if (!Main.dedServ) On_UserInterface.Update -= UpdateExternalInterface;
		if (subscribedBrowser != null) subscribedBrowser.OnLeftClick -= BrowserClicked;
		subscribedBrowser = null;
		browserPanel = favoritesPanel = browserConfig = browserInterface = null;
		browserPosition = browserSize = favoritesPosition = null;
		saveBrowserConfig = null; browserTextBox = null;
	}
}
