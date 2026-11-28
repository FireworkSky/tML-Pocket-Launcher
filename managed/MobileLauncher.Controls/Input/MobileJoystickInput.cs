using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input.Touch;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace MobileJoystick;

public enum MobileControlAction
{
	Jump,
	Grapple,
	Heal,
	Mana,
	Buff,
	Mount,
	SmartInteract,
	SmartCursor,
	AimModeToggle,
	TargetLockToggle,
	Interact,
	UseItem,
	Inventory,
	Map,
	MapStyle,
	Settings,
	Chat,
	ZoomIn,
	ZoomOut,
	QuickPotions,
	Zoom,
	RadialMenu,
	Unbound = 22,
	SwapUseInteract,
	CycleUseProfile,
	QuickMount,
	AutoSelect,
	Loadout1,
	Loadout2,
	Loadout3,
	SwitchLoadout,
	ModKeybind,
	LauncherControl,
	KeyboardKey,
	ToggleGameControls,
	MapAlphaCycle,
	TogglePointerMode,
	PerformanceDisplay,
	ShowKeyboard
}

internal readonly struct MobileControlButton
{
	public MobileControlButton(MobileControlAction action, string textureName, Vector2 center, float radius,
		string id = null, bool multitouch = true, IReadOnlyList<MobileControlAction> radialActions = null, string modKeybind = null, IReadOnlyList<string> radialModKeybinds = null,
		MobileLauncherControl launcher = null, Vector2 dimensions = default, string keyboardKey = null, IReadOnlyList<string> radialKeyboardKeys = null,
		string customTexture = null)
	{
		Action = action;
		TextureName = textureName;
		CustomTexture = customTexture;
		Center = center;
		Radius = radius;
		Id = id;
		Multitouch = multitouch;
		RadialActions = radialActions;
		ModKeybind = modKeybind;
		RadialModKeybinds = radialModKeybinds;
		KeyboardKey = keyboardKey;
		RadialKeyboardKeys = radialKeyboardKeys;
		Launcher = launcher;
		Dimensions = dimensions;
	}

	public MobileControlAction Action { get; }

	public string TextureName { get; }
	public string CustomTexture { get; }

	public Vector2 Center { get; }

	public float Radius { get; }

	public string Id { get; }

	public bool Multitouch { get; }

	public IReadOnlyList<MobileControlAction> RadialActions { get; }

	public string ModKeybind { get; }
	public IReadOnlyList<string> RadialModKeybinds { get; }
	public string KeyboardKey { get; }
	public IReadOnlyList<string> RadialKeyboardKeys { get; }
	public MobileLauncherControl Launcher { get; }
	public Vector2 Dimensions { get; }

	public Rectangle Bounds => new((int)(Center.X - Radius), (int)(Center.Y - Radius), (int)(Radius * 2f), (int)(Radius * 2f));

	public bool Contains(Vector2 point) => Launcher != null ? Launcher.Contains(point, Center, Dimensions) : Action is MobileControlAction.Inventory or MobileControlAction.Chat or MobileControlAction.Map or MobileControlAction.MapStyle
		? Bounds.Contains(point.ToPoint())
		: Vector2.DistanceSquared(point, Center) <= Radius * Radius;
}

internal readonly record struct MobileControlStick(string Id, MobileJoystickRole Role, Vector2 Center, float Radius, string TextureName = null, string CustomTexture = null);

internal readonly struct MobileControlLayout
{
	public MobileControlLayout(Vector2 joystickCenter, float joystickRadius, Vector2 attackCenter, float attackRadius, MobileControlButton[] buttons, float scale, IReadOnlyList<MobileControlStick> sticks = null)
	{
		JoystickCenter = joystickCenter;
		JoystickRadius = joystickRadius;
		AttackCenter = attackCenter;
		AttackRadius = attackRadius;
		Buttons = buttons;
		Scale = scale;
		Sticks = sticks ?? new[] { new MobileControlStick(null, MobileJoystickRole.Move, joystickCenter, joystickRadius, "JoystickKnob"),
			new MobileControlStick(null, MobileJoystickRole.Aim, attackCenter, attackRadius, "AttackAimUse") };
	}

	public Vector2 JoystickCenter { get; }

	public float JoystickRadius { get; }

	public Vector2 AttackCenter { get; }

	public float AttackRadius { get; }

	public MobileControlButton[] Buttons { get; }

	public float Scale { get; }
	public IReadOnlyList<MobileControlStick> Sticks { get; }
}

internal readonly struct MobileTouchContact
{
	public MobileTouchContact(int id, Vector2 position, TouchLocationState state)
	{
		Id = id;
		Position = position;
		State = state;
	}

	public int Id { get; }

	public Vector2 Position { get; }

	public TouchLocationState State { get; }

	public bool IsReleased => State == TouchLocationState.Released || State == TouchLocationState.Invalid;
}

internal static partial class MobileJoystickInput
{
	private enum AimStickMode { LeftClick, AimOnly, RightClick }
	private static AimStickMode aimStickMode;
	private const int MouseContactId = -1000;
	private const int StatusTextFrames = 150;

	private static readonly List<MobileTouchContact> Contacts = new();
	private static readonly HashSet<int> touchFingerIds = new();
	private static readonly HashSet<int> currentTouchFingerIds = new();
	private static readonly HashSet<MobileControlAction> CurrentActions = new();
	private static readonly HashSet<MobileControlAction> PreviousActions = new();
	private static readonly HashSet<int> externalUiContacts = new();
	internal static bool HasExternalUiTouch => externalUiContacts.Count != 0;
	private static readonly HashSet<MobileControlAction> PressedActions = new();

