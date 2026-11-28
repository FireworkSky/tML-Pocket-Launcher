using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input.Touch;
using Terraria;

namespace MobileJoystick;

internal static class MobileTrackpad
{
    private const int NoContact = -1;
    private const int TapMilliseconds = 350;
    private const int DoubleTapMilliseconds = 280;
    internal static readonly TouchCollection EmptyContacts = new(Array.Empty<TouchLocation>());
    private static readonly Dictionary<int, bool> directControls = new();
    private static readonly List<TouchLocation> controlSamples = new();
    private static readonly List<int> expiredContacts = new();
    private static readonly HashSet<int> switchingContacts = new();
    private static readonly List<MobileTouchContact> pinchContacts = new();
    private static Vector2 cursor = new(.5f, .5f);
    private static Vector2 fingerStart, lastFinger;
    private static int fingerId = NoContact;
    private static bool active, moved, dragging, pressed;
    private static long startedAt, lastTapAt = -DoubleTapMilliseconds - 1;

    internal static bool Enabled => MobileJoystickConfig.Instance?.ShowMouseCursor == true;
    internal static TouchCollection ControlContacts { get; private set; } = EmptyContacts;
    internal static IReadOnlyList<MobileTouchContact> PinchContacts => pinchContacts;
    internal static bool Clicked { get; private set; }
    private static Vector2 Bounds => new(Math.Max(1, TouchPanel.DisplayWidth), Math.Max(1, TouchPanel.DisplayHeight));
    internal static Vector2 ScreenPosition => MobileJoystickInput.ScaleTouchPosition(cursor * (Bounds - Vector2.One));

    internal static bool ReadPointer(ref Vector2 position)
    {
        position = ScreenPosition;
        return pressed;
    }

    internal static (Vector2 Position, bool Pressed)? Sample()
    {
        if (!Enabled || !Main.hasFocus || MobileMenuSystem.UsesDirectEditorTouch) {
            ClearGesture();
            if (switchingContacts.Count > 0) {
                var current = MobileTouchInput.GetState();
                switchingContacts.RemoveWhere(id => !ContainsActiveContact(current, id));
                if (switchingContacts.Count > 0) return (cursor * (Bounds - Vector2.One), false);
            }
            return Enabled ? (cursor * (Bounds - Vector2.One), false) : null;
        }

        TouchCollection touches = MobileTouchInput.GetState();
        if (!active) {
            // A finger held across a mode/focus change must lift before it can click.
            foreach (var touch in touches) directControls[touch.Id] = false;
            active = true;
        }

        controlSamples.Clear();
        pinchContacts.Clear();
        pressed = Clicked = false;
        TouchLocation selected = default;
        var config = MobileJoystickConfig.Instance;
        bool canPinch = config.EnableTouchInput && MobileJoystickInput.ShouldUseControls(config)
            && (Main.mapFullscreen || config.EnablePinchZoom && !Main.playerInventory && !MobileChatSystem.IsOpen);
        Vector2 bounds = Bounds;
        long now = Environment.TickCount64;
        int previousFinger = fingerId;
        foreach (var touch in touches) {
            bool down = touch.State is TouchLocationState.Pressed or TouchLocationState.Moved;
            if (!directControls.TryGetValue(touch.Id, out bool direct) && down) {
                direct = MobileJoystickInput.StartsOnDirectControl(touch);
                directControls[touch.Id] = direct;
                if (!direct && previousFinger == NoContact && fingerId == NoContact) {
                    fingerId = touch.Id;
                    lastFinger = fingerStart = touch.Position;
                    startedAt = now;
                    moved = false;
                    // A quick second touch after a click holds the mouse button for dragging.
                    dragging = now - lastTapAt <= DoubleTapMilliseconds;
                    lastTapAt = -DoubleTapMilliseconds - 1;
                }
            }
            if (direct) controlSamples.Add(touch);
            else if (canPinch && down)
                pinchContacts.Add(new MobileTouchContact(touch.Id, MobileJoystickInput.ScaleTouchPosition(touch.Position), touch.State));
            if (!down) directControls.Remove(touch.Id);
            if (touch.Id == fingerId) selected = touch;
        }

        if (pinchContacts.Count >= 2) {
            fingerId = NoContact;
            lastTapAt = -DoubleTapMilliseconds - 1;
        }
        else if (selected.State != TouchLocationState.Invalid) {
            float slop = Math.Max(4f, Math.Min(bounds.X, bounds.Y) * .008f);
            moved |= Vector2.DistanceSquared(selected.Position, fingerStart) >= slop * slop;
            if (moved || dragging) {
                Vector2 delta = (selected.Position - lastFinger) / Vector2.Max(bounds - Vector2.One, Vector2.One);
                cursor = Vector2.Clamp(cursor + delta, Vector2.Zero, Vector2.One);
                lastFinger = selected.Position;
            }
            if (selected.State != TouchLocationState.Released) pressed = dragging;
            else {
                pressed = Clicked = !moved && !dragging && now - startedAt <= TapMilliseconds;
                if (pressed) lastTapAt = now;
                fingerId = NoContact;
            }
        }
        else fingerId = NoContact;
        expiredContacts.Clear();
        foreach (int id in directControls.Keys)
            if (!ContainsContact(touches, id)) expiredContacts.Add(id);
        foreach (int id in expiredContacts) directControls.Remove(id);
        ControlContacts = controlSamples.Count == 0 ? EmptyContacts : new TouchCollection(controlSamples.ToArray());
        return (cursor * (bounds - Vector2.One), pressed);
    }

    private static bool ContainsContact(TouchCollection touches, int id)
    {
        foreach (var touch in touches)
            if (touch.Id == id) return true;
        return false;
    }

    private static bool ContainsActiveContact(TouchCollection touches, int id)
    {
        foreach (var touch in touches)
            if (touch.Id == id && touch.State is TouchLocationState.Pressed or TouchLocationState.Moved) return true;
        return false;
    }

    internal static void SuppressDirectPointerUntilRelease()
    {
        // Keep the finger that switched off the trackpad from becoming a click.
        foreach (var touch in MobileTouchInput.GetState())
            if (touch.State is TouchLocationState.Pressed or TouchLocationState.Moved) switchingContacts.Add(touch.Id);
    }

    private static void ClearGesture()
    {
        if (!active) return;
        active = pressed = Clicked = false;
        fingerId = NoContact;
        lastTapAt = -DoubleTapMilliseconds - 1;
        directControls.Clear();
        pinchContacts.Clear();
        ControlContacts = EmptyContacts;
    }

    internal static void Reset()
    {
        ClearGesture();
        switchingContacts.Clear();
        cursor = new(.5f, .5f);
    }
}
