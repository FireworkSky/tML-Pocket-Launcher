using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace MobileJoystick;

public sealed partial class MobileExternalUiSystem
{
	private sealed class LegacyViewApi
	{
		internal string Layer;
		internal PropertyInfo Position, Origin, Width, Height, Visible, OverridesMouse;
		internal FieldInfo Children;
		internal MethodInfo InParent;
		internal Func<IEnumerable<object>> Roots;
	}
	private static readonly List<LegacyViewApi> legacyViews = new();
	private static List<ExternalUiBounds> drawingLegacyBounds = new(), visibleLegacyBounds = new();

	private static void FindLegacyViews()
	{
		legacyViews.Clear();
		if (ModLoader.TryGetMod("HEROsMod", out Mod hero)) {
			Type master = hero.Code.GetType("HEROsMod.UIKit.MasterView");
			FieldInfo root = master?.GetField("masterView", Flags);
			RegisterLegacyView(hero, "HEROsMod.UIKit.UIView", "HerosMod: UI", () => new[] { root?.GetValue(null) });
		}
		if (ModLoader.TryGetMod("CheatSheet", out Mod cheat)) {
			Type view = cheat.Code.GetType("CheatSheet.UI.UIView");
			var roots = new List<FieldInfo>();
			if (view != null) foreach (FieldInfo field in cheat.GetType().GetFields(Flags))
				if (!field.IsStatic && view.IsAssignableFrom(field.FieldType)) roots.Add(field);
			RegisterLegacyView(cheat, "CheatSheet.UI.UIView", "CheatSheet: All Cheat Sheet", () => {
				var result = new List<object>();
				foreach (FieldInfo field in roots) if (field.GetValue(cheat) is object root) result.Add(root);
				Type config = cheat.Code.GetType("CheatSheet.Menus.ConfigurationTool");
				if (config?.GetField("configurationWindow", Flags)?.GetValue(null) is object window) result.Add(window);
				return result;
			});
		}
	}

	private static void RegisterLegacyView(Mod mod, string typeName, string layer, Func<IEnumerable<object>> roots)
	{
		Type type = mod.Code.GetType(typeName);
		if (type == null) return;
		var api = new LegacyViewApi {
			Layer = layer, Roots = roots, Position = type.GetProperty("DrawPosition"), Origin = type.GetProperty("Origin"),
			Width = type.GetProperty("Width"), Height = type.GetProperty("Height"), Visible = type.GetProperty("Visible"),
			OverridesMouse = type.GetProperty("OverridesMouse"), Children = type.GetField("children"),
			InParent = type.GetMethod("InParent", Flags)
		};
		if (api.Position != null && api.Origin != null && api.Width != null && api.Height != null
			&& api.Visible != null && api.OverridesMouse != null && api.Children != null) legacyViews.Add(api);
	}

	private static void RecordLegacyLayer(string name)
	{
		var screen = new Rectangle(0, 0, Terraria.GameInput.PlayerInput.RealScreenWidth, Terraria.GameInput.PlayerInput.RealScreenHeight);
		foreach (var api in legacyViews) {
			if (name != api.Layer) continue;
			foreach (object root in api.Roots()) if (root != null) CollectLegacyView(api, root, name, screen, 0);
		}
	}

	private static void CollectLegacyView(LegacyViewApi api, object view, string name, Rectangle screen, int depth)
	{
		if (depth > 32 || api.Visible.GetValue(view) is not true) return;
		Vector2 position = (Vector2)api.Position.GetValue(view) - (Vector2)api.Origin.GetValue(view);
		float width = (float)api.Width.GetValue(view), height = (float)api.Height.GetValue(view);
		Rectangle bounds = ScreenBounds(position.X, position.Y, width, height, Main.UIScale);
		bool viewport = bounds.Left <= screen.Left + 2 && bounds.Top <= screen.Top + 2
			&& bounds.Right >= screen.Right - 2 && bounds.Bottom >= screen.Bottom - 2;
		if (api.OverridesMouse.GetValue(view) is true && !viewport)
			AddBounds(drawingLegacyBounds, name, bounds, screen);
		if (api.Children.GetValue(view) is not IEnumerable children) return;
		foreach (object child in children) {
			if (child == null || api.InParent?.Invoke(child, null) is false) continue;
			CollectLegacyView(api, child, name, screen, depth + 1);
		}
	}

	private static bool HitsLegacyView(Vector2 point)
	{
		foreach (var panel in drawingLegacyBounds) if (panel.Bounds.Contains(point.ToPoint())) return true;
		foreach (var panel in visibleLegacyBounds) if (panel.Bounds.Contains(point.ToPoint())) return true;
		return false;
	}
}