	private static int joystickFingerId = int.MinValue;
	private static int attackFingerId = int.MinValue;
	private static MobileControlStick activeMoveStick, activeAimStick;
	internal static MobileControlStick MoveStick => activeMoveStick.Radius > 0 ? activeMoveStick : new(null, MobileJoystickRole.Move, Layout.JoystickCenter, Layout.JoystickRadius);
	internal static MobileControlStick AimStick => activeAimStick.Radius > 0 ? activeAimStick : new(null, MobileJoystickRole.Aim, Layout.AttackCenter, Layout.AttackRadius);
	private static Vector2 touchAimPosition;
	private static Vector2 previousAttackTouchPosition;
	private static Vector2? previousAimOrigin;
	private static bool hasTouchAimPosition;
	private static int statusTextTime;
	private static bool mouseWasDown;
	private static bool waitForMouseRelease;
	private static bool gameplayUseWasDown;
	private static readonly MobileRadialControl Radial = new();
	private static int radialContactThisFrame = int.MinValue;
	internal static MobileRadialControl ActiveRadial => Radial;
	private static bool IsRadialContact(int id) => (Radial.Active && Radial.ContactId == id) || radialContactThisFrame == id;

	public static MobileControlLayout Layout { get; private set; }

	public static Vector2 JoystickVector { get; private set; }

	public static bool AttackHeld { get; private set; }
	internal static bool AttackJustPressed { get; private set; }
	internal static bool SmartCursorToolUseHeld => AttackHeld && AimAndUseMode && SmartCursorEnabled && !MobileWorldInteraction.UsesRightTrigger
		&& Main.LocalPlayer.HeldItem is { IsAir: false } item && (item.pick > 0 || item.axe > 0 || item.hammer > 0);

	public static Vector2 TouchAimPosition => interactHasAim ? InteractAimPosition : BaseTouchAimPosition;
	private static Vector2 BaseTouchAimPosition => touchAimPosition
		+ (previousAimOrigin.HasValue ? MobileWorldInteraction.ToScreen(Main.LocalPlayer.MountedCenter) - previousAimOrigin.Value : Vector2.Zero);

	public static bool HasTouchAimPosition => interactHasAim || hasTouchAimPosition;
	internal static bool HasExplicitTouchAim => interactHasAim || launcherCursorActive;

	internal static Vector2 TouchCrosshairPosition => MobileWorldInteraction.DirectUseHeld
		? MobileWorldInteraction.PrecisionPosition
		: HasTouchAimPosition ? TouchAimPosition : MobileWorldInteraction.ToScreen(Main.MouseWorld);

	internal static bool ShowTouchCrosshair => MobileWorldInteraction.DirectUseHeld || AttackHeld || InteractHeld
		|| launcherCursorActive || IsActionDown(MobileControlAction.UseItem);

	public static bool HasAnyControlTouch { get; private set; }
	internal static bool ControlsHidden => MobileJoystickConfig.Instance?.HideGameControls == true;

	public static bool EnabledThisFrame { get; private set; }

	public static bool AimAndUseMode => aimStickMode == AimStickMode.LeftClick;
	internal static bool AimRightClickMode => aimStickMode == AimStickMode.RightClick;
	internal static bool AimRightClickHeld => AttackHeld && AimRightClickMode;
	internal static string AimModeLabel => MobileSettingsCatalog.Text(aimStickMode switch {
		AimStickMode.LeftClick => "AimAndUse",
		AimStickMode.AimOnly => "AimOnly",
		_ => "MouseRightClick"
	});

	public static bool TargetLockEnabled { get; private set; }

	public static bool SmartCursorEnabled { get; private set; } = true;

	public static string StatusText { get; private set; } = string.Empty;

	public static float StatusTextOpacity => MathHelper.Clamp(statusTextTime / 30f, 0f, 1f);

	public static Vector2 AttackKnobOffset { get; private set; }

	public static IReadOnlyCollection<MobileControlAction> ActiveActions => CurrentActions;

	public static IReadOnlyList<MobileTouchContact> ActiveContacts => Contacts;

	public static bool IsActionDown(MobileControlAction action) => CurrentActions.Contains(action);

	public static bool WasActionPressed(MobileControlAction action) => PressedActions.Contains(action);

	public static bool IsFingerOnJoystick(int fingerId) => joystickFingerId == fingerId;

	public static bool IsFingerOnAttack(int fingerId) => attackFingerId == fingerId;

	public static void Reset()
	{
		ResetLauncherControls();
		gameplayUseWasDown = false;
		MobileWorldInteraction.Reset();
		ResetInteractAim();
		MobileModKeybinds.Reset();
		MobileKeyboardBindings.Reset();
		ResetMapInteraction();
		Grapple.Reset();
		pendingGrappleDirection = null;
		grappleContactThisFrame = int.MinValue;
		Radial.Reset();
		radialContactThisFrame = int.MinValue;
		Contacts.Clear();
		touchFingerIds.Clear();
		currentTouchFingerIds.Clear();
		CurrentActions.Clear();
		PreviousActions.Clear();
		externalUiContacts.Clear();
		MobileControlAvoidance.Clear();
		PressedActions.Clear();
		joystickFingerId = attackFingerId = int.MinValue;
		activeMoveStick = activeAimStick = default;
		JoystickVector = AttackKnobOffset = Vector2.Zero;
		AttackHeld = AttackJustPressed = HasAnyControlTouch = EnabledThisFrame = false;
		TargetLockEnabled = false;
		SmartCursorEnabled = true;
		aimStickMode = AimStickMode.LeftClick;
		MobileCalamityMechInput.Reset();
		StatusText = string.Empty;
		statusTextTime = 0;
		mouseWasDown = false;
		waitForMouseRelease = true;
		ResetTouchAim();
		LockOnHelper.ForceUsability = false;
		if (!Main.dedServ) LockOnHelper.Toggle(forceOff: true);
	}

