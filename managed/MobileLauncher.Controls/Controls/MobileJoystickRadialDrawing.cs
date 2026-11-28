using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace MobileJoystick;

public partial class MobileJoystickSystem
{
	private void DrawRadialControl(SpriteBatch spriteBatch, Color color)
	{
		MobileRadialControl radial = MobileJoystickInput.ActiveRadial;
		if (!radial.Active) return;
		bool potions = radial.Button.Action == MobileControlAction.QuickPotions;
		bool custom = radial.Button.Action == MobileControlAction.RadialMenu;
		Vector2 center = radial.Button.Center;
		float radius = radial.OuterRadius;
		MobileControlArtwork.DrawRadialFrame(spriteBatch, center, radial.Button.Radius, Color.White * .9f);
		for (int i = 0; i < 4; i++) {
			MobileControlAction? action = custom
				? radial.Button.RadialActions != null && i < radial.Button.RadialActions.Count && radial.Button.RadialActions[i] != MobileControlAction.Unbound
					? radial.Button.RadialActions[i] : null
				: i switch {
					0 => potions ? MobileControlAction.Heal : null,
					1 => potions ? MobileControlAction.Mana : MobileControlAction.ZoomIn,
					2 => potions ? MobileControlAction.Buff : null,
					_ => potions ? null : MobileControlAction.ZoomOut
				};
			Vector2 iconPosition = center + new Vector2((float)Math.Sin(i * MathHelper.PiOver2), -(float)Math.Cos(i * MathHelper.PiOver2)) * radius * 0.75f;
			if (action.HasValue && radial.SelectedSlot == i)
				DefaultControlTextures.TryDrawDefault(spriteBatch, "PhoneYellowCircle", iconPosition, radial.Button.Radius * 2f, Color.White);
			if (custom) {
				MobileControlAction bound = action ?? MobileControlAction.Unbound;
				if (bound == MobileControlAction.KeyboardKey && radial.Button.RadialKeyboardKeys?.Count > i)
					MobileKeyboardBindings.DrawLabel(spriteBatch, radial.Button.RadialKeyboardKeys[i], iconPosition, radial.Button.Radius * 1.5f, radial.Button.Radius / 48f, color);
				else if (bound is MobileControlAction.Heal or MobileControlAction.Mana or MobileControlAction.Buff)
					MobileControlArtwork.DrawPotionItemIcon(spriteBatch, bound, iconPosition, radial.Button.Radius, color);
				else if (!MobileControlArtwork.DrawActionIcon(spriteBatch, bound, iconPosition, radial.Button.Radius, color))
					DrawTexture(spriteBatch, MobileJoystickLayout.GetDefaultTexture(bound), iconPosition, radial.Button.Radius, color);
				Vector2 numberPosition = center + (iconPosition - center) * .6f;
				Utils.DrawBorderString(spriteBatch, (i + 1).ToString(), numberPosition, Color.White,
					radial.Button.Radius / 48f, 0.5f, 0.5f);
			}
			else if (potions && action.HasValue) {
				MobileControlArtwork.DrawPotionItemIcon(spriteBatch, action.Value, iconPosition, radial.Button.Radius * 0.95f,
					radial.SelectedSlot == i ? Color.White : color);
			}
			else if (action.HasValue) MobileControlArtwork.DrawActionIcon(spriteBatch, action.Value, iconPosition, radial.Button.Radius, color);
		}
	}

}
