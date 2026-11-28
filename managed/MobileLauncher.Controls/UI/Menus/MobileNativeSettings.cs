using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace MobileJoystick;

// These two tML menu integrations have no public enumeration/cycling API.
// Use the installed loader's own collections and callback so other mods keep
// their server permissions, custom config editors and boss-bar selection.
internal static class MobileNativeSettings
{
	private static readonly FieldInfo ConfigsField = typeof(ConfigManager).GetField("Configs", BindingFlags.Static | BindingFlags.NonPublic);
	private static readonly MethodInfo BossMenu = typeof(BossBarLoader).GetMethod("InsertMenu", BindingFlags.Static | BindingFlags.NonPublic);

	internal static IEnumerable<ModConfig> Configs()
	{
		var all = (IDictionary<Mod, List<ModConfig>>)ConfigsField.GetValue(null);
		foreach (var list in all.Values) foreach (ModConfig config in list) yield return config;
	}

	internal static string BossBarLabel()
	{
		object[] arguments = { null };
		return (string)BossMenu.Invoke(null, arguments);
	}

	internal static void CycleBossBar()
	{
		object[] arguments = { null };
		BossMenu.Invoke(null, arguments);
		((Action)arguments[0])();
	}

	internal static void CycleResolution()
	{
		var modes = new List<Point>();
		for (int i = 0; i < Main.numDisplayModes; i++) {
			Point mode = new(Main.displayWidth[i], Main.displayHeight[i]);
			if (!modes.Contains(mode)) modes.Add(mode);
		}
		// Android commonly reports only its physical mode. Offer render sizes
		// with the same aspect ratio, staying above Terraria's minimum dimensions.
		if (modes.Count <= 1) {
			Point native = modes.Count == 1 ? modes[0] : new(Main.screenWidth, Main.screenHeight);
			foreach (Point mode in RenderResolutions(native.X, native.Y)) if (!modes.Contains(mode)) modes.Add(mode);
		}
		if (modes.Count == 0) return;
		int selected = modes.IndexOf(new Point(Main.PendingResolutionWidth, Main.PendingResolutionHeight));
		Point next = modes[(selected + 1) % modes.Count];
		Main.SetResolution(next.X, next.Y);
		Main.PendingResolutionWidth = Main.screenWidth;
		Main.PendingResolutionHeight = Main.screenHeight;
	}
	internal static IEnumerable<Point> RenderResolutions(int width, int height)
	{
		foreach (float scale in new[] { 1f, .85f, .75f, 2f / 3f, .5f }) {
			int w = (int)(width * scale) / 2 * 2, h = (int)(height * scale) / 2 * 2;
			if (w >= Main.minScreenW && h >= Main.minScreenH) yield return new Point(w, h);
		}
	}

	internal static float CursorComponent(bool border, int component)
	{
		var hsl = (border ? Main.mouseBorderColorSlider : Main.mouseColorSlider).GetHSLVector();
		return component == 0 ? hsl.X : component == 1 ? hsl.Y : hsl.Z;
	}

	internal static void SetCursorComponent(bool border, int component, float value)
	{
		var color = border ? Main.mouseBorderColorSlider : Main.mouseColorSlider;
		var hsl = color.GetHSLVector();
		if (component == 0) hsl.X=value;
		else if (component == 1) hsl.Y=value;
		else hsl.Z=value;
		color.SetHSL(hsl);
		color.ApplyToMainLegacyBars();
		if (border) Main.MouseBorderColor=color.GetColor();
		else Main.mouseColor=color.GetColor();
	}
}
