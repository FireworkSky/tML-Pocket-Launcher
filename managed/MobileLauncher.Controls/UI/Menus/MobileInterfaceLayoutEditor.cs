using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MobileJoystick;

internal sealed class MobileInterfaceLayoutEditor
{
	private readonly List<MobileInterfaceLayoutProfile> undo = new();
	private readonly List<MobileInterfaceLayoutProfile> redo = new();
	private const MobileInterfaceRegion ModLayoutRegion = MobileInterfaceRegion.CalamityDifficulty;
	private readonly record struct ExternalEdit(MobileInterfaceRegionLayout ModLayout, Dictionary<string, (Vector2 Center, float Scale)> Panels);
	private readonly List<ExternalEdit> externalUndo = new(), externalRedo = new();
	private readonly List<ExternalUiBounds> pausePanels = new();
	private readonly List<ExternalUiBounds> openingPanels = new();
	private readonly List<ExternalUiBounds> livePanels = new();
	private ExternalEdit externalOpening;
	private bool externalSaveAttempted;
	private const int HistoryLimit = 32;

	public MobileInterfaceLayoutProfile Draft { get; private set; }
	public MobileInterfaceLayoutProfile Opening { get; private set; }
	public MobileInterfaceRegion Selected { get; set; } = MobileInterfaceRegion.Hotbar;
	public bool InventoryView { get; set; }
	public bool ExternalView { get; set; }
	public string SelectedExternalUiId { get; set; }
	public bool IsOpen { get; private set; }
	public Rectangle HotbarEditorBounds { get; set; }
	public bool CanUndo => ExternalView ? externalUndo.Count > 0 : undo.Count > 0;
	public bool CanRedo => ExternalView ? externalRedo.Count > 0 : redo.Count > 0;

	internal void CaptureBeforePause() => MobileExternalUiSystem.CollectBounds(pausePanels);

	public void Open(MobileInterfaceLayoutProfile profile)
	{
		Opening = profile?.Clone() ?? new MobileInterfaceLayoutProfile();
		Draft = Opening.Clone();
		Selected = MobileInterfaceRegion.Hotbar;
		InventoryView = false;
		ExternalView = false;
		SelectedExternalUiId = null;
		HotbarEditorBounds = Rectangle.Empty;
		undo.Clear(); redo.Clear();
		externalUndo.Clear(); externalRedo.Clear();
		externalSaveAttempted = false;
		externalOpening = CaptureExternalEdit();
		MobileExternalUiSystem.CollectBounds(openingPanels);
		AppendMissingPanels(openingPanels, pausePanels);
		IsOpen = true;
		ExternalUiLayoutRegistry.SetEditing(true);
	}

	public void Close(bool keepExternalChanges = false)
	{
		if (!keepExternalChanges) {
			RestoreExternalEdit(externalOpening, restoreProfile: false);
			if (externalSaveAttempted) {
				foreach (var adapter in ExternalUiLayoutRegistry.Registered) {
					try { adapter.Save(); }
					catch (Exception error) { MobileControlsSession.Logger.Warn($"Could not restore {adapter.Name} layout", error); }
				}
			}
		}
		ExternalUiLayoutRegistry.SetEditing(false);
		IsOpen = false;
		Draft = null;
		Opening = null;
		undo.Clear(); redo.Clear();
		externalUndo.Clear(); externalRedo.Clear();
		externalOpening = default;
		openingPanels.Clear();
		livePanels.Clear();
	}

	public MobileInterfaceRegionLayout Resolve(MobileInterfaceRegion region, int width, int height)
	{
		MobileInterfaceRegionLayout entry = Draft?.Get(region) ?? new MobileInterfaceRegionLayout();
		Rectangle bounds = DefaultBounds(region, width, height);
		return new MobileInterfaceRegionLayout {
			CenterX = float.IsNaN(entry.CenterX) ? bounds.Center.X / Math.Max(1f, width) : entry.CenterX,
			CenterY = float.IsNaN(entry.CenterY) ? bounds.Center.Y / Math.Max(1f, height) : entry.CenterY,
			Scale = Math.Clamp(entry.Scale, .5f, MaximumScale(region, width, height))
		};
	}

	public float MaximumScale(MobileInterfaceRegion region, int width, int height)
		=> MobileInterfaceLayoutProfile.MaximumScaleFor(DefaultBounds(region, width, height), SafeBoundsFor(region, width, height));

	public Rectangle Bounds(MobileInterfaceRegion region, int width, int height)
	{
		Rectangle defaults = DefaultBounds(region, width, height);
		return MobileInterfaceLayoutProfile.Transform(Resolve(region, width, height), defaults, defaults,
			new Rectangle(0, 0, width, height), SafeBoundsFor(region, width, height));
	}

	public Rectangle SafeBoundsFor(MobileInterfaceRegion region, int width, int height)
		=> region == MobileInterfaceRegion.Hotbar && !HotbarEditorBounds.IsEmpty
			? HotbarEditorBounds : DefaultSafeBounds(region, width, height);

