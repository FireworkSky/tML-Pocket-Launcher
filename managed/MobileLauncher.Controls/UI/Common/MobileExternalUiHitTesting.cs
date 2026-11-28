using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;

namespace MobileJoystick;

public sealed partial class MobileExternalUiSystem
{
	private readonly record struct DrawnInterface(UserInterface Interface, UIState State, float Scale, string LayerName)
	{
		internal bool Contains(Vector2 screenPoint) =>
			ReferenceEquals(Interface.CurrentState, State)
			&& State.GetElementAt(screenPoint / Scale) is UIElement element && element != State
			&& !IsViewportContainer(element, State);
	}

	private static List<DrawnInterface> visibleInterfaces = new();
	private static List<DrawnInterface> drawingInterfaces = new();
	private static List<(UIElement Root, float Scale, string Name)> visibleImprovePanels = new(), drawingImprovePanels = new();
	private static List<(object Ui, float Scale, string Name)> visibleSilkyPanels = new(), drawingSilkyPanels = new();
	private static string drawingUiLayer;
	private static Type improvePanelType, silkyLayerType;
	private static MethodInfo silkyGetElement;
	private static PropertyInfo improveEnabled, improveNotSelectable;
	private static PropertyInfo silkyUi, silkyRoot, silkyEnabled, silkyInteractable;

	private static void FindForeignPanelTypes()
	{
		FindLegacyViews();
		if (ModLoader.TryGetMod("ImproveGame", out Mod improve)) {
			improvePanelType = improve.Code.GetType("ImproveGame.UIFramework.BaseViews.BaseBody");
			improveEnabled = improvePanelType?.GetProperty("Enabled");
			improveNotSelectable = improvePanelType?.GetProperty("IsNotSelectable");
		}
		if (ModLoader.TryGetMod("SilkyUIFramework", out Mod silky)) {
			silkyLayerType = silky.Code.GetType("SilkyUIFramework.SilkyUILayer");
			silkyUi = silkyLayerType?.GetProperty("SilkyUI", BindingFlags.NonPublic | BindingFlags.Instance);
			silkyRoot = silkyUi?.PropertyType.GetProperty("RootNode");
			Type rootType = silkyRoot?.PropertyType;
			FindSilkyBounds(rootType);
			silkyEnabled = rootType?.GetProperty("Enabled");
			silkyInteractable = rootType?.GetProperty("IsInteractable");
			// The framework's public HitTest also changes Terraria's global zoom.
			silkyGetElement = rootType?.GetMethod("GetElementAt", [typeof(Vector2)]);
		}
	}

	private static void LoadForeignInterfaces()
	{
		On_GameInterfaceLayer.Draw += DrawInterfaceLayer;
		On_UserInterface.Draw += RecordDrawnInterface;
		On_UIElement.Draw += RecordImprovePanel;
	}

	private static void UnloadForeignInterfaces()
	{
		if (!Main.dedServ) {
			On_UserInterface.Draw -= RecordDrawnInterface;
			On_GameInterfaceLayer.Draw -= DrawInterfaceLayer;
			On_UIElement.Draw -= RecordImprovePanel;
		}
		improvePanelType = silkyLayerType = null;
		silkyGetElement = null;
		improveEnabled = improveNotSelectable = silkyUi = silkyRoot = silkyEnabled = silkyInteractable = null;
		FindSilkyBounds(null);
		legacyViews.Clear();
		ClearForeignInterfaces();
	}

	public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
	{
		// The inventory draws before most mod panels. It needs the previous
		// completed frame's hit areas while the current frame is being drawn.
		(visibleInterfaces, drawingInterfaces) = (drawingInterfaces, visibleInterfaces);
		drawingInterfaces.Clear();
		(visibleImprovePanels, drawingImprovePanels) = (drawingImprovePanels, visibleImprovePanels);
		drawingImprovePanels.Clear();
		(visibleSilkyPanels, drawingSilkyPanels) = (drawingSilkyPanels, visibleSilkyPanels);
		drawingSilkyPanels.Clear();
		(visibleLegacyBounds, drawingLegacyBounds) = (drawingLegacyBounds, visibleLegacyBounds);
		drawingLegacyBounds.Clear();
	}

	public override void OnWorldUnload() => ClearForeignInterfaces();

	private static void ClearForeignInterfaces()
	{
		visibleInterfaces.Clear();
		drawingInterfaces.Clear();
		visibleImprovePanels.Clear();
		drawingImprovePanels.Clear();
		visibleSilkyPanels.Clear();
		drawingSilkyPanels.Clear();
		visibleLegacyBounds.Clear();
		drawingLegacyBounds.Clear();
	}

