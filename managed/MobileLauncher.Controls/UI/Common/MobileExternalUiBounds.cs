using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameInput;
using Terraria.UI;

namespace MobileJoystick;

internal readonly record struct ExternalUiBounds(string Name, Rectangle Bounds, string AdapterId = null);

public sealed partial class MobileExternalUiSystem
{
	private static PropertyInfo silkyChildren, silkyIgnoresMouse, silkyDisablesMouse;
	private static FieldInfo silkyBounds;
	private static PropertyInfo silkyBoundsX, silkyBoundsY, silkyBoundsWidth, silkyBoundsHeight;

	private static void FindSilkyBounds(Type rootType)
	{
		silkyChildren = rootType?.GetProperty("ElementsInOrder", Flags);
		silkyIgnoresMouse = rootType?.GetProperty("IgnoreMouseInteraction");
		silkyDisablesMouse = rootType?.GetProperty("DisableMouseInteraction");
		silkyBounds = rootType?.GetField("Bounds");
		Type boundsType = silkyBounds?.FieldType;
		silkyBoundsX = boundsType?.GetProperty("X");
		silkyBoundsY = boundsType?.GetProperty("Y");
		silkyBoundsWidth = boundsType?.GetProperty("Width");
		silkyBoundsHeight = boundsType?.GetProperty("Height");
	}

	internal static void CollectBounds(List<ExternalUiBounds> result)
	{
		result.Clear();
		if (Main.gameMenu || Main.mapFullscreen || Main.hideUI || MobileJoystickConfig.Instance?.EnableMobileControls != true) return;
		var screen = new Rectangle(0, 0, PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight);
		if (screen.Width <= 0 || screen.Height <= 0) return;

		foreach (var adapter in ExternalUiLayoutRegistry.Registered)
			if (adapter.Visible) AddBounds(result, adapter.Name, adapter.Bounds, screen, adapter.Id);
		foreach (var panel in drawingLegacyBounds) AddBounds(result, panel.Name, panel.Bounds, screen);

		foreach (var panel in drawingInterfaces)
			if (ReferenceEquals(panel.Interface.CurrentState, panel.State))
				CollectTerrariaChildren(result, panel.State, panel.LayerName, panel.Scale, screen);
		foreach (var (root, scale, name) in drawingImprovePanels)
			if (improveEnabled?.GetValue(root) is true && improveNotSelectable?.GetValue(root) is false)
				CollectTerrariaChildren(result, root, name, scale, screen);
		foreach (var (ui, scale, name) in drawingSilkyPanels) {
			object root = silkyRoot?.GetValue(ui);
			if (root != null && silkyEnabled?.GetValue(root) is true && silkyInteractable?.GetValue(root) is true)
				CollectSilkyChildren(result, root, name, scale, screen);
		}
	}

	private static void CollectTerrariaChildren(List<ExternalUiBounds> result, UIElement root, string name,
		float scale, Rectangle screen)
	{
		foreach (UIElement child in root.Children) {
			if (child.IgnoresMouseInteraction) continue;
			CalculatedStyle style = child.GetDimensions();
			Rectangle bounds = ScreenBounds(style.X, style.Y, style.Width, style.Height, scale);
			if (bounds.Contains(screen)) {
				CollectTerrariaChildren(result, child, name, scale, screen);
				continue;
			}
			AddBounds(result, name, bounds, screen);
		}
	}

	private static void CollectSilkyChildren(List<ExternalUiBounds> result, object root, string name, float scale, Rectangle screen)
	{
		if (silkyChildren?.DeclaringType?.IsInstanceOfType(root) != true || silkyChildren.GetValue(root) is not IEnumerable children)
			return;
		foreach (object child in children) {
			if (silkyDisablesMouse?.GetValue(child) is true || silkyBounds?.GetValue(child) is not object box
				|| silkyBoundsX?.GetValue(box) is not float x || silkyBoundsY?.GetValue(box) is not float y
				|| silkyBoundsWidth?.GetValue(box) is not float width || silkyBoundsHeight?.GetValue(box) is not float height)
				continue;
			Rectangle bounds = ScreenBounds(x, y, width, height, scale);
			if (bounds.Contains(screen) || silkyIgnoresMouse?.GetValue(child) is true)
				CollectSilkyChildren(result, child, name, scale, screen);
			else AddBounds(result, name, bounds, screen);
		}
	}

	private static Rectangle ScreenBounds(float x, float y, float width, float height, float scale)
		=> new((int)MathF.Floor(x * scale), (int)MathF.Floor(y * scale),
			(int)MathF.Ceiling((x + width) * scale) - (int)MathF.Floor(x * scale),
			(int)MathF.Ceiling((y + height) * scale) - (int)MathF.Floor(y * scale));

	private static void AddBounds(List<ExternalUiBounds> result, string name, Rectangle bounds, Rectangle screen, string adapterId = null)
	{
		bounds = Rectangle.Intersect(bounds, screen);
		if (bounds.Width <= 1 || bounds.Height <= 1) return;
		foreach (var existing in result)
			if (existing.Bounds.Contains(bounds) && (existing.AdapterId != null || existing.Name == name)) return;
		result.Add(new ExternalUiBounds(name, bounds, adapterId));
	}
}
