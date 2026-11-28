using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;

namespace MobileJoystick;

internal static class LauncherControlPackSettings
{
	private static string selectedSource;
	private static int target;
	private static int sourcePage;
	private static string status = "";
	private static LauncherControlPack preview;
	internal static List<MobileSetting> Create(Action refresh)
	{
		string T(string key) => MobileSettingsCatalog.Text(key);
		var items = new List<MobileSetting>();
		void Action(string key, System.Action run, Func<string> display = null) => items.Add(new MobileSetting {
			LabelText = () => T(key), Kind = MobileSettingKind.Action, Activate = () => {
				try { run(); } catch (Exception error) { status = T("PackFailed") + ": " + error.Message; Main.NewText(status, Color.OrangeRed); }
			}, Display = display
		});
		Action("PackOpenFolder", LauncherControlPack.OpenDirectory);
		Action("PackRefresh", refresh);
		string[] sources;
		try { sources = LauncherControlPack.Sources(); } catch (Exception error) { sources = Array.Empty<string>(); status = error.Message; }
		int pages = Math.Max(1, (sources.Length + 7) / 8);
		sourcePage = Math.Clamp(sourcePage, 0, pages - 1);
		sources = sources.Skip(sourcePage * 8).Take(8).ToArray();
		if (pages > 1) Action("PackNextSources", () => { sourcePage = (sourcePage + 1) % pages; refresh(); }, () => (sourcePage + 1) + "/" + pages);
		if (!sources.Contains(selectedSource)) { selectedSource = sources.FirstOrDefault(); preview = null; }
		items.Add(new MobileSetting {
			LabelText = () => T("PackSource"), Kind = MobileSettingKind.Choice,
			ChoiceTexts = () => sources.Length == 0 ? new[] { T("PackNoSources") } : sources.Select(Path.GetFileName).ToArray(),
			Read = () => Math.Max(0, Array.IndexOf(sources, selectedSource)), Write = index => { if (sources.Length > 0) { selectedSource = sources[Math.Clamp((int)index, 0, sources.Length - 1)]; preview = null; status = ""; } }
		});
		items.Add(new MobileSetting {
			LabelText = () => T("PackTarget"), Kind = MobileSettingKind.Choice,
			ChoiceTexts = () => Enumerable.Range(0, MobileCustomControls.PresetCount).Select(i => (i + 1) + ": " + MobileJoystickConfig.Instance.CustomPresets.ElementAtOrDefault(i)?.Name).ToArray(),
			Read = () => target, Write = value => target = Math.Clamp((int)value, 0, MobileCustomControls.PresetCount - 1)
		});
		void Preview() {
			if (selectedSource == null) throw new InvalidDataException(T("PackNoSources"));
			preview = LauncherControlPack.Read(selectedSource);
			foreach (MobileCustomControl control in preview.Preset.Controls) {
				foreach (string key in LauncherControlBindings.ControlKeys(control.Launcher.Data).Distinct()) {
					if (string.IsNullOrEmpty(key) || key == "KEYBOARD_ESCAPE" || key.StartsWith("ACTION_", StringComparison.Ordinal) || key.StartsWith("TRIGGER_", StringComparison.Ordinal)) continue;
					if (LauncherControlBindings.InputName(key) != null && !LauncherControlBindings.Resolve(key).Any())
						preview.Warnings.Add(control.Launcher.String("name") + ": " + MobileSettingsCatalog.Text("PackUnboundKey", key));
				}
			}
			status = MobileSettingsCatalog.Text("PackSummary", preview.Preset.Controls.Count, preview.Warnings.Count);
		}
		Action("PackPreview", Preview, () => status);
		Action("PackReport", () => {
			Preview();
			string report = Path.Combine(LauncherControlPack.ImportRoot, "import-report.txt");
			File.WriteAllText(report, preview.Preset.Name + Environment.NewLine + status + Environment.NewLine + string.Join(Environment.NewLine, preview.Warnings));
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = report, UseShellExecute = true });
		});
		Action("PackImportReplace", () => {
			Preview(); preview.Install(target);
			status = MobileSettingsCatalog.Text("PackImported", target + 1) + " · " + status;
			Main.NewText(status, Color.LightGreen);
		}, () => status);
		return items;
	}
}