	internal static bool IsButtonAvailable(MobileControlButton button)
	{
		if (MobileKeyboardButton.IsDedicated(button)) return false; // Drawn and captured above menus as well as gameplay.
		if (ControlsHidden && !IsControlVisibilityToggle(button)) return false;
		if (MobileControlAvoidance.Obscures(button) || MobileExternalUiSystem.BlocksInventory(button.Center)) return false;
		if (MobileChatSystem.BlocksControl(button.Center) && button.Action != MobileControlAction.Chat) return false;
		if (button.Launcher != null) return button.Launcher.Flag("isVisible", true) && !Main.mapFullscreen && !Main.playerInventory && !MobileInventorySystem.BlocksControl(button.Center);
		if (Main.mapFullscreen) return button.Action is MobileControlAction.Chat or MobileControlAction.Map or MobileControlAction.MapStyle
			or MobileControlAction.ZoomIn or MobileControlAction.ZoomOut;
		if (Main.playerInventory && button.Action is MobileControlAction.Inventory or MobileControlAction.Settings
			or MobileControlAction.Chat or MobileControlAction.Map or MobileControlAction.MapStyle) return false;
		return !MobileInventorySystem.BlocksControl(button.Center);
	}

	internal static bool IsStickAvailable(MobileControlStick stick)
	{
		if (ControlsHidden || stick.Radius <= 0f) return false;
		// A panel opened during an existing drag must not cancel that finger.
		if (stick.Role == MobileJoystickRole.Move && joystickFingerId != int.MinValue && activeMoveStick == stick
			|| stick.Role == MobileJoystickRole.Aim && attackFingerId != int.MinValue && activeAimStick == stick) return true;
		return !MobileControlAvoidance.Obscures(stick) && !MobileExternalUiSystem.BlocksInventory(stick.Center);
	}

	private static bool IsControlVisibilityToggle(MobileControlButton button)
		=> button.Action == MobileControlAction.ToggleGameControls
			|| button.Action == MobileControlAction.RadialMenu && button.RadialActions?.Contains(MobileControlAction.ToggleGameControls) == true
			|| button.Launcher != null && LauncherControlBindings.ControlKeys(button.Launcher.Data)
				.Any(key => key == "ACTION_ToggleGameControls");

	public static void Update()
	{
		bool previousAttackHeld = AttackHeld;
		AttackJustPressed = false;
		if (statusTextTime > 0) {
			statusTextTime--;
		}

		MobileJoystickConfig config = MobileJoystickConfig.Instance;
		if (!Main.hasFocus) waitForMouseRelease = true;
		EnabledThisFrame = ShouldUseControls(config);
		if (EnabledThisFrame) MobileControlAvoidance.Update();
		else MobileControlAvoidance.Clear();
		if (!Main.hasFocus) externalUiContacts.Clear();
		MobileModKeybinds.BeginFrame();
		MobileKeyboardBindings.BeginFrame();
		PreviousActions.Clear();
		foreach (MobileControlAction action in CurrentActions) {
			PreviousActions.Add(action);
		}

		CurrentActions.Clear();
		PressedActions.Clear();
		Contacts.Clear();
		JoystickVector = Vector2.Zero;
		AttackHeld = false;
		AttackKnobOffset = Vector2.Zero;
		HasAnyControlTouch = false;
		radialContactThisFrame = int.MinValue;

		if (!EnabledThisFrame) {
			MobileCalamityMechInput.Reset();
			ResetLauncherControls();
			MobileCalamityUiSystem.ReadInput(Array.Empty<MobileTouchContact>());
			ResetMapInteraction();
			ResetInteractAim();
			Grapple.Reset();
			pendingGrappleDirection = null;
			grappleContactThisFrame = int.MinValue;
			Radial.Reset();
			if (Main.hasFocus && config?.EnableMobileControls == true && (config.UseMobileInventory && Main.playerInventory || MobileChatSystem.IsOpen || Main.drawingPlayerChat) && !Main.gameMenu && !Main.dedServ && !Main.ingameOptionsWindow && !Main.inFancyUI)
				ReadTouchContacts(config);
			MobileChatSystem.UpdateInput();
			joystickFingerId = int.MinValue;
			attackFingerId = int.MinValue;
			ResetTouchAim();
			LockOnHelper.ForceUsability = false;
			return;
		}

		LockOnHelper.ForceUsability = TargetLockEnabled;

		Layout = CreateLayout(config);
		ReadTouchContacts(config);
		MobileChatSystem.UpdateInput();
		Contacts.RemoveAll(contact => MobileChatSystem.OwnsContact(contact.Id));
		externalUiContacts.RemoveWhere(id => id == MouseContactId ? !mouseWasDown : !currentTouchFingerIds.Contains(id));
		Contacts.RemoveAll(contact => {
			if (contact.Id == joystickFingerId || contact.Id == attackFingerId || MobileInventorySystem.OwnsContact(contact.Id))
				return false;
			if (externalUiContacts.Contains(contact.Id)) return true;
			bool overPanel = MobileExternalUiSystem.BlocksInventory(contact.Position);
			if (overPanel && contact.State == TouchLocationState.Pressed) externalUiContacts.Add(contact.Id);
			return overPanel;
		});
		MobileCalamityUiSystem.ReadInput(Contacts);
		Contacts.RemoveAll(contact => MobileCalamityUiSystem.OwnsContact(contact.Id));
		if (MobileCalamityUiSystem.Dragging) {
			Main.mouseLeft = PlayerInput.Triggers.Current.MouseLeft = false;
			PlayerInput.Triggers.JustPressed.MouseLeft = false;
		}
		if (MobileCalamityMechInput.ReadInput(Contacts)) {
			HasAnyControlTouch = true;
			Main.mouseLeft = PlayerInput.Triggers.Current.MouseLeft = false;
			PlayerInput.Triggers.JustPressed.MouseLeft = false;
		}
		if (UpdateScreenPinch(config)) {
			HasAnyControlTouch = true;
			Main.mouseLeft = PlayerInput.Triggers.Current.MouseLeft = false;
			PlayerInput.Triggers.JustPressed.MouseLeft = false;
		}
		UpdateLauncherControls();
		UpdateRadialControls();
		UpdateGrappleControl();
		UpdateInteractAim();
		UpdateFingerAssignments();
		UpdateAnalogControls(config);
		AttackJustPressed = AttackHeld && !previousAttackHeld;
		UpdateButtonActions();
		if (!Main.playerInventory && !Main.mapFullscreen) UpdateHotbarSelection(config);
		ApplyImmediateActions();
		// Cache the logical aim before native world/UI zoom contexts restore the
		// input. Movement, jump and toolbar fingers must never become the aim.
		if ((!MobileTrackpad.Enabled && (HasAnyControlTouch || hasTouchAimPosition && Contacts.Count == 0 && MobileTouchInput.UsingTouch)
			|| MobileTrackpad.Enabled && (AttackHeld || HasExplicitTouchAim))
			&& !Main.playerInventory && !Main.mapFullscreen && Main.LocalPlayer.talkNPC < 0
			&& !MobileExternalUiSystem.BlocksInventory(new Vector2(Main.mouseX, Main.mouseY))) {
			MobileJoystickPlayer.ApplyTouchAim();
			// World use is reconciled from its own contact in UpdateInput, after
			// this control pass. Never preserve the movement finger's mouse hold.
			Main.mouseLeft = PlayerInput.Triggers.Current.MouseLeft = false;
			PlayerInput.Triggers.JustPressed.MouseLeft = false;
		}
		UpdateHeldZoom();
		UpdateFullscreenMapGesture(Stopwatch.GetTimestamp());

		if (HasAnyControlTouch && Main.myPlayer >= 0 && Main.myPlayer < Main.maxPlayers) {
			Main.LocalPlayer.mouseInterface = true;
		}
	}

