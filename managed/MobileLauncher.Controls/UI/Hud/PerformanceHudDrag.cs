using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input.Touch;

namespace MobileJoystick;

internal sealed class PerformanceHudDrag
{
    private Rectangle bounds;
    private Vector2 viewport, grabOffset;
    private Vector2? anchor;
    internal int? Finger { get; private set; }

    internal Rectangle Place(Rectangle initial, int width, int height)
    {
        viewport = new Vector2(width, height);
        bounds = initial;
        Vector2 travel = AvailableTravel();
        Vector2 position = anchor.HasValue ? anchor.Value * travel : new Vector2(initial.X, initial.Y);
        position = Vector2.Clamp(position, Vector2.Zero, travel);
        bounds.X = (int)MathF.Round(position.X);
        bounds.Y = (int)MathF.Round(position.Y);
        return bounds;
    }

    private Vector2 AvailableTravel() => Vector2.Max(Vector2.Zero, viewport - new Vector2(bounds.Width, bounds.Height));

    internal bool Hit(Vector2 normalized)
    {
        var p = normalized * viewport;
        return bounds.Contains((int)p.X, (int)p.Y);
    }

    internal bool Consume(int id, TouchLocationState state, Vector2 normalized, bool visible)
    {
        if (!visible) {
            End();
            return false;
        }
        Vector2 p = normalized * viewport;
        if (Finger != id) {
            if (Finger.HasValue || state != TouchLocationState.Pressed || !Hit(normalized)) return false;
            Finger = id;
            grabOffset = p - new Vector2(bounds.X, bounds.Y);
        }
        if (state != TouchLocationState.Invalid) {
            Vector2 travel = AvailableTravel();
            Vector2 position = Vector2.Clamp(p - grabOffset, Vector2.Zero, travel);
            anchor = position / Vector2.Max(Vector2.One, travel);
        }
        if (state is TouchLocationState.Released or TouchLocationState.Invalid) End();
        return true;
    }

    internal void End() => Finger = null;
}