	public void Move(MobileInterfaceRegion region, Vector2 delta, int width, int height, bool record = true)
	{
		if (record) PushUndo();
		MobileInterfaceRegionLayout entry = Resolve(region, width, height);
		Rectangle defaults = DefaultBounds(region, width, height);
		Rectangle safe = SafeBoundsFor(region, width, height);
		float halfWidth = Math.Min(defaults.Width * entry.Scale * .5f, safe.Width * .5f);
		float halfHeight = Math.Min(defaults.Height * entry.Scale * .5f, safe.Height * .5f);
		// Keep fractional contact movement. Feeding the rounded render rectangle
		// back into the gesture discards small upward deltas on high-DPI phones.
		float cx = Math.Clamp(entry.CenterX * width, safe.Left + halfWidth, safe.Right - halfWidth);
		float cy = Math.Clamp(entry.CenterY * height, safe.Top + halfHeight, safe.Bottom - halfHeight);
		cx = Math.Clamp(cx + delta.X, safe.Left + halfWidth, safe.Right - halfWidth);
		cy = Math.Clamp(cy + delta.Y, safe.Top + halfHeight, safe.Bottom - halfHeight);
		Draft.Set(region, new MobileInterfaceRegionLayout { CenterX = cx / Math.Max(1f, width), CenterY = cy / Math.Max(1f, height), Scale = entry.Scale });
	}

	public void BeginGesture()
	{
		if (ExternalView) PushExternalUndo();
		else PushUndo();
	}

	public void SetScale(MobileInterfaceRegion region, float scale, int width, int height, bool record = true)
	{
		if (record) PushUndo();
		MobileInterfaceRegionLayout entry = Resolve(region, width, height);
		Rectangle bounds = DefaultBounds(region, width, height);
		Draft.Set(region, new MobileInterfaceRegionLayout { CenterX = entry.CenterX, CenterY = entry.CenterY, Scale = Math.Clamp(scale, .5f, MaximumScale(region, width, height)) });
		ClampToViewport(region, bounds, width, height);
	}

	public void ResetSelected(int width, int height)
	{
		PushUndo();
		Draft.Reset(Selected);
	}

	public void RestoreDefaults()
	{
		PushUndo();
		RestoreBaseLayout(new MobileInterfaceLayoutProfile());
	}

	public void Undo()
	{
		if (!CanUndo) return;
		if (ExternalView) {
			externalRedo.Add(CaptureExternalEdit());
			RestoreExternalEdit(externalUndo[^1]);
			externalUndo.RemoveAt(externalUndo.Count - 1);
			return;
		}
		redo.Add(Draft.Clone());
		RestoreBaseLayout(undo[^1]);
		undo.RemoveAt(undo.Count - 1);
	}

	public void Redo()
	{
		if (!CanRedo) return;
		if (ExternalView) {
			externalUndo.Add(CaptureExternalEdit());
			RestoreExternalEdit(externalRedo[^1]);
			externalRedo.RemoveAt(externalRedo.Count - 1);
			return;
		}
		undo.Add(Draft.Clone());
		RestoreBaseLayout(redo[^1]);
		redo.RemoveAt(redo.Count - 1);
	}

	public MobileInterfaceLayoutProfile Apply()
	{
		Draft.Validate();
		externalSaveAttempted = true;
		foreach (ExternalUiLayoutAdapter adapter in ExternalUiLayoutRegistry.Adapters) adapter.Save();
		return Draft.Clone();
	}

	public void CaptureExternalSelection(string id)
		=> SelectedExternalUiId = ExternalUiLayoutRegistry.Find(id)?.Id;

	public ExternalUiLayoutAdapter SelectedExternalUi
		=> ExternalUiLayoutRegistry.Find(SelectedExternalUiId);

	public void ResetSelectedExternalUi()
	{
		ExternalUiLayoutAdapter adapter = SelectedExternalUi;
		if (adapter == null) return;
		PushExternalUndo();
		adapter.RestoreDefault();
	}

	public void ResetExternalUiDefaults()
	{
		if (ExternalUiLayoutRegistry.Adapters.Count == 0) return;
		PushExternalUndo();
		foreach (ExternalUiLayoutAdapter adapter in ExternalUiLayoutRegistry.Adapters) adapter.RestoreDefault();
	}

	internal IReadOnlyList<ExternalUiBounds> PreviewPanels()
	{
		MobileExternalUiSystem.CollectBounds(livePanels);
		AppendMissingPanels(livePanels, openingPanels);
		foreach (var panel in pausePanels) {
			if (panel.AdapterId == null || livePanels.Exists(current => current.AdapterId == panel.AdapterId)) continue;
			var adapter = ExternalUiLayoutRegistry.Find(panel.AdapterId);
			if (adapter != null && !adapter.Bounds.IsEmpty)
				livePanels.Add(panel with { Bounds = adapter.Bounds });
		}
		return livePanels;
	}