	public static bool ContactBelongsToControl(MobileTouchContact contact)
	{
		if (MobileChatSystem.OwnsContact(contact.Id)) return true;
		if (MobileCalamityUiSystem.OwnsContact(contact.Id)) return true;
		if (EnabledThisFrame && (IsGrappleContact(contact.Id) || IsInteractContact(contact.Id))) return true;
		if (EnabledThisFrame && IsRadialContact(contact.Id)) return true;
		if (!EnabledThisFrame || MobileInventorySystem.OwnsContact(contact.Id)
			|| MobileInventorySystem.BlocksControl(contact.Position)) return false;
		if (contact.IsReleased) {
			return joystickFingerId == contact.Id || attackFingerId == contact.Id;
		}

		if (joystickFingerId == contact.Id || attackFingerId == contact.Id) {
			return true;
		}

		return HitsDirectControl(contact, Layout);
	}

	internal static bool StartsOnDirectControl(TouchLocation touch)
	{
		var config = MobileJoystickConfig.Instance;
		if (!ShouldUseControls(config) || !config.EnableTouchInput) return false;
		var contact = new MobileTouchContact(touch.Id, ScaleTouchPosition(touch.Position), touch.State);
		return !MobileInventorySystem.BlocksControl(contact.Position)
			&& !MobileChatSystem.BlocksControl(contact.Position)
			&& !MobileExternalUiSystem.BlocksInventory(contact.Position)
			&& HitsDirectControl(contact, CreateLayout(config));
	}

	private static bool HitsDirectControl(MobileTouchContact contact, MobileControlLayout layout)
	{
		if (TouchesStick(contact, layout)) return true;
		foreach (MobileControlButton button in layout.Buttons) {
			if (IsButtonAvailable(button) && button.Contains(contact.Position)) {
				return true;
			}
		}

		if (Main.playerInventory || ControlsHidden || MobileTrackpad.Enabled && Main.mapFullscreen) return false;
		MobileHotbarLayout hotbar = MobileJoystickLayout.CreateHotbarLayout(MobileJoystickConfig.Instance);
		for (int i = 0; i < 10; i++) {
			Rectangle slot = hotbar.GetSlot(i);
			if (slot.Contains(contact.Position.ToPoint()) && !MobileControlAvoidance.Obscures(slot)) {
				return true;
			}
		}

		return false;
	}

	internal static bool ShouldUseControls(MobileJoystickConfig config)
	{
		if (config == null || !config.EnableMobileControls) {
			return false;
		}

		if (Main.dedServ || Main.gameMenu || !Main.hasFocus || Main.myPlayer < 0 || Main.myPlayer >= Main.maxPlayers) {
			return false;
		}

		Player player = Main.LocalPlayer;
		if (!player.active || player.dead || player.ghost) {
			return false;
		}

		if (Main.LocalPlayer.talkNPC >= 0 || (Main.playerInventory && !MobileInventorySystem.Active) || Main.editSign || Main.editChest || Main.ingameOptionsWindow || Main.inFancyUI) {
			return false;
		}

		return true;
	}

	private static MobileControlLayout CreateLayout(MobileJoystickConfig config)
	{
		MobileControlLayout layout = Main.mapFullscreen ? CreateMapLayout(config)
			: MobileJoystickLayout.CreateControlsLayout(config, Main.screenWidth, Main.screenHeight);
		if (!ControlsHidden || layout.Buttons.Any(IsControlVisibilityToggle)) return layout;
		MobileHotbarLayout hotbar = MobileJoystickLayout.CreateHotbarLayout(config);
		float radius = hotbar.SettingsButton.Width * .5f;
		MobileControlButton restore = new(MobileControlAction.ToggleGameControls, "Settings", hotbar.SettingsButton.Center.ToVector2(), radius);
		return new MobileControlLayout(layout.JoystickCenter, layout.JoystickRadius, layout.AttackCenter, layout.AttackRadius,
			layout.Buttons.Append(restore).ToArray(), layout.Scale, layout.Sticks);
	}

	private static void UpdateHotbarSelection(MobileJoystickConfig config)
	{
		if (config.HideGameControls) return;
		MobileHotbarLayout hotbar = MobileJoystickLayout.CreateHotbarLayout(config);
		foreach (MobileTouchContact contact in Contacts) {
			if (contact.IsReleased || IsRadialContact(contact.Id) || IsGrappleContact(contact.Id) || IsInteractContact(contact.Id) || contact.Id == joystickFingerId || contact.Id == attackFingerId) {
				continue;
			}
			for (int i = 0; i < 10; i++) {
				Rectangle slot = hotbar.GetSlot(i);
				if (slot.Contains(contact.Position.ToPoint()) && !MobileControlAvoidance.Obscures(slot)) {
					Main.LocalPlayer.selectedItem = i;
					HasAnyControlTouch = true;
					break;
				}
			}
		}
	}

