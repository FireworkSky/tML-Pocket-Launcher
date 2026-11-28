using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameInput;

namespace MobileJoystick;

internal sealed class ExternalUiLayoutAdapter
{
	internal string Id { get; }
	internal string Name { get; }
	internal Func<bool> IsVisible { get; }
	internal Func<Rectangle> GetBounds { get; }
	internal Action<Vector2> SetCenter { get; }
	internal Func<float> GetScale { get; }
	internal Action<float> SetScale { get; }
	internal Action Save { get; }
	internal Action<bool> SetEditing { get; }
	private readonly Action reset;
	internal Vector2 DefaultCenter { get; set; }
	internal float DefaultScale { get; set; }
	internal bool CanScale => GetScale != null && SetScale != null;

	internal ExternalUiLayoutAdapter(string id, string name, Func<bool> isVisible, Func<Rectangle> getBounds,
		Action<Vector2> setCenter, Func<float> getScale, Action<float> setScale, Action save, Action<bool> setEditing, Action reset)
	{
		Id = id; Name = name; IsVisible = isVisible; GetBounds = getBounds;
		SetCenter = setCenter; GetScale = getScale; SetScale = setScale; Save = save; SetEditing = setEditing;
		this.reset = reset;
		DefaultCenter = Bounds.Center.ToVector2();
		DefaultScale = Scale;
	}

	internal Rectangle Bounds
	{
		get { try { return GetBounds(); } catch { return Rectangle.Empty; } }
	}

	internal bool Visible
	{
		get { try { return IsVisible() && !Bounds.IsEmpty; } catch { return false; } }
	}

	internal float Scale
	{
		get {
			if (!CanScale) return 1f;
			try { return Math.Clamp(GetScale(), .5f, 2f); } catch { return 1f; }
		}
	}

	internal void Move(Vector2 center, bool clampToScreen = true)
	{
		Rectangle bounds = Bounds;
		if (bounds.IsEmpty) return;
		Vector2 screen = new(Math.Max(1, PlayerInput.RealScreenWidth), Math.Max(1, PlayerInput.RealScreenHeight));
		Vector2 half = Vector2.Min(bounds.Size(), screen) * .5f;
		Vector2 clamped = clampToScreen ? Vector2.Clamp(center, half, screen - half) : center;
		try { SetCenter(clamped); } catch { }
	}

	internal void Resize(float scale)
	{
		if (CanScale) { try { SetScale(Math.Clamp(scale, .5f, 2f)); } catch { } }
	}

	internal void RestoreDefault()
	{
		if (reset != null) { reset(); return; }
		Move(DefaultCenter);
		Resize(DefaultScale);
	}

	internal void CaptureDefault()
	{
		DefaultCenter = Bounds.Center.ToVector2();
		DefaultScale = Scale;
	}
}

internal static class ExternalUiLayoutRegistry
{
	private static readonly Dictionary<string, ExternalUiLayoutAdapter> adapters = new(StringComparer.OrdinalIgnoreCase);
	internal static IReadOnlyList<ExternalUiLayoutAdapter> Adapters => adapters.Values.OrderBy(adapter => adapter.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
	internal static IEnumerable<ExternalUiLayoutAdapter> Registered => adapters.Values;

	internal static bool Register(string id, string name, Func<bool> isVisible, Func<Rectangle> getBounds,
		Action<Vector2> setCenter, Func<float> getScale, Action<float> setScale, Action save, Action<bool> setEditing, Action reset = null)
	{
		if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || string.IsNullOrWhiteSpace(name) || name.Length > 128
			|| isVisible == null || getBounds == null || setCenter == null || save == null
			|| (getScale == null) != (setScale == null) || adapters.ContainsKey(id)) return false;
		var adapter = new ExternalUiLayoutAdapter(id, name, isVisible, getBounds, setCenter, getScale, setScale, save, setEditing, reset);
		adapters.Add(id, adapter);
		return true;
	}

	internal static ExternalUiLayoutAdapter Find(string id)
		=> id != null && adapters.TryGetValue(id, out ExternalUiLayoutAdapter adapter) ? adapter : null;

	internal static void SetEditing(bool editing)
	{
		foreach (ExternalUiLayoutAdapter adapter in adapters.Values) {
			try { adapter.SetEditing?.Invoke(editing); } catch { }
		}
	}

	internal static void Clear() => adapters.Clear();
}
