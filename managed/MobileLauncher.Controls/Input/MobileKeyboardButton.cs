using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameInput;

namespace MobileJoystick;

internal static class MobileKeyboardButton
{
    private static readonly List<MobileControlButton> buttons = new();

    internal static bool IsDedicated(MobileControlButton button) =>
        button.Action == MobileControlAction.ShowKeyboard ||
        button.Launcher?.Type == "button" && button.Launcher.String("keycode") == "ACTION_ShowKeyboard";

    internal static bool Hit(Vector2 normalized) {
        Vector2 point = normalized * new Vector2(PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight);
        foreach (var button in buttons)
            if (button.Contains(point)) return true;
        return false;
    }

    internal static void Clear() => buttons.Clear();

    internal static void Draw(SpriteBatch batch) {
        buttons.Clear();
        var config = MobileJoystickConfig.Instance;
        if (config?.EnableMobileControls != true || !config.EnableTouchInput || config.HideGameControls ||
            !Main.hasFocus || MobileMenuSystem.UsesDirectEditorTouch) return;
        var preset = MobileJoystickLayout.GetCustomPreset(config);
        if (preset?.Controls == null) return;
        int width = PlayerInput.RealScreenWidth, height = PlayerInput.RealScreenHeight;
        if (width <= 0 || height <= 0) return;
        float scale = MobileJoystickLayout.GetControlScale(config, width, height);
        foreach (var control in preset.Controls) {
            if (control.Kind != MobileCustomControlKind.Button) continue;
            var launcher = control.Launcher;
            if (launcher != null ? launcher.String("keycode") != "ACTION_ShowKeyboard" || !launcher.Flag("isVisible", true)
                : control.Action != MobileControlAction.ShowKeyboard) continue;
            float radius = MobileJoystickLayout.GetCustomControlRadius(control, scale, height);
            Vector2 center = MobileJoystickLayout.GetCustomControlCenter(control, width, height, radius);
            buttons.Add(new MobileControlButton(MobileControlAction.ShowKeyboard, control.TextureName,
                center, radius, control.Id, launcher: launcher, dimensions: launcher?.Dimensions(height, control.Size) ?? default,
                customTexture: control.CustomTexture));
        }
        if (buttons.Count == 0) return;
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        try {
            Color color = Color.White * config.Opacity;
            foreach (var button in buttons) {
                if (button.Launcher != null) LauncherControlArtwork.Draw(batch, button, color);
                else if (!CustomControlTextures.Draw(batch, button.CustomTexture, button.Center, new Vector2(button.Radius * 2), color))
                    MobileControlArtwork.DrawButton(batch, button.Action, button.Center, button.Radius, color, textureName: button.TextureName);
            }
        }
        finally { batch.End(); }
    }
}