	private static void ReadTouchContacts(MobileJoystickConfig config)
	{
		currentTouchFingerIds.Clear();
		if (config.EnableTouchInput) {
			TouchCollection touches = MobileTrackpad.Enabled ? MobileTrackpad.ControlContacts : MobileTouchInput.GetState();
			for (int i = 0; i < touches.Count; i++) {
				TouchLocation touch = touches[i];
				Vector2 position = ScaleTouchPosition(touch.Position);
				TouchLocationState state = touch.State;
				if (state is TouchLocationState.Pressed or TouchLocationState.Moved) {
					currentTouchFingerIds.Add(touch.Id);
					// A render-only poll may advance Pressed to Moved; catch-up
					// updates may also read the same Pressed sample more than once.
					state = touchFingerIds.Contains(touch.Id) ? TouchLocationState.Moved : TouchLocationState.Pressed;
				}
				Contacts.Add(new MobileTouchContact(touch.Id, position, state));
			}
		}
		touchFingerIds.Clear();
		touchFingerIds.UnionWith(currentTouchFingerIds);

		if (!Main.mouseLeft) waitForMouseRelease = false;
		// The launcher maps a real touch to Main.mouseLeft. Do not add it twice,
		// or let that synthetic hold become a fallback finger after touch-up.
		if (Contacts.Count > 0 && Main.mouseLeft) waitForMouseRelease = true;
		bool fallbackDown = !MobileTrackpad.Enabled && config.EnableMouseFallback && Main.mouseLeft && !waitForMouseRelease && Contacts.Count == 0;
		if (fallbackDown) {
			Contacts.Add(new MobileTouchContact(MouseContactId, new Vector2(Main.mouseX, Main.mouseY),
				mouseWasDown ? TouchLocationState.Moved : TouchLocationState.Pressed));
		}
		else if (mouseWasDown) {
			Contacts.Add(new MobileTouchContact(MouseContactId, new Vector2(Main.mouseX, Main.mouseY), TouchLocationState.Released));
		}
		mouseWasDown = fallbackDown;
	}

	internal static Vector2 ScaleTouchPosition(Vector2 position)
	{
		float displayWidth = TouchPanel.DisplayWidth <= 0 ? PlayerInput.RealScreenWidth : TouchPanel.DisplayWidth;
		float displayHeight = TouchPanel.DisplayHeight <= 0 ? PlayerInput.RealScreenHeight : TouchPanel.DisplayHeight;
		if (displayWidth <= 0f || displayHeight <= 0f) {
			return position;
		}

		return new Vector2(position.X * PlayerInput.RealScreenWidth / displayWidth, position.Y * PlayerInput.RealScreenHeight / displayHeight);
	}

	private static void UpdateFingerAssignments()
	{
		if (ControlsHidden) {
			joystickFingerId = attackFingerId = int.MinValue;
			activeMoveStick = activeAimStick = default;
			JoystickVector = AttackKnobOffset = Vector2.Zero;
			AttackHeld = AttackJustPressed = false;
			ResetTouchAim();
			return;
		}
		bool joystickStillDown = false;
		bool attackStillDown = false;

		foreach (MobileTouchContact contact in Contacts) {
			bool assignedStick = contact.Id == joystickFingerId || contact.Id == attackFingerId;
			if (contact.IsReleased || IsRadialContact(contact.Id) || IsGrappleContact(contact.Id) || IsInteractContact(contact.Id)
				|| !assignedStick && MobileInventorySystem.BlocksControl(contact.Position) || MobileInventorySystem.OwnsContact(contact.Id)) {
				continue;
			}
			if (IsBlockedByExclusiveControl(contact.Id)) continue;

			if (contact.Id == joystickFingerId) {
				joystickStillDown = true;
			}

			if (contact.Id == attackFingerId) {
				attackStillDown = true;
			}
		}

		if (!joystickStillDown) {
			joystickFingerId = int.MinValue;
		}

		if (!attackStillDown) {
			attackFingerId = int.MinValue;
			ResetTouchAim();
		}

		foreach (MobileTouchContact contact in Contacts) {
			if (contact.IsReleased) {
				continue;
			}

			if (contact.State != TouchLocationState.Pressed || IsRadialContact(contact.Id) || IsGrappleContact(contact.Id) || IsInteractContact(contact.Id) || contact.Id == joystickFingerId || contact.Id == attackFingerId
				|| MobileInventorySystem.BlocksControl(contact.Position) || MobileInventorySystem.OwnsContact(contact.Id)
				|| IsBlockedByExclusiveControl(contact.Id)) continue;

			foreach (MobileControlStick stick in Layout.Sticks) {
                if (!IsStickAvailable(stick) || Vector2.DistanceSquared(contact.Position, stick.Center) > stick.Radius * stick.Radius) continue;
                if (stick.Role == MobileJoystickRole.Move && joystickFingerId == int.MinValue) {
                    joystickFingerId = contact.Id;
                    activeMoveStick = stick;
                    HasAnyControlTouch = true;
                    break;
                }
                if (stick.Role == MobileJoystickRole.Aim && attackFingerId == int.MinValue) {
                    attackFingerId = contact.Id;
                    activeAimStick = stick;
                    InitializeTouchAim(contact.Position);
                    HasAnyControlTouch = true;
                    break;
                }
            }
		}
	}