	internal bool WasPanelVisible(string id)
		=> pausePanels.Exists(panel => panel.AdapterId == id) || openingPanels.Exists(panel => panel.AdapterId == id);

	private static void AppendMissingPanels(List<ExternalUiBounds> panels, List<ExternalUiBounds> snapshots)
	{
		int liveCount = panels.Count;
		foreach (var snapshot in snapshots)
			if (snapshot.AdapterId == null && panels.FindIndex(0, liveCount, panel => panel.Name == snapshot.Name) < 0)
				panels.Add(snapshot);
	}

	private ExternalEdit CaptureExternalEdit()
	{
		var panels = new Dictionary<string, (Vector2 Center, float Scale)>(StringComparer.OrdinalIgnoreCase);
		foreach (var adapter in ExternalUiLayoutRegistry.Registered) {
			Rectangle bounds = adapter.Bounds;
			if (!bounds.IsEmpty) panels[adapter.Id] = (new Vector2(bounds.Center.X, bounds.Center.Y), adapter.Scale);
		}
		return new ExternalEdit(Draft?.Has(ModLayoutRegion) == true ? Draft.Get(ModLayoutRegion) : null, panels);
	}

	private void RestoreExternalEdit(ExternalEdit edit, bool restoreProfile = true)
	{
		if (edit.Panels == null) return;
		foreach (var adapter in ExternalUiLayoutRegistry.Registered) {
			if (!edit.Panels.TryGetValue(adapter.Id, out var state)) continue;
			adapter.Resize(state.Scale);
			adapter.Move(state.Center, clampToScreen: false);
		}
		if (!restoreProfile) return;
		// Other tabs can change after an external edit. Restore only mod-owned regions here.
		if (edit.ModLayout != null) Draft.Set(ModLayoutRegion, edit.ModLayout);
		else Draft.Reset(ModLayoutRegion);
	}

	private void RestoreBaseLayout(MobileInterfaceLayoutProfile profile)
	{
		if (Draft.Has(ModLayoutRegion)) profile.Set(ModLayoutRegion, Draft.Get(ModLayoutRegion));
		else profile.Reset(ModLayoutRegion);
		Draft = profile;
	}

	private void PushExternalUndo()
	{
		externalUndo.Add(CaptureExternalEdit());
		if (externalUndo.Count > HistoryLimit) externalUndo.RemoveAt(0);
		externalRedo.Clear();
	}

	private void ClampToViewport(MobileInterfaceRegion region, Rectangle defaults, int width, int height)
	{
		Rectangle bounds = Bounds(region, width, height);
		MobileInterfaceRegionLayout entry = Resolve(region, width, height);
		Rectangle safe = SafeBoundsFor(region, width, height);
		float halfWidth = Math.Min(defaults.Width * entry.Scale * .5f, safe.Width * .5f);
		float halfHeight = Math.Min(defaults.Height * entry.Scale * .5f, safe.Height * .5f);
		float cx = Math.Clamp(bounds.Center.X, safe.Left + halfWidth, safe.Right - halfWidth);
		float cy = Math.Clamp(bounds.Center.Y, safe.Top + halfHeight, safe.Bottom - halfHeight);
		Draft.Set(region, new MobileInterfaceRegionLayout { CenterX = cx / Math.Max(1f, width), CenterY = cy / Math.Max(1f, height), Scale = entry.Scale });
	}

	private void PushUndo()
	{
		if (Draft == null) return;
		undo.Add(Draft.Clone());
		if (undo.Count > HistoryLimit) undo.RemoveAt(0);
		redo.Clear();
	}

	internal static Rectangle DefaultBounds(MobileInterfaceRegion region, int width, int height)
	{
		if (region == MobileInterfaceRegion.Hotbar) {
			MobileHotbarLayout hotbar = MobileJoystickLayout.CreateHotbarLayout(MobileJoystickConfig.Instance, width, height, false);
			Rectangle buttons = Rectangle.Union(hotbar.InventoryButton, hotbar.SettingsButton);
			return Rectangle.Union(buttons, Rectangle.Union(hotbar.GetSlot(0), hotbar.GetSlot(MobileHotbarLayout.SlotCount - 1)));
		}
		var inventory = new MobileInventoryLayout(width, height);
		return inventory.RegionBounds(region);
	}

	internal static Rectangle DefaultSafeBounds(MobileInterfaceRegion region, int width, int height)
	{
		if (region != MobileInterfaceRegion.Hotbar) return new MobileInventoryLayout(width, height).SafeViewport;
		float scale = MobileJoystickLayout.GetControlScale(MobileJoystickConfig.Instance, width, height);
		int inset = Math.Max(4, (int)((MobileJoystickConfig.Instance?.EdgePadding ?? 18) * scale * .5f));
		return new Rectangle(inset, 4, Math.Max(1, width - inset * 2), Math.Max(1, height - 8));
	}
}
