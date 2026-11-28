using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;

namespace MobileJoystick;

internal static class MobilePerformanceHud
{
	private static int mode;
	private static long nextRefresh;
	private static string[] lines = Array.Empty<string>();
	internal static readonly PerformanceHudDrag Drag = new();
	private static ulong lastDraw;
	private static bool hasBounds;
	private static bool ShowInWorld => Visible && !Main.gameMenu && Main.hasFocus
		&& !Main.ingameOptionsWindow && !Main.inFancyUI && !Main.playerInventory && !Main.mapFullscreen
		&& !Main.drawingPlayerChat && !MobileChatSystem.IsOpen && !Main.editSign && !Main.editChest
		&& Main.LocalPlayer.talkNPC < 0 && MobileJoystickConfig.Instance?.EnableMobileControls == true;
	internal static bool CanGrab => hasBounds && ShowInWorld && Main.GameUpdateCount - lastDraw <= 2;

	private static Rectangle Place(Rectangle initial, int width, int height)
	{
		lastDraw = Main.GameUpdateCount;
		hasBounds = true;
		return Drag.Place(initial, width, height);
	}

	internal static void ClearBounds()
	{
		hasBounds = false;
		Drag.End();
	}

	internal static bool Visible => mode > 0;

	internal static void Cycle()
	{
		mode = (mode + 1) % 4;
		ClearBounds();
		nextRefresh = 0;
	}

	internal static void Draw(SpriteBatch batch)
	{
		if (!ShowInWorld || !FontAssets.MouseText.IsLoaded) {
			ClearBounds();
			return;
		}
		long now = Environment.TickCount64;
		if (now >= nextRefresh) {
			Refresh();
			nextRefresh = now + 250;
		}

		var font = FontAssets.MouseText.Value;
		float scale = MathHelper.Clamp(PlayerInput.RealScreenHeight / 720f, .75f, 1.5f);
		float width = 0;
		foreach (string line in lines) width = Math.Max(width, font.MeasureString(line).X * scale);
		int padding = (int)(10 * scale);
		int lineHeight = (int)(font.LineSpacing * scale);
		int panelWidth = (int)Math.Ceiling(width) + padding * 2;
		int panelHeight = lineHeight * lines.Length + padding * 2;
		Rectangle bounds = new((PlayerInput.RealScreenWidth - panelWidth) / 2, padding, panelWidth, panelHeight);
		bounds = Place(bounds, PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight);
		Texture2D pixel = TextureAssets.MagicPixel.Value;
		batch.Draw(pixel, bounds, new Color(17, 24, 44) * .9f);
		batch.Draw(pixel, new Rectangle(bounds.X, bounds.Y, bounds.Width, Math.Max(1, padding / 5)), new Color(109, 188, 200));
		Vector2 position = new(bounds.X + padding, bounds.Y + padding);
		foreach (string line in lines) {
			Utils.DrawBorderString(batch, line, position, Color.White, scale);
			position.Y += lineHeight;
		}
	}

	private static void Refresh()
	{
		var read = AppContext.GetData("MobileLauncher.FrameStatistics")
			as Func<(double Fps, double MeanMs, double LowFps, double P95Ms, double WorstMs)>;
		var sample = read?.Invoke() ?? default;
		string fps = sample.Fps > 0 ? $"{sample.Fps:F0}" : "—";
		string T(string key) => MobileSettingsCatalog.Text(key);
		string FrameTime(double milliseconds) => sample.Fps > 0 ? $"{milliseconds:F1} ms" : "—";
		string summary = $"{fps} FPS";
		if (mode == 1) {
			lines = new[] { summary };
			return;
		}
		string timing = $"{T("PerformanceMean")} {FrameTime(sample.MeanMs)}    1% Low {(sample.LowFps > 0 ? sample.LowFps.ToString("F0") + " FPS" : "—")}";
		if (mode == 2) {
			lines = new[] { summary, timing };
			return;
		}
		double heapMiB = GC.GetGCMemoryInfo().TotalCommittedBytes / (1024d * 1024d);
		lines = new[] {
			summary,
			timing,
			$"P95 {FrameTime(sample.P95Ms)}    {T("PerformanceWorst")} {FrameTime(sample.WorstMs)}",
			$"{T("PerformanceResolution")} {PlayerInput.RealScreenWidth} × {PlayerInput.RealScreenHeight}",
			$"{T("PerformanceManagedMemory")} {heapMiB:F0} MiB",
		};
	}
}