	private static void UpdateAnalogControls(MobileJoystickConfig config)
	{
		if (hasTouchAimPosition && previousAimOrigin.HasValue) {
			touchAimPosition = BaseTouchAimPosition;
			previousAimOrigin = MobileWorldInteraction.ToScreen(Main.LocalPlayer.MountedCenter);
		}
		foreach (MobileTouchContact contact in Contacts) {
			if (contact.IsReleased) {
				continue;
			}

			if (contact.Id == joystickFingerId) {
				Vector2 delta = contact.Position - MoveStick.Center;
				float length = delta.Length();
				if (length > MoveStick.Radius) {
					delta *= MoveStick.Radius / length;
				}

				JoystickVector = delta / MoveStick.Radius;
				if (JoystickVector.Length() < config.JoystickDeadzone) {
					JoystickVector = Vector2.Zero;
				}

				HasAnyControlTouch = true;
			}

			if (contact.Id == attackFingerId) {
				if (!hasTouchAimPosition) {
					InitializeTouchAim(contact.Position);
				}

				Vector2 knobDelta = (contact.Position - AimStick.Center) / AimStick.Radius;
				if (knobDelta.LengthSquared() > 1f) {
					knobDelta.Normalize();
				}
				AttackKnobOffset = knobDelta;
				Vector2 dragDelta = contact.Position - previousAttackTouchPosition;
				touchAimPosition += dragDelta * config.CursorSensitivity;
				touchAimPosition.X = MathHelper.Clamp(touchAimPosition.X, 0f, PlayerInput.RealScreenWidth - 1f);
				touchAimPosition.Y = MathHelper.Clamp(touchAimPosition.Y, 0f, PlayerInput.RealScreenHeight - 1f);
				previousAttackTouchPosition = contact.Position;
				AttackHeld = true;
				HasAnyControlTouch = true;
			}
		}
	}

	private static void InitializeTouchAim(Vector2 contactPosition)
	{
		Player player = Main.LocalPlayer;
		// A released stick returns to rest; each new drag starts at the player.
		touchAimPosition = hasTouchAimPosition ? BaseTouchAimPosition : MobileWorldInteraction.ToScreen(player.MountedCenter);
		previousAimOrigin = MobileWorldInteraction.ToScreen(player.MountedCenter);
		previousAttackTouchPosition = contactPosition;
		hasTouchAimPosition = true;
	}

	private static void ResetTouchAim()
	{
		MobileInteractionTarget.Reset();
		touchAimPosition = Vector2.Zero;
		previousAttackTouchPosition = Vector2.Zero;
		previousAimOrigin = null;
		hasTouchAimPosition = false;
	}

	private static void UpdateRadialControls()
	{
		if (Radial.Active) {
			bool found = false;
			foreach (MobileTouchContact contact in Contacts) {
				if (contact.Id != Radial.ContactId) continue;
				found = true;
				radialContactThisFrame = contact.Id;
				var radialButton = Radial.Button;
				int slot = Math.Abs(contact.Position.Y - radialButton.Center.Y) >= Math.Abs(contact.Position.X - radialButton.Center.X)
					? contact.Position.Y < radialButton.Center.Y ? 0 : 2 : contact.Position.X > radialButton.Center.X ? 1 : 3;
				MobileControlAction? committed = Radial.Update(contact);
				if (committed == MobileControlAction.ModKeybind && radialButton.RadialModKeybinds?.Count > slot)
					MobileModKeybinds.Hold(radialButton.RadialModKeybinds[slot]);
				if (committed == MobileControlAction.KeyboardKey && radialButton.RadialKeyboardKeys?.Count > slot)
					MobileKeyboardBindings.Hold(radialButton.RadialKeyboardKeys[slot]);
				if (committed.HasValue) CurrentActions.Add(committed.Value);
				break;
			}
			if (!found) Radial.Reset();
		}
		if (!Radial.Active) {
			foreach (MobileTouchContact contact in Contacts) {
				if (contact.State != TouchLocationState.Pressed || IsRadialContact(contact.Id) || IsGrappleContact(contact.Id) || IsInteractContact(contact.Id)
					|| contact.Id == joystickFingerId || contact.Id == attackFingerId
					|| MobileInventorySystem.BlocksControl(contact.Position) || MobileInventorySystem.OwnsContact(contact.Id)) continue;
				foreach (MobileControlButton button in Layout.Buttons) {
					if (button.Action is not (MobileControlAction.QuickPotions or MobileControlAction.Zoom or MobileControlAction.RadialMenu)
						|| !IsButtonAvailable(button) || !button.Contains(contact.Position)
						|| !CanActivateCustomButton(button, contact)) continue;
					Radial.Begin(button, contact);
					radialContactThisFrame = contact.Id;
					break;
				}
				if (Radial.Active) break;
			}
		}
		if (Radial.Active) {
			HasAnyControlTouch = true;
			if (Radial.Button.Action == MobileControlAction.Zoom && Radial.Selection.HasValue) CurrentActions.Add(Radial.Selection.Value);
		}
		if (radialContactThisFrame != int.MinValue) HasAnyControlTouch = true;
	}

	private static void UpdateButtonActions()
	{
		foreach (MobileTouchContact contact in Contacts) {
			if (contact.IsReleased || IsRadialContact(contact.Id) || IsGrappleContact(contact.Id) || IsInteractContact(contact.Id) || MobileInventorySystem.BlocksControl(contact.Position) || MobileInventorySystem.OwnsContact(contact.Id)) {
				continue;
			}

			if (contact.Id == joystickFingerId || contact.Id == attackFingerId) {
				continue;
			}

			foreach (MobileControlButton button in Layout.Buttons) {
				if (button.Action is MobileControlAction.LauncherControl or MobileControlAction.Interact or MobileControlAction.Grapple or MobileControlAction.QuickPotions or MobileControlAction.Zoom or MobileControlAction.RadialMenu or MobileControlAction.Unbound) continue;
				if (button.Action == MobileControlAction.TogglePointerMode
					&& (contact.Id < 0 || contact.State != TouchLocationState.Pressed)) continue;
				// Closing the bag exposes this HUD button beneath the same contact.
				// A held or moved contact must not immediately open it again.
				if (button.Action is MobileControlAction.Inventory or MobileControlAction.Chat or MobileControlAction.Map or MobileControlAction.MapStyle or MobileControlAction.PerformanceDisplay
					&& contact.State != TouchLocationState.Pressed) continue;
				if (!IsButtonAvailable(button) || !button.Contains(contact.Position)) {
					continue;
				}
				if (!CanActivateCustomButton(button, contact)) continue;

				CurrentActions.Add(button.Action);
				if (button.Action == MobileControlAction.ModKeybind) MobileModKeybinds.Hold(button.ModKeybind);
				if (button.Action == MobileControlAction.KeyboardKey) MobileKeyboardBindings.Hold(button.KeyboardKey);
				HasAnyControlTouch = true;
			}
		}

		foreach (MobileControlAction action in CurrentActions) {
			if (!PreviousActions.Contains(action)) {
				PressedActions.Add(action);
			}
		}
	}

