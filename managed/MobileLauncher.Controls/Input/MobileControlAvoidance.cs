using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MobileJoystick;

internal static class MobileControlAvoidance
{
	private static readonly List<ExternalUiBounds> panels = new();

	internal static void Update() => MobileExternalUiSystem.CollectBounds(panels);
	internal static void Clear() => panels.Clear();

	internal static bool Obscures(MobileControlButton button)
	{
		foreach (var panel in panels)
			if (Overlaps(button, panel.Bounds)) return true;
		return false;
	}

	internal static bool Obscures(Rectangle bounds)
	{
		foreach (var panel in panels)
			if (bounds.Intersects(panel.Bounds)) return true;
		return false;
	}

	internal static bool Obscures(MobileControlStick stick)
	{
		foreach (var panel in panels)
			if (CircleOverlaps(stick.Center, stick.Radius, panel.Bounds)) return true;
		return false;
	}

	internal static Rectangle Bounds(MobileControlButton button)
	{
		if (button.Launcher == null) return button.Bounds;
		float radians = MathHelper.ToRadians(button.Launcher.Number("rotation"));
		float sin = MathF.Abs(MathF.Sin(radians)), cos = MathF.Abs(MathF.Cos(radians));
		Vector2 half = button.Dimensions * .5f;
		return Bounds(button.Center, new Vector2(cos * half.X + sin * half.Y, sin * half.X + cos * half.Y));
	}

	internal static Rectangle Bounds(Vector2 center, Vector2 halfSize)
	{
		int left = (int)MathF.Floor(center.X - halfSize.X), top = (int)MathF.Floor(center.Y - halfSize.Y);
		return new Rectangle(left, top, (int)MathF.Ceiling(center.X + halfSize.X) - left,
			(int)MathF.Ceiling(center.Y + halfSize.Y) - top);
	}

	internal static bool Overlaps(MobileControlButton button, Rectangle panel)
	{
		if (button.Launcher != null || button.Action is MobileControlAction.Inventory or MobileControlAction.Chat
			or MobileControlAction.Map or MobileControlAction.MapStyle or MobileControlAction.Settings)
			return Bounds(button).Intersects(panel);
		return CircleOverlaps(button.Center, button.Radius, panel);
	}

	internal static bool CircleOverlaps(Vector2 center, float radius, Rectangle panel)
	{
		Vector2 nearest = Vector2.Clamp(center, new Vector2(panel.Left, panel.Top), new Vector2(panel.Right, panel.Bottom));
		return radius > 0f && Vector2.DistanceSquared(nearest, center) < radius * radius;
	}
}