	private static bool DrawInterfaceLayer(On_GameInterfaceLayer.orig_Draw orig, GameInterfaceLayer layer)
	{
		string previous = drawingUiLayer;
		drawingUiLayer = layer.Active && layer.ScaleType == InterfaceScaleType.UI && !Main.gameMenu
			&& MobileJoystickConfig.Instance?.EnableMobileControls == true ? layer.Name : null;
		try {
			bool result = orig(layer);
			if (drawingUiLayer != null) RecordLegacyLayer(layer.Name);
			if (drawingUiLayer != null && silkyLayerType?.IsInstanceOfType(layer) == true && silkyUi?.GetValue(layer) is object ui)
				drawingSilkyPanels.Add((ui, Main.UIScale, layer.Name));
			return result;
		}
		finally { drawingUiLayer = previous; }
	}

	private static void RecordDrawnInterface(On_UserInterface.orig_Draw orig, UserInterface ui, SpriteBatch batch, GameTime time)
	{
		float scale = Main.UIScale;
		orig(ui, batch, time);
		if (drawingUiLayer == null || ui.CurrentState is not UIState state || !IsForeignState(state)) return;
		RecordInterface(ui, state, scale);
	}

	private static void RecordInterface(UserInterface ui, UIState state, float scale)
	{
		foreach (var panel in drawingInterfaces)
			if (ReferenceEquals(panel.Interface, ui) && ReferenceEquals(panel.State, state) && panel.LayerName == drawingUiLayer) return;
		drawingInterfaces.Add(new DrawnInterface(ui, state, scale, drawingUiLayer));
	}

	private static bool IsForeignState(UIState state)
	{
		// Boss Cursor draws its arrows through a transparent viewport-sized
		// element. It has no interactive panel and must not own touch input.
		if (state?.GetType().FullName == "BossCursor.UI.UiCursor") return false;
		Assembly assembly = state?.GetType().Assembly;
		return assembly != null && assembly != typeof(UIState).Assembly && assembly != typeof(MobileExternalUiSystem).Assembly;
	}

	private static void RecordImprovePanel(On_UIElement.orig_Draw orig, UIElement element, SpriteBatch batch)
	{
		orig(element, batch);
		if (drawingUiLayer == null) return;
		// Optimized mod callers can inline UserInterface.Draw and skip its detour.
		// The root still draws through UIElement.Draw after UserInterface.Use.
		if (element is UIState state && ReferenceEquals(UserInterface.ActiveInstance?.CurrentState, state) && IsForeignState(state))
			RecordInterface(UserInterface.ActiveInstance, state, Main.UIScale);
		if (improvePanelType?.IsInstanceOfType(element) == true)
			drawingImprovePanels.Add((element, Main.UIScale, drawingUiLayer));
	}

	private static bool HitsForeignInterface(Vector2 point)
	{
		if (Main.gameMenu || Main.mapFullscreen || MobileJoystickConfig.Instance?.EnableMobileControls != true) return false;
		// Check the latest draws as well so a newly opened panel can own input.
		foreach (var panel in drawingInterfaces)
			if (panel.Contains(point)) return true;
		foreach (var panel in visibleInterfaces)
			if (panel.Contains(point)) return true;
		return HitsImprovePanel(drawingImprovePanels, point) || HitsImprovePanel(visibleImprovePanels, point)
			|| HitsSilkyPanel(drawingSilkyPanels, point) || HitsSilkyPanel(visibleSilkyPanels, point) || HitsLegacyView(point);
	}

	private static bool HitsImprovePanel(List<(UIElement Root, float Scale, string Name)> panels, Vector2 point)
	{
		foreach (var (panel, scale, _) in panels) {
			if (improveEnabled?.GetValue(panel) is not true || improveNotSelectable?.GetValue(panel) is not false) continue;
			UIElement hit = panel.GetElementAt(point / scale);
			// ImproveGame's focus predicate reads its mouse-hover state. An
			// independent touch must be hit-tested using its own coordinates.
			if (hit != null && hit != panel && !IsViewportContainer(hit, panel)) return true;
		}
		return false;
	}

	private static bool IsViewportContainer(UIElement element, UIElement root)
	{
		CalculatedStyle bounds = element.GetDimensions(), viewport = root.GetDimensions();
		return viewport.Width > 1 && viewport.Height > 1
			&& bounds.X <= viewport.X + 1 && bounds.Y <= viewport.Y + 1
			&& bounds.X + bounds.Width >= viewport.X + viewport.Width - 1
			&& bounds.Y + bounds.Height >= viewport.Y + viewport.Height - 1;
	}

	private static bool HitsSilkyPanel(List<(object Ui, float Scale, string Name)> panels, Vector2 point)
	{
		foreach (var (ui, scale, _) in panels) {
			object root = silkyRoot?.GetValue(ui);
			if (root != null && silkyEnabled?.GetValue(root) is true && silkyInteractable?.GetValue(root) is true
				&& silkyGetElement?.Invoke(root, [point / scale]) is object hit && !ReferenceEquals(root, hit)) return true;
		}
		return false;
	}
}