	private static bool CanActivateCustomButton(MobileControlButton button, MobileTouchContact contact)
	{
		if (button.Id == null) return true;
		foreach (MobileTouchContact other in Contacts) {
			if (other.Id == contact.Id || other.IsReleased || MobileInventorySystem.BlocksControl(other.Position)
				|| MobileInventorySystem.OwnsContact(other.Id) || !TouchesAnyControl(other)) continue;
			if (!button.Multitouch || IsExclusiveButtonTouch(other.Id)) return false;
		}
		return true;
	}

	private static bool IsBlockedByExclusiveControl(int contactId)
	{
		foreach (MobileTouchContact other in Contacts) {
			if (other.Id != contactId && !other.IsReleased && IsExclusiveButtonTouch(other.Id)) return true;
		}
		return false;
	}

	private static bool IsExclusiveButtonTouch(int contactId)
	{
		if (Grapple.Active && Grapple.ContactId == contactId && Grapple.Button.Id != null && !Grapple.Button.Multitouch) return true;
		if (InteractHeld && interactFingerId == contactId && interactButton.Id != null && !interactButton.Multitouch) return true;
		foreach (MobileTouchContact contact in Contacts) {
			if (contact.Id != contactId || contact.IsReleased) continue;
			foreach (MobileControlButton button in Layout.Buttons) {
				if (button.Id != null && !button.Multitouch && button.Action != MobileControlAction.Unbound
					&& IsButtonAvailable(button) && button.Contains(contact.Position)) return true;
			}
		}
		return false;
	}

    private static bool TouchesStick(MobileTouchContact contact)
        => TouchesStick(contact, Layout);

    private static bool TouchesStick(MobileTouchContact contact, MobileControlLayout layout)
    {
        if (layout.Sticks == null) return false;
        foreach (var stick in layout.Sticks)
            if (stick.Role is MobileJoystickRole.Move or MobileJoystickRole.Aim && IsStickAvailable(stick)
                && Vector2.DistanceSquared(contact.Position, stick.Center) <= stick.Radius * stick.Radius) return true;
        return false;
    }

	private static bool TouchesAnyControl(MobileTouchContact contact)
	{
		if (IsGrappleContact(contact.Id) || IsInteractContact(contact.Id)) return true;
		if (TouchesStick(contact)) return true;
		foreach (MobileControlButton button in Layout.Buttons)
			if (IsButtonAvailable(button) && button.Contains(contact.Position)) return true;
		return false;
	}

    internal static void ApplyGameplayTriggers()
    {
        // Held projectiles (including InnoVault ranged weapons) read the native
        // trigger directly, after SetControls. Keep it down for the entire drag
        // without synthesizing Main.mouseLeft, which would also click UI.
        bool canUse = EnabledThisFrame && !Main.playerInventory && !Main.mapFullscreen
            && !Main.ingameOptionsWindow && !Main.inFancyUI && Main.LocalPlayer.talkNPC < 0
            && Main.mouseItem.IsAir && !MobileInventorySystem.BlocksWorldUse;
        bool use = canUse && !MobileWorldInteraction.UsesRightTrigger
            && (MobileWorldInteraction.DirectUseHeld || AttackHeld && AimAndUseMode || IsActionDown(MobileControlAction.UseItem));
        if (use) {
            PlayerInput.Triggers.Current.MouseLeft = true;
            PlayerInput.Triggers.JustPressed.MouseLeft |= !gameplayUseWasDown;
            PlayerInput.Triggers.JustReleased.MouseLeft = false;
        }
        else if (gameplayUseWasDown && !PlayerInput.Triggers.Current.MouseLeft) {
            PlayerInput.Triggers.JustReleased.MouseLeft = true;
        }
        gameplayUseWasDown = use;
        if (!EnabledThisFrame || Main.mapFullscreen || MobileInventorySystem.BlocksWorldUse) return;
        ApplyPotionTrigger(MobileControlAction.Heal, "QuickHeal");
        ApplyPotionTrigger(MobileControlAction.Mana, "QuickMana");
        bool mount = IsActionDown(MobileControlAction.Mount) || IsActionDown(MobileControlAction.QuickMount);
        PlayerInput.Triggers.Current.QuickMount |= mount;
        if (mount) {
            PlayerInput.Triggers.JustPressed.QuickMount = !PlayerInput.Triggers.Old.QuickMount;
            PlayerInput.Triggers.JustReleased.QuickMount = false;
        }
    }

    private static void ApplyPotionTrigger(MobileControlAction action, string key)
    {
        if (!IsActionDown(action)) return;
        var triggers = PlayerInput.Triggers;
        triggers.Current.KeyStatus[key] = true;
        triggers.JustPressed.KeyStatus[key] = !triggers.Old.KeyStatus[key];
        triggers.JustReleased.KeyStatus[key] = false;
    }

