using System;
using System.ComponentModel;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;

namespace MobileJoystick;

public sealed partial class MobileExternalUiSystem
{
	private static void RegisterBossLogButton()
	{
		if (!ModLoader.TryGetMod("BossChecklist", out Mod mod)) return;
		Assembly code = mod.Code;
		var systemType = code.GetType("BossChecklist.Systems.BossLogSystem");
		object system = systemType?.GetProperty("Instance", Flags)?.GetValue(null);
		if (system == null || systemType.GetField("BossLog", Flags)?.GetValue(system) is not UIState root
			|| root.GetType().GetField("OpenBossLogButton", Flags)?.GetValue(root) is not UIElement button) return;

		var modType = code.GetType("BossChecklist.BossChecklist");
		FieldInfo configField = modType?.GetField("BossLogConfig", Flags);
		PropertyInfo position = configField?.FieldType.GetProperty("BossLogPos", Flags);
		MethodInfo save = configField == null ? null : modType.GetMethod("SaveConfig", Flags, null, [configField.FieldType], null);
		if (position?.CanWrite != true || position.PropertyType != typeof(Vector2) || save == null) return;

		FieldInfo dragging = button.GetType().GetField("dragging", Flags);
		object Config() => configField.GetValue(null);

		void SetOffset(Vector2 offset)
		{
			if (Config() is not object config) return;
			position.SetValue(config, offset);
			button.Left.Set(offset.X, 1f);
			button.Top.Set(offset.Y, 1f);
			button.Recalculate();
		}

		Rectangle Bounds()
		{
			if (button.Parent != null) return PanelBounds(button);
			object config = Config();
			if (config == null || position.GetValue(config) is not Vector2 offset) return Rectangle.Empty;
			CalculatedStyle parent = root.GetInnerDimensions();
			return ScreenBounds(parent.X + parent.Width + offset.X, parent.Y + parent.Height + offset.Y,
				button.Width.Pixels, button.Height.Pixels, Main.UIScale);
		}

		void Move(Vector2 center)
		{
			CalculatedStyle parent = root.GetInnerDimensions();
			Vector2 halfSize = new Vector2(button.Width.Pixels, button.Height.Pixels) * .5f;
			SetOffset(center / Main.UIScale - halfSize - new Vector2(parent.X + parent.Width, parent.Y + parent.Height));
		}

		Action reset = position.GetCustomAttribute<DefaultValueAttribute>()?.Value is Vector2 defaults
			? () => SetOffset(defaults) : null;
		ExternalUiLayoutRegistry.Register("BossChecklist.LogButton", MobileSettingsCatalog.Text("BossChecklistLogButton"),
			() => Main.playerInventory && button.Parent == root, Bounds, Move, null, null,
			() => { if (Config() is object config) save.Invoke(null, [config]); },
			_ => dragging?.SetValue(button, false), reset);
	}
}