	private static void ApplyImmediateActions()
	{
		Player player = Main.LocalPlayer;
		if (WasActionPressed(MobileControlAction.ShowKeyboard)) MobileTextInputSystem.ShowKeyboard();
		if (WasActionPressed(MobileControlAction.TogglePointerMode)) TogglePointerMode();
		if (WasActionPressed(MobileControlAction.PerformanceDisplay)) {
			MobilePerformanceHud.Cycle();
			SoundEngine.PlaySound(SoundID.MenuTick);
		}

		if (WasActionPressed(MobileControlAction.Inventory)) {
			player.ToggleInv();
			if (Main.playerInventory && MobileInventorySystem.Enabled) MobileInventorySystem.OpenedFromControl();
		}

		if (WasActionPressed(MobileControlAction.MapStyle) || WasActionPressed(MobileControlAction.MapAlphaCycle)) {
			if (Main.mapFullscreen) { Main.mapFullscreen = false; Main.mapStyle = 1; }
			else Main.mapStyle = WasActionPressed(MobileControlAction.MapAlphaCycle)
				? (Main.mapStyle + 1) % 3 : Main.mapStyle == 1 ? 0 : 1;
			SoundEngine.PlaySound(SoundID.MenuTick);
		}

		if (WasActionPressed(MobileControlAction.Map)) {
			if (Main.mapFullscreen) {
				Main.mapFullscreen = false;
				SoundEngine.PlaySound(SoundID.MenuClose);
			}
			else {
				MobileChatSystem.Hide();
				player.TryOpeningFullscreenMap();
			}
		}

		if (WasActionPressed(MobileControlAction.Settings)) {
			IngameOptions.Open();
		}

		if (WasActionPressed(MobileControlAction.ToggleGameControls)) {
			MobileJoystickConfig.Instance.HideGameControls = !MobileJoystickConfig.Instance.HideGameControls;
			if (MobileJoystickConfig.Instance.HideGameControls) {
				JoystickVector = AttackKnobOffset = Vector2.Zero;
				AttackHeld = AttackJustPressed = false;
				MobileWorldInteraction.Reset();
				ResetTouchAim();
			}
			ShowStatusText(MobileSettingsCatalog.Text(MobileJoystickConfig.Instance.HideGameControls ? "GameControlsHidden" : "GameControlsShown"));
			SoundEngine.PlaySound(SoundID.MenuTick);
		}

		if (WasActionPressed(MobileControlAction.AimModeToggle)) {
			CycleAimStickMode();
			SoundEngine.PlaySound(SoundID.MenuTick);
		}

		if (WasActionPressed(MobileControlAction.TargetLockToggle)) {
			TargetLockEnabled = !TargetLockEnabled;
			LockOnHelper.ForceUsability = TargetLockEnabled;
			if (TargetLockEnabled) {
				LockOnHelper.Toggle();
				ShowStatusText(MobileSettingsCatalog.Text("TargetLockEnabled"));
			}
			else {
				LockOnHelper.Toggle(forceOff: true);
				ShowStatusText(MobileSettingsCatalog.Text("TargetLockDisabled"));
			}

			SoundEngine.PlaySound(SoundID.MenuTick);
		}

		if (WasActionPressed(MobileControlAction.SmartCursor)) {
			SmartCursorEnabled = !SmartCursorEnabled;
			ShowStatusText(MobileSettingsCatalog.Text(SmartCursorEnabled ? "SmartCursorEnabled" : "SmartCursorDisabled"));
			SoundEngine.PlaySound(SoundID.MenuTick);
		}

		if (WasActionPressed(MobileControlAction.Chat)) {
			MobileChatSystem.Toggle();
		}

		if (WasActionPressed(MobileControlAction.CycleUseProfile)) LockOnHelper.CycleUseModes();
		if (WasActionPressed(MobileControlAction.Loadout1)) player.TrySwitchingLoadout(0);
		if (WasActionPressed(MobileControlAction.Loadout2)) player.TrySwitchingLoadout(1);
		if (WasActionPressed(MobileControlAction.Loadout3)) player.TrySwitchingLoadout(2);
		if (WasActionPressed(MobileControlAction.SwitchLoadout)) player.TrySwitchingLoadout((player.CurrentLoadoutIndex + 1) % player.Loadouts.Length);
	}

	private static void ShowStatusText(string text)
	{
		StatusText = text;
		statusTextTime = StatusTextFrames;
	}

	private static void TogglePointerMode()
	{
		var config = MobileJoystickConfig.Instance;
		bool wasTrackpad = config.ShowMouseCursor;
		config.ShowMouseCursor = !wasTrackpad;
		try { config.SaveFromMobileMenu(); }
		catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException) {
			config.ShowMouseCursor = wasTrackpad;
			MobileControlsSession.Logger.Warn("Unable to save pointer mode", error);
			ShowStatusText(MobileSettingsCatalog.Text("UnableToSaveSettings"));
			return;
		}
		if (wasTrackpad) MobileTrackpad.SuppressDirectPointerUntilRelease();
		Main.mouseLeft = PlayerInput.Triggers.Current.MouseLeft = PlayerInput.Triggers.JustPressed.MouseLeft = false;
		ShowStatusText(MobileSettingsCatalog.Text(wasTrackpad ? "DirectTouchMode" : "TrackpadMode"));
		SoundEngine.PlaySound(SoundID.MenuTick);
	}

	private static void CycleAimStickMode()
	{
		aimStickMode = aimStickMode switch {
			AimStickMode.LeftClick => AimStickMode.AimOnly,
			AimStickMode.AimOnly => AimStickMode.RightClick,
			_ => AimStickMode.LeftClick
		};
		ShowStatusText(AimModeLabel);
	}

	public static Item GetPriorityRightClickOrHeldItem()
	{
		if (Main.myPlayer < 0 || Main.myPlayer >= Main.maxPlayers) {
			return null;
		}

		Player player = Main.LocalPlayer;
		return player.HeldItem;
	}

	public static Item GetHeldItem()
	{
		if (Main.myPlayer < 0 || Main.myPlayer >= Main.maxPlayers) {
			return null;
		}

		return Main.LocalPlayer.HeldItem;
	}

	internal static void UseRightClickItem(Player player, int slot)
	{
		bool oldMouseRight = Main.mouseRight;
		bool oldMouseRightRelease = Main.mouseRightRelease;
		try {
			Main.mouseRight = true;
			Main.mouseRightRelease = true;
			ItemSlot.RightClick(player.inventory, ItemSlot.Context.InventoryItem, slot);
		}
		finally {
			Main.mouseRight = oldMouseRight;
			Main.mouseRightRelease = oldMouseRightRelease;
		}
	}
}
