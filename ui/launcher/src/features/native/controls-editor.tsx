import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { Button, Icon, Switch } from '../../components/ui';
import { LabeledSelect } from '../../components/form-controls';
import { useTranslation } from '../../i18n';
import { nativeRequest } from '../../native/bridge';
import type { ControlArtworkMap } from './control-artwork';
import { useLauncher } from '../../state/launcher';
import {
  componentTypes,
  ControlBindings,
  ControlTexturePicker,
  RadialPreview,
  Range,
  type ControlData,
} from './controls-properties';
import { useLayoutGesture } from './use-layout-gesture';
import { useFloatingPosition } from './use-floating-position';
import { ButtonSurface } from './game-button';

type Category = 'controls' | 'interface';
type Viewport = { width: number; height: number };
type Position = { CenterX: number; CenterY: number; Scale: number };
type Control = {
  Id: string;
  Kind: number | string;
  JoystickRole?: number | string;
  Action?: number | string;
  TextureName?: string;
  CustomTexture?: string;
  X: number;
  Y: number;
  Size: number;
  Multitouch?: boolean;
  KeyboardKey?: string;
  ModKeybind?: string;
  RadialActions?: (number | string)[];
  RadialKeyboardKeys?: string[];
  RadialModKeybinds?: string[];
  Launcher?: { Data: ControlData } | null;
};
type Preset = { Name: string; Controls: Control[] };
type Profile = {
  ControlLayout?: number | string;
  ActiveCustomPresetIndex?: number;
  CustomPresets?: (Preset | null)[];
  ControlScale?: number;
  Opacity?: number;
  JoystickDeadzone?: number;
  JoystickUpJumps?: boolean;
  EnablePinchZoom?: boolean;
  ShowTopButtons?: boolean;
  LeftStickOffsetX?: number;
  LeftStickOffsetY?: number;
  RightStickOffsetX?: number;
  RightStickOffsetY?: number;
  UIFontScale?: number;
  EdgePadding?: number;
  HotbarOrientation?: number | string;
  InterfaceLayout?: { FormatVersion: number; Regions: Record<string, Position> };
  EnableTouchInput?: boolean;
  HideGameControls?: boolean;
};
type PreviewScene = 'world' | 'hotbar' | 'inventory' | 'crafting';
const layoutKeys = [
  'ControlLayout',
  'ActiveCustomPresetIndex',
  'CustomPresets',
  'ControlScale',
  'LeftStickOffsetX',
  'LeftStickOffsetY',
  'RightStickOffsetX',
  'RightStickOffsetY',
  'Opacity',
  'JoystickDeadzone',
  'JoystickUpJumps',
  'EnablePinchZoom',
  'ShowTopButtons',
  'EnableTouchInput',
  'HideGameControls',
  'UIFontScale',
  'HotbarOrientation',
  'InterfaceLayout',
  'EdgePadding',
] as const satisfies readonly (keyof Profile)[];

const defaultControlMode = 1;
const defaultControlScale = 1.8;
const gameControlScale = (profile: Profile, { width, height }: Viewport) =>
  (profile.ControlScale ?? defaultControlScale) * Math.min(width / 1920, height / 1080);

function hotbarGeometry(profile: Profile, viewport: Viewport) {
  const scale = gameControlScale(profile, viewport);
  const inset = Math.max(4, Math.trunc((profile.EdgePadding ?? 18) * scale * 0.5));
  const top = Math.max(4, Math.trunc(6 * scale));
  const side = Math.max(16, Math.trunc(38 * scale));
  const slot = Math.max(16, Math.trunc(50 * scale));
  const gap = Math.max(2, Math.trunc(3 * scale));
  const horizontal = profile.HotbarOrientation === 1 || profile.HotbarOrientation === 'Horizontal';
  const settingsY = top + (slot + gap) * (horizontal ? 1 : 4);
  return {
    inset,
    left: Math.max(inset, Math.trunc(viewport.width * 0.047)),
    top,
    side,
    slot,
    gap,
    horizontal,
    settingsY,
    width: side + (horizontal ? 10 : 2) * (slot + gap),
    height: horizontal ? settingsY - top + side : 5 * slot + 4 * gap,
  };
}

function interfaceRegions(
  profile: Profile,
  { width, height }: Viewport,
  hotbar: ReturnType<typeof hotbarGeometry>,
) {
  const padding = profile.EdgePadding ?? 18;
  const screenScale = Math.min(width / 1280, height / 576);
  const inset = Math.ceil(Math.max(18, padding) * screenScale);
  const topInset = Math.max(2, Math.trunc(4 * screenScale));
  const usable = width - inset * 2;
  const scale = Math.min(usable / 1280, (height - 2 * topInset) / 576);
  const region = (
    id: string,
    label: string,
    x: number,
    y: number,
    regionWidth: number,
    regionHeight: number,
  ) => ({
    id,
    label,
    x: (x + regionWidth / 2) / width,
    y: (y + regionHeight / 2) / height,
    width: regionWidth / width,
    height: regionHeight / height,
  });
  return [
    {
      ...region('Hotbar', '快捷栏', hotbar.left, hotbar.top, hotbar.width, hotbar.height),
      insetX: hotbar.inset / width,
      insetY: 4 / height,
    },
    ...[
      region(
        'InventoryItems',
        '背包格子',
        inset + 54 * scale,
        topInset + 4 * scale,
        498 * scale,
        248 * scale,
      ),
      region(
        'CoinAmmo',
        '钱币与弹药',
        inset + 572 * scale,
        topInset + 60 * scale,
        94 * scale,
        194 * scale,
      ),
      region('InventoryActions', '背包操作', inset, topInset, usable, 355 * scale),
      region(
        'Equipment',
        '装备区',
        width - inset - 460 * scale,
        topInset,
        460 * scale,
        355 * scale,
      ),
      region('Crafting', '制作区', width - inset - 460 * scale, topInset, 460 * scale, 400 * scale),
      region(
        'CalamityDifficulty',
        '灾厄难度按钮',
        width - inset - 260 * scale,
        topInset + 260 * scale,
        80 * scale,
        80 * scale,
      ),
    ].map((entry) => ({ ...entry, insetX: inset / width, insetY: topInset / height })),
  ];
}
const ACTIONS = [
  'Jump',
  'Grapple',
  'Heal',
  'Mana',
  'Buff',
  'Mount',
  'SmartInteract',
  'SmartCursor',
  'AimModeToggle',
  'TargetLockToggle',
  'Interact',
  'UseItem',
  'Inventory',
  'Map',
  'MapStyle',
  'Settings',
  'Chat',
  'ZoomIn',
  'ZoomOut',
  'QuickPotions',
  'Zoom',
  'RadialMenu',
  'Unbound',
  'SwapUseInteract',
  'CycleUseProfile',
  'QuickMount',
  'AutoSelect',
  'Loadout1',
  'Loadout2',
  'Loadout3',
  'SwitchLoadout',
  'ModKeybind',
  'LauncherControl',
  'KeyboardKey',
  'ToggleGameControls',
  'MapAlphaCycle',
  'TogglePointerMode',
  'PerformanceDisplay',
  'ShowKeyboard',
];
const ACTION_LABELS = [
  '跳跃',
  '钩爪',
  '治疗',
  '魔力',
  '增益',
  '坐骑',
  '智能互动',
  '智能光标',
  '瞄准模式',
  '锁定',
  '互动',
  '使用物品',
  '背包',
  '地图',
  '地图样式',
  '设置',
  '聊天',
  '放大',
  '缩小',
  '药水',
  '缩放',
  '轮盘',
  '无绑定',
  '使用／互动',
  '切换使用模式',
  '快速坐骑',
  '自动选择',
  '装备配置 1',
  '装备配置 2',
  '装备配置 3',
  '切换装备配置',
  '模组按键',
  '扩展控件',
  '键盘按键',
  '隐藏／显示游戏控制',
  '循环地图透明度',
  '切换触屏和鼠标',
  '性能显示',
  '弹出输入法',
];
const clamp = (value: number, lower = 0, upper = 1) => Math.min(upper, Math.max(lower, value));
const enumIndex = (value: number | string | undefined, names: readonly string[]) => {
  const index = typeof value === 'number' ? value : names.indexOf(value ?? '');
  return index >= 0 && index < names.length ? index : 0;
};
const isStick = (control: Control) => control.Kind === 0 || control.Kind === 'Joystick';
const controlLabel = (control: Control, t: ReturnType<typeof useTranslation>['t']) =>
  control.Launcher?.Data?.name ||
  t(
    isStick(control)
      ? ['移动', '瞄准', '钩爪摇杆', '未绑定摇杆'][
          enumIndex(control.JoystickRole, ['Move', 'Aim', 'Grapple', 'Unbound'])
        ]
      : control.Kind === 2 || control.Kind === 'RadialMenu'
        ? '轮盘'
        : ACTION_LABELS[enumIndex(control.Action, ACTIONS)],
  );

const actionGlyphs = [
  '↑',
  '⌁',
  '+',
  '✦',
  '✚',
  '◈',
  '↗',
  '⌖',
  '⊕',
  '◎',
  '↗',
  '◆',
  '▤',
  '▱',
  '⌖',
  '☰',
  '···',
  '+',
  '−',
  '✚',
  '⌕',
  '◉',
];

// Match the centered sprite sizing used by DefaultControlTextures.TryDraw.
function Artwork({
  name,
  textures,
  size = 1,
  x = 0,
  y = 0,
  opacity = 1,
  fill = false,
}: {
  name?: string | null;
  textures: ControlArtworkMap;
  size?: number;
  x?: number;
  y?: number;
  opacity?: number;
  fill?: boolean;
}) {
  const image = name ? textures[name] : undefined;
  return image ? (
    <img
      className="controls-art-layer"
      src={image}
      alt=""
      style={{
        left: `${(0.5 + x) * 100}%`,
        top: `${(0.5 + y) * 100}%`,
        width: `${size * 100}%`,
        height: `${size * 100}%`,
        opacity,
        objectFit: fill ? 'fill' : 'contain',
      }}
    />
  ) : null;
}

function InventorySlot({
  textures,
  selected = false,
  icon,
  material = 'Slot',
}: {
  textures: ControlArtworkMap;
  selected?: boolean;
  icon?: string;
  material?: string;
}) {
  const frame = `MobileInventory/${selected ? 'SlotSelected' : material}`;
  return (
    <i data-textured={!!textures[frame]}>
      <Artwork name={frame} textures={textures} fill opacity={selected ? 1 : 0.86} />
      {icon && (
        <Artwork name={`MobileInventory/${icon}`} textures={textures} size={0.68} opacity={0.3} />
      )}
    </i>
  );
}

function InterfaceArtwork({
  id,
  hotbar,
  textures,
  opacity,
  showHotbarButtons = true,
}: {
  id: string;
  hotbar: ReturnType<typeof hotbarGeometry>;
  textures: ControlArtworkMap;
  opacity: number;
  showHotbarButtons?: boolean;
}) {
  if (id === 'Hotbar')
    return (
      <div className="controls-art-hotbar">
        {showHotbarButtons && (
          <span
            className="controls-art-side"
            style={{
              width: `${(hotbar.side / hotbar.width) * 100}%`,
              height: `${(hotbar.side / hotbar.height) * 100}%`,
            }}
          >
            <Artwork name="BackpackIcon" textures={textures} size={0.8} opacity={opacity} />
          </span>
        )}
        <div
          className="controls-art-hotbar-slots"
          style={{
            left: `${((hotbar.side + hotbar.gap) / hotbar.width) * 100}%`,
            width: `${((hotbar.width - hotbar.side - hotbar.gap) / hotbar.width) * 100}%`,
            height: `${((hotbar.horizontal ? hotbar.slot : hotbar.height) / hotbar.height) * 100}%`,
            rowGap: `${(hotbar.gap * 100) / (hotbar.horizontal ? hotbar.slot : hotbar.height)}%`,
            columnGap: `${(hotbar.gap * 100) / (hotbar.width - hotbar.side - hotbar.gap)}%`,
            gridTemplateColumns: `repeat(${hotbar.horizontal ? 10 : 2}, minmax(0, 1fr))`,
            gridTemplateRows: `repeat(${hotbar.horizontal ? 1 : 5}, minmax(0, 1fr))`,
            gridAutoFlow: 'column',
          }}
        >
          {Array.from({ length: 10 }, (_, index) => (
            <InventorySlot key={index} textures={textures} selected={index === 0} />
          ))}
        </div>
        {showHotbarButtons && (
          <span
            className="controls-art-side"
            style={{
              top: `${((hotbar.settingsY - hotbar.top) / hotbar.height) * 100}%`,
              width: `${(hotbar.side / hotbar.width) * 100}%`,
              height: `${(hotbar.side / hotbar.height) * 100}%`,
            }}
          >
            <Artwork name="Settings" textures={textures} size={0.8} opacity={opacity} />
          </span>
        )}
      </div>
    );
  if (id === 'InventoryItems')
    return (
      <div className="controls-art-slots inventory">
        {Array.from({ length: 50 }, (_, index) => (
          <InventorySlot key={index} textures={textures} selected={index === 0} />
        ))}
      </div>
    );
  if (id === 'CoinAmmo')
    return (
      <div className="controls-art-slots coin-ammo">
        {Array.from({ length: 8 }, (_, index) => (
          <InventorySlot
            key={index}
            textures={textures}
            icon={index < 4 ? 'EmptyCoin' : 'EmptyAmmo'}
          />
        ))}
      </div>
    );
  if (id === 'Equipment')
    return (
      <div className="controls-art-equipment">
        {Array.from({ length: 16 }, (_, index) => (
          <InventorySlot
            key={index}
            textures={textures}
            material={index % 3 === 0 ? 'SlotGreen' : index % 3 === 1 ? 'SlotTeal' : 'SlotPurple'}
            icon={
              [
                'EmptyHead',
                'EmptyBody',
                'EmptyLegs',
                'EmptyAccessory',
                'EmptyDye',
                'EmptyGrapple',
                'EmptyMount',
                'EmptyPet',
              ][index % 8]
            }
          />
        ))}
      </div>
    );
  if (id === 'Crafting')
    return (
      <div className="controls-art-crafting">
        <div className="controls-art-categories">
          {Array.from({ length: 6 }, (_, index) => (
            <InventorySlot
              key={index}
              textures={textures}
              icon={`Category${[0, 2, 4, 5, 8, 9][index]}`}
            />
          ))}
        </div>
        <div className="controls-art-slots recipes">
          {Array.from({ length: 15 }, (_, index) => (
            <InventorySlot key={index} textures={textures} />
          ))}
        </div>
        <span className="controls-art-craft-button">
          <Artwork name="MobileInventory/Craft" textures={textures} />
        </span>
      </div>
    );
  if (id === 'InventoryActions')
    return (
      <div className="controls-art-actions">
        {['QuickStack', 'Sort', 'Restock', 'CraftLock', 'Settings'].map((name) => (
          <span key={name}>
            <Artwork name={`MobileInventory/${name}`} textures={textures} size={0.82} />
          </span>
        ))}
      </div>
    );
  if (id === 'CalamityDifficulty') return <div className="controls-art-difficulty">☠</div>;
  return null;
}

function ControlArtwork({
  control,
  mode,
  data,
  textures,
  builtIn,
  jumpHint,
}: {
  control: Control;
  mode: string;
  data?: ControlData;
  textures: ControlArtworkMap;
  builtIn: boolean;
  jumpHint: boolean;
}) {
  if (control.CustomTexture?.startsWith('data:image/png;base64,')) {
    return (
      <img className="controls-art-texture" src={control.CustomTexture} alt="" draggable={false} />
    );
  }
  // Imported launcher controls deliberately keep their own shapes and paints.
  if (data) {
    if (data.type === 'joystick') {
      return (
        <>
          <span
            className="controls-art-ral-pad"
            style={{ opacity: Number(data.opacity ?? 0.35) * 0.8 }}
          />
          <span
            className="controls-art-ral-knob"
            style={{
              width: `${Number(data.stickKnobSize ?? 0.4) * 100}%`,
              height: `${Number(data.stickKnobSize ?? 0.4) * 100}%`,
              opacity: Number(data.stickOpacity ?? 0.7),
            }}
          />
        </>
      );
    }
    return (
      <>
        <Artwork name={data.nativeTexture as string | undefined} textures={textures} />
        {data.type === 'button' && <span className="controls-art-launcher-label">{data.name}</span>}
        {data.type === 'radialmenu' && !data.nativeTexture && <Icon name="sun" />}
      </>
    );
  }
  const buttonAction = ACTIONS[enumIndex(control.Action, ACTIONS)];
  const toolbarGlyph: Record<string, string> = { PerformanceDisplay: '▂▄▆', TogglePointerMode: '▣', ShowKeyboard: '⌨' };
  if (!isStick(control) && toolbarGlyph[buttonAction]) {
    return (
      <>
        <Artwork name="PhoneTealCircle" textures={textures} />
        <span className="controls-art-pointer-mode" aria-hidden="true">
          {toolbarGlyph[buttonAction]}
        </span>
      </>
    );
  }
  const role = enumIndex(control.JoystickRole, STICK_ROLES);
  if (textures.JoystickPad && isStick(control) && role !== 2) {
    const defaultKnob = role === 0 ? 'JoystickKnob' : 'AttackAimUse';
    const selectedKnob = control.TextureName;
    const knob =
      selectedKnob &&
      selectedKnob !== defaultKnob &&
      !(role === 1 && selectedKnob === 'AttackAimOnly')
        ? selectedKnob
        : role === 0
          ? 'JoystickKnob'
          : role === 1
            ? 'AttackAimOnly'
            : 'ItemButtonFrame';
    return (
      <>
        <Artwork name="JoystickPad" textures={textures} />
        <Artwork name={knob} textures={textures} size={0.5} />
        {role === 0 && jumpHint && (
          <Artwork
            name="MoveJumpHint"
            textures={textures}
            y={-0.38}
            size={25 / (132 * control.Size)}
          />
        )}
      </>
    );
  }
  if (textures.PhoneTealCircle && (!isStick(control) || role === 2)) {
    const action =
      role === 2 && isStick(control)
        ? 1
        : control.Kind === 2 || control.Kind === 'RadialMenu'
          ? 21
          : enumIndex(control.Action, ACTIONS);
    // Keep these rules aligned with MobileControlArtwork and MobileHudArtwork.
    const defaults = [
      'Jump',
      'GrappleHook',
      'Heal',
      'Mana',
      'Buff',
      'Mount',
      'SmartCursor',
      'SmartCursor',
      'AimUseMode',
      'TargetLockOn',
      'ItemButtonFrame',
      'ItemButtonFrame',
      'BackpackIcon',
      'Map',
      'MapStyle',
      'Settings',
      'ChatBubble',
      'ZoomIn',
      'ZoomOut',
      'QuickPotions',
      'ZoomPair',
      'DirectionalArrowsOriginal',
      'ItemButtonFrame',
      'ItemButtonFrame',
      'SmartCursor',
      'Mount',
      'SmartCursor',
      'BackpackIcon',
      'BackpackIcon',
      'BackpackIcon',
      'BackpackIcon',
    ];
    const selected = control.TextureName;
    if (selected && selected !== defaults[action] && textures[selected])
      return <Artwork name={selected} textures={textures} />;
    if ([13, 14, 16].includes(action)) {
      const glyph = action === 16 ? 'HudChat' : action === 14 ? 'HudMinimapSelected' : 'HudMap';
      return (
        <>
          <span
            className="controls-art-toolbar"
            style={{ borderImageSource: `url("${textures.HudToolbar}")` }}
          />
          <Artwork name={glyph} textures={textures} size={0.72} opacity={0.95} />
        </>
      );
    }
    if (action === 19)
      return (
        <>
          <Artwork name="PhoneTealCircle" textures={textures} />
          <Artwork name="Items/Item_28" textures={textures} size={20 / 64} y={-0.25} />
          <Artwork name="Items/Item_110" textures={textures} size={20 / 64} x={0.25} />
          <Artwork name="Items/Item_2349" textures={textures} size={20 / 64} y={0.25} />
        </>
      );
    const background = [0, 7, 8, 9].includes(action)
      ? 'PhoneTealCircle'
      : [1, 5, 25].includes(action)
        ? 'PhoneGreenCircle'
        : [2, 3, 4, 6, 10, 11, 21, 22, 23].includes(action)
          ? 'PhonePurpleCircle'
          : null;
    const glyphs = [
      'PhoneJumpGlyph',
      'PhoneEmptyGrappleGlyph',
      'Items/Item_28',
      'Items/Item_110',
      'Items/Item_2349',
      'PhoneMountGlyph',
      'PhoneSmartInteractGlyph',
      'PhoneSmartCursorOnGlyph',
      'PhoneAimModeGlyph',
      'PhoneTargetGlyph',
      'PhoneInteractGlyph',
      'PhoneUseGlyph',
    ];
    const glyph =
      glyphs[action] ??
      (action === 22
        ? 'PhoneUnboundGlyph'
        : action === 23
          ? 'PhoneSwapGlyph'
          : action === 21
            ? 'PhoneRadialGlyph'
            : defaults[action]);
    return (
      <>
        <Artwork name={background} textures={textures} />
        <Artwork
          name={glyph}
          textures={textures}
          size={background ? 0.675 : builtIn && [12, 15].includes(action) ? 0.8 : 1}
        />
      </>
    );
  }
  if (isStick(control)) {
    const aim = enumIndex(control.JoystickRole, ['Move', 'Aim', 'Grapple', 'Unbound']) === 1;
    const pad = textures.JoystickPad;
    const knob = textures[aim ? 'AttackAimUse' : 'JoystickKnob'];
    return (
      <>
        <span className="controls-art-stick-ring">
          {pad && <img className="controls-art-texture" src={pad} alt="" />}
        </span>
        <span className={`controls-art-stick-knob${knob ? ' has-texture' : ''}`}>
          {knob ? <img className="controls-art-texture" src={knob} alt="" /> : aim ? '⌖' : '✥'}
        </span>
      </>
    );
  }
  if (control.Kind === 2 || control.Kind === 'RadialMenu')
    return (
      <span className="controls-art-radial">
        {textures.PhoneRadialGlyph ? (
          <img className="controls-art-texture" src={textures.PhoneRadialGlyph} alt="" />
        ) : (
          <>
            <i>↑</i>
            <i>→</i>
            <i>↓</i>
            <i>←</i>
          </>
        )}
      </span>
    );
  const action = enumIndex(control.Action, ACTIONS);
  const textureNames = [
    'PhoneJumpGlyph',
    'PhoneEmptyGrappleGlyph',
    'Heal',
    'Mana',
    'Buff',
    'PhoneMountGlyph',
    'PhoneSmartInteractGlyph',
    'PhoneSmartCursorGlyph',
    'PhoneAimModeGlyph',
    'PhoneTargetGlyph',
    'PhoneInteractGlyph',
    'PhoneUseGlyph',
    'PhoneInventoryGlyph',
    'Map',
    'MapStyle',
    'PhoneMenuGlyph',
    'Chat',
    'PhoneZoomInGlyph',
    'PhoneZoomOutGlyph',
    'QuickPotions',
    'ZoomPair',
    'PhoneRadialGlyph',
    'PhoneUnboundGlyph',
    'PhoneSwapGlyph',
    'PhoneUseProfileGlyph',
    'PhoneMountGlyph',
    'PhoneAutoSelectGlyph',
    'PhoneLoadoutGlyph',
    'PhoneLoadoutGlyph',
    'PhoneLoadoutGlyph',
    'PhoneLoadoutGlyph',
    null,
    null,
    null,
  ];
  const texture = control.TextureName || textureNames[action];
  if (texture && textures[texture])
    return (
      <img
        className="controls-art-texture controls-art-action-texture"
        src={textures[texture]}
        alt=""
      />
    );
  const symbol = mode === 'Advanced' && action === 20 ? '⌕' : (actionGlyphs[action] ?? '●');
  return <span className="controls-art-button">{symbol}</span>;
}

function defaultControls(
  profile: Profile,
  mode: number,
  viewport: Viewport,
  hotbar: ReturnType<typeof hotbarGeometry>,
  hotbarPosition: Position | undefined,
): Control[] {
  const { width, height } = viewport;
  const scale = gameControlScale(profile, viewport);
  const edge = (profile.EdgePadding ?? 18) * scale * 0.5;
  const stickY = height - 104 * scale - edge;
  const left = Math.max(width * 0.19, 166 * scale + edge) + (profile.LeftStickOffsetX ?? 0);
  const right =
    width - Math.max(width * 0.145, 166 * scale + edge) + (profile.RightStickOffsetX ?? 0);
  const leftY = stickY + (profile.LeftStickOffsetY ?? 0);
  const rightY = stickY + (profile.RightStickOffsetY ?? 0);
  const button = (action: number, x: number, y: number, size = 1): Control => ({
    Id: `button-${ACTIONS[action]}`,
    Kind: 1,
    Action: action,
    X: x / width,
    Y: y / height,
    Size: size,
  });
  const near = (action: number, hand: 'left' | 'right', x: number, y: number, size?: number) =>
    button(
      action,
      (hand === 'left' ? left : right) + x * scale,
      (hand === 'left' ? leftY : rightY) + y * scale,
      size,
    );
  const hotbarButton = (action: number, y: number) => {
    const size = hotbarPosition?.Scale ?? 1;
    const halfSide = Math.floor(Math.max(1, Math.round(hotbar.side * size)) / 2);
    const x = hotbarPosition
      ? Math.round(hotbarPosition.CenterX * width - Math.floor(hotbar.width / 2) * size)
      : hotbar.left;
    const top = hotbarPosition
      ? Math.round(
          hotbarPosition.CenterY * height + (y - hotbar.top - Math.floor(hotbar.height / 2)) * size,
        )
      : y;
    return button(action, x + halfSide, top + halfSide, (hotbar.side * size) / (48 * scale));
  };
  const controls: Control[] = [
    { Id: 'joystick-move', Kind: 0, JoystickRole: 0, X: left / width, Y: leftY / height, Size: 1 },
    { Id: 'joystick-aim', Kind: 0, JoystickRole: 1, X: right / width, Y: rightY / height, Size: 1 },
    near(1, 'right', -106, -84, 32 / 24),
    near(0, 'right', 122, -84, 32 / 24),
    near(5, 'right', 126, 7),
    near(9, 'right', -104, 80),
    near(7, 'right', 104, 80),
    near(10, 'left', -106, -84, 32 / 24),
    near(8, 'left', -96, 80),
    near(11, 'right', -126, 7, 25 / 24),
    hotbarButton(12, hotbar.top),
    hotbarButton(15, hotbar.settingsY),
  ];
  if (mode === 1) {
    controls.push(near(19, 'left', 110, -84, 32 / 24), near(20, 'left', 104, 80));
  } else {
    controls.push(
      near(2, 'left', 110, -84, 32 / 24),
      near(3, 'left', 128, 7),
      near(4, 'left', 106, 80),
      near(18, 'left', 176, 80, 16 / 24),
      near(17, 'right', -176, 80, 16 / 24),
    );
  }
  const size = Math.trunc(40 * scale);
  const toolbarActions = [ACTIONS.indexOf('PerformanceDisplay'), 16, 14, 13];
  toolbarActions.forEach((action, index) => {
    if (index > 0 && profile.ShowTopButtons === false) return;
    controls.push(
      button(
        action,
        width - hotbar.inset - size * (4 - index) + Math.floor(size / 2),
        hotbar.top + Math.floor(size / 2),
        size / (48 * scale),
      ),
    );
  });
  return controls;
}

const STICK_ROLES = ['Move', 'Aim', 'Grapple', 'Unbound'];
const STICK_LABELS = ['移动', '瞄准', '钩爪', '无绑定'];
const KEYBOARD_KEYS = [
  ...'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789',
  ...[
    'SPACE',
    'RETURN',
    'ESCAPE',
    'TAB',
    'UP',
    'DOWN',
    'LEFT',
    'RIGHT',
    'LSHIFT',
    'LCTRL',
    'LALT',
    'BACKSPACE',
  ],
  ...Array.from({ length: 12 }, (_, index) => 'F' + (index + 1)),
];
const KEY_BINDINGS: [string, string][] = [
  ['UNKNOWN', '无绑定'],
  ...ACTIONS.flatMap((action, index): [string, string][] =>
    [19, 20, 21, 22, 31, 32, 33].includes(index)
      ? []
      : [[`ACTION_${action}`, ACTION_LABELS[index]]],
  ),
  ['MOUSE_LEFT', '鼠标左键'],
  ['MOUSE_RIGHT', '鼠标右键'],
  ['MOUSE_MIDDLE', '鼠标中键'],
  ...KEYBOARD_KEYS.map((key): [string, string] => ['KEYBOARD_' + key, key]),
];

function controlBounds(entry: Control, profile: Profile, customMode: boolean, viewport: Viewport) {
  const data = entry.Launcher?.Data;
  const action = enumIndex(entry.Action, ACTIONS);
  const size =
    customMode && !isStick(entry) && action === 12 ? Math.max(1, entry.Size) : entry.Size;
  const radius = (isStick(entry) ? 66 : 24) * gameControlScale(profile, viewport) * size;
  const width =
    (data ? (data.width ?? 0.16) * size * viewport.height : radius * 2) / viewport.width;
  const height = data ? (data.height ?? 0.16) * size : (radius * 2) / viewport.height;
  const x = data
    ? entry.X + width / 2
    : customMode
      ? clamp(entry.X, Math.min(0.5, width / 2), Math.max(0.5, 1 - width / 2))
      : entry.X;
  const y = data
    ? entry.Y + height / 2
    : customMode
      ? clamp(entry.Y, Math.min(0.5, height / 2), Math.max(0.5, 1 - height / 2))
      : entry.Y;
  return { x, y, width, height };
}

export function ControlsEditor({ controlTextures, onClose }: {
  controlTextures: ControlArtworkMap;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const { notify } = useLauncher();
  const [category, setCategory] = useState<Category>('controls');
  const [profile, setProfile] = useState<Profile>();
  const [failure, setFailure] = useState('');
  const [pending, setPending] = useState(false);
  const [selected, setSelected] = useState('');
  const [scene, setScene] = useState<PreviewScene>('world');
  const [viewport, setViewport] = useState<Viewport>();
  const [showToolbar, setShowToolbar] = useState(true);
  const [grid, setGrid] = useState(true);
  const [snap, setSnap] = useState(true);
  const [zoom, setZoom] = useState(100);
  const [previewOnly, setPreviewOnly] = useState(false);
  const [panel, setPanel] = useState<'menu' | 'palette' | 'inspector' | 'selection'>();
  const [selectionPanelRight, setSelectionPanelRight] = useState(false);
  const [dock, setDock] = useState({ x: 1, y: 0.5 });
  const menuPosition = useFloatingPosition();
  const palettePosition = useFloatingPosition();
  const inspectorPosition = useFloatingPosition();
  const toolbarPosition = useFloatingPosition();
  const [confirmExit, setConfirmExit] = useState(false);
  const saved = useRef<Profile | undefined>(undefined);
  const history = useRef<{ past: Profile[]; future: Profile[] }>({ past: [], future: [] });
  const dialog = useRef<HTMLDialogElement>(null);
  const preview = useRef<HTMLDivElement>(null);
  const dockDrag = useRef<{
    id: number;
    startX: number;
    startY: number;
    width: number;
    height: number;
    moved: boolean;
  } | null>(null);
  const requestClose = () => {
    if (pending) return;
    if (profile && layoutKeys.some((key) => saved.current?.[key] !== profile[key]))
      setConfirmExit(true);
    else onClose();
  };
  const toggleMenu = () => {
    if (!profile) requestClose();
    else setPanel(panel === 'menu' ? undefined : 'menu');
  };
  const changeCategory = (next: Category) => {
    if (next === category) return;
    setCategory(next);
    setScene(next === 'controls' ? 'world' : 'hotbar');
    setSelected(next === 'controls' ? '' : 'Hotbar');
    setPanel(undefined);
  };
  useLayoutEffect(() => {
    const element = dialog.current;
    if (!element) return;
    const opener = document.activeElement;
    element.showModal();
    const measure = () => {
      const ratio = window.devicePixelRatio;
      setViewport({
        width: Math.round(element.clientWidth * ratio),
        height: Math.round(element.clientHeight * ratio),
      });
    };
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(element);
    void nativeRequest('controls.editor', { active: true }).catch(() => {});
    return () => {
      observer.disconnect();
      element.close();
      if (opener instanceof HTMLElement && opener.isConnected)
        opener.focus({ preventScroll: true });
      void nativeRequest('controls.editor', { active: false }).catch(() => {});
    };
  }, []);

  useEffect(() => {
    let mounted = true;
    void nativeRequest<Profile>('controls.read').then(
      (value) => {
        if (!mounted) return;
        saved.current = value;
        setProfile(value);
      },
      (error: Error) => {
        if (mounted) setFailure(error.message);
      },
    );
    return () => {
      mounted = false;
    };
  }, []);

  const controlEditor = category === 'controls';
  const edit = (changes: Partial<Profile>) => {
    if (!profile) return;
    history.current.past = [...history.current.past.slice(-39), profile];
    history.current.future = [];
    setProfile({ ...profile, ...changes });
  };
  const traverse = (forward: boolean) => {
    if (!profile) return;
    const { past, future } = history.current;
    const next = (forward ? future : past).pop();
    if (!next) return;
    (forward ? past : future).push(profile);
    setProfile(next);
    if (panel === 'selection') setPanel(undefined);
  };

  const save = async () => {
    if (!profile || pending) return;
    setPending(true);
    setFailure('');
    try {
      await nativeRequest('controls.save', {
        category: 'layout',
        settings: Object.fromEntries(
          layoutKeys
            .filter((key) => profile[key] !== saved.current?.[key])
            .map((key) => [key, profile[key]]),
        ),
      });
      saved.current = profile;
      onClose();
      notify(t('布局已保存，下次启动游戏时生效。'));
    } catch (error) {
      setFailure(error instanceof Error ? error.message : '布局保存失败');
    } finally {
      setPending(false);
    }
  };

  const clearSelection = () => {
    setSelected('');
    setPanel(undefined);
  };
  const dragProps = useLayoutGesture(preview, snap, setSelected, (id, node) => {
    const bounds = node.getBoundingClientRect();
    setSelectionPanelRight(bounds.left + bounds.width / 2 < window.innerWidth / 2);
    setSelected(id);
    setPanel('selection');
  });

  const renderEditor = (profile: Profile, viewport: Viewport) => {
    const mode = enumIndex(profile.ControlLayout ?? defaultControlMode, [
      'Simple',
      'Advanced',
      'Custom',
    ]);
    const presetIndex = clamp(profile.ActiveCustomPresetIndex ?? 0, 0, 3);
    const preset = profile.CustomPresets?.[presetIndex];
    const customControls = mode === 2 ? preset?.Controls : undefined;
    const builtInMode = mode === 2 ? defaultControlMode : mode;
    const hotbar = hotbarGeometry(profile, viewport);
    const regions = interfaceRegions(profile, viewport, hotbar);
    const region = regions.find((entry) => entry.id === selected) ?? regions[0];
    const positions =
      profile.InterfaceLayout?.FormatVersion === 1 ? profile.InterfaceLayout.Regions : {};
    const maxScale = (entry: typeof region) =>
      clamp(
        Math.min((1 - 2 * entry.insetX) / entry.width, (1 - 2 * entry.insetY) / entry.height),
        0.5,
        2,
      );
    const locateRegion = (entry: typeof region) => {
      const position = positions?.[entry.id];
      if (!position) return { CenterX: entry.x, CenterY: entry.y, Scale: 1 };
      const scale = clamp(position.Scale, 0.5, maxScale(entry));
      const insetX = entry.insetX + (entry.width * scale) / 2;
      const insetY = entry.insetY + (entry.height * scale) / 2;
      return {
        CenterX: clamp(position.CenterX, Math.min(0.5, insetX), Math.max(0.5, 1 - insetX)),
        CenterY: clamp(position.CenterY, Math.min(0.5, insetY), Math.max(0.5, 1 - insetY)),
        Scale: scale,
      };
    };
    const controls =
      customControls ??
      defaultControls(
        profile,
        builtInMode,
        viewport,
        hotbar,
        positions?.Hotbar ? locateRegion(regions[0]) : undefined,
      );
    const control = controls.find((entry) => entry.Id === selected);
    const selectionOnly = panel === 'selection';
    const editRegion = (id: string, position?: Position) => {
      const next = { ...positions };
      if (position) next[id] = position;
      else delete next[id];
      edit({ InterfaceLayout: { FormatVersion: 1, Regions: next } });
    };
    const editPreset = (next: Control[] | null, name = preset?.Name) => {
      const presets = Array.from(
        { length: 4 },
        (_, index) => profile.CustomPresets?.[index] ?? null,
      );
      presets[presetIndex] = next
        ? { Name: name ?? t('预设 {number}', { number: presetIndex + 1 }), Controls: next }
        : null;
      edit({ CustomPresets: presets, ActiveCustomPresetIndex: presetIndex, ControlLayout: 2 });
    };
    const editControl = (id: string, changes: Partial<Control>) =>
      editPreset(controls.map((entry) => (entry.Id === id ? { ...entry, ...changes } : entry)));
    const newControl = (kind: number, data?: ControlData) => {
      const Id = globalThis.crypto.randomUUID();
      editPreset([
        ...controls,
        {
          Id,
          Kind: kind,
          Action: data ? 32 : 0,
          JoystickRole: data ? 3 : 0,
          X: 0.5,
          Y: 0.5,
          Size: 1,
          ...(data && { Launcher: { Data: data } }),
        },
      ]);
      setSelected(Id);
      setPanel(undefined);
    };
    const duplicate = () => {
      if (!control) return;
      const Id = globalThis.crypto.randomUUID();
      editPreset([
        ...controls,
        { ...control, Id, X: clamp(control.X + 0.025), Y: clamp(control.Y + 0.025) },
      ]);
      setSelected(Id);
    };
    const regionPosition = locateRegion(region);
    const controlData = control?.Launcher?.Data;

    const sceneRegions = regions.filter(({ id }) => {
      if (scene === 'world' || scene === 'hotbar') return id === 'Hotbar';
      return id !== 'Hotbar' && id !== (scene === 'crafting' ? 'Equipment' : 'Crafting');
    });
    const sceneSelection: Record<PreviewScene, string> = {
      world: '',
      hotbar: 'Hotbar',
      inventory: 'InventoryItems',
      crafting: 'Crafting',
    };
    const selectScene = (next: PreviewScene) => {
      setScene(next);
      if (category === 'interface') setSelected(sceneSelection[next] || 'Hotbar');
    };
    const renderRegionArt = (entry: (typeof regions)[number], interactive: boolean) => {
      const current = locateRegion(entry);
      return interactive ? (
        <button
          key={entry.id}
          type="button"
          aria-label={t(entry.label)}
          aria-pressed={!previewOnly && entry.id === selected}
          disabled={previewOnly || pending}
          className={`controls-preview-node controls-preview-region controls-region-${entry.id}`}
          style={{
            left: `${current.CenterX * 100}%`,
            top: `${current.CenterY * 100}%`,
            width: `${entry.width * current.Scale * 100}%`,
            height: `${entry.height * current.Scale * 100}%`,
          }}
          {...dragProps({
            id: entry.id,
            box: {
              x: current.CenterX,
              y: current.CenterY,
              width: entry.width * current.Scale,
              height: entry.height * current.Scale,
            },
            insetX: entry.insetX,
            insetY: entry.insetY,
            fitSize: (w, h) => {
              const scale = clamp(
                Math.max(w / entry.width, h / entry.height),
                0.5,
                maxScale(entry),
              );
              return { width: entry.width * scale, height: entry.height * scale };
            },
            commit: (box) =>
              editRegion(entry.id, {
                CenterX: box.x,
                CenterY: box.y,
                Scale: box.width / entry.width,
              }),
          })}
        >
          <InterfaceArtwork
            id={entry.id}
            hotbar={hotbar}
            textures={controlTextures}
            opacity={profile.Opacity ?? 0.78}
            showHotbarButtons={!customControls}
          />
          <span className="controls-region-label">{t(entry.label)}</span>
          <span className="controls-resize-handle" aria-hidden="true" />
        </button>
      ) : (
        <div
          key={entry.id}
          aria-hidden="true"
          className={`controls-preview-region controls-region-art controls-region-${entry.id}`}
          style={{
            left: `${current.CenterX * 100}%`,
            top: `${current.CenterY * 100}%`,
            width: `${entry.width * current.Scale * 100}%`,
            height: `${entry.height * current.Scale * 100}%`,
          }}
        >
          <InterfaceArtwork
            id={entry.id}
            hotbar={hotbar}
            textures={controlTextures}
            opacity={profile.Opacity ?? 0.78}
            showHotbarButtons={false}
          />
        </div>
      );
    };

    return (
      <div
        className="controls-editor-workspace"
        data-preview-only={previewOnly}
        data-dock-left={dock.x < 0.5}
        data-dock-top={dock.y < 0.5}
        data-toolbar={showToolbar && !previewOnly}
      >
        <div className="controls-canvas-scroll">
          <div className="controls-canvas-scale" style={{ width: `${zoom}%`, height: `${zoom}%` }}>
            <div
              className="controls-preview"
              ref={preview}
              aria-label={
                scene === 'world'
                  ? t('游戏场景预览')
                  : scene === 'hotbar'
                    ? t('快捷栏预览')
                    : scene === 'crafting'
                      ? t('制作界面预览')
                      : t('背包界面预览')
              }
              onClick={(event) => {
                if (
                  event.target === event.currentTarget &&
                  !previewOnly &&
                  !event.currentTarget.closest('[data-manipulating="true"]')
                )
                  clearSelection();
              }}
              data-grid={grid && !previewOnly}
              data-scene={scene}
              data-native-artwork={!!controlTextures.JoystickPad}
            >
              {controlEditor && sceneRegions.map((entry) => renderRegionArt(entry, false))}
              {controlEditor
                ? controls.map((entry) => {
                    const { x, y, width, height } = controlBounds(
                      entry,
                      profile,
                      !!customControls,
                      viewport,
                    );
                    const data = entry.Launcher?.Data;
                    const label = controlLabel(entry, t);
                    const circle =
                      !data ||
                      data.shape === 'CIRCLE' ||
                      data.type === 'joystick' ||
                      data.type === 'radialmenu';
                    const color = (value: unknown, fallback: number) =>
                      '#' +
                      (Number(value ?? fallback) >>> 0).toString(16).padStart(8, '0').slice(-6);
                    if (previewOnly && (data?.isVisible === false || profile.HideGameControls))
                      return null;
                    return (
                      <button
                        key={entry.Id}
                        type="button"
                        aria-label={label}
                        aria-pressed={!previewOnly && entry.Id === selected}
                        disabled={pending}
                        className={`controls-preview-node controls-preview-control${isStick(entry) ? ' is-stick' : ''}`}
                        data-control-type={data?.type ?? (isStick(entry) ? 'joystick' : 'button')}
                        data-native-control={!data}
                        style={{
                          left: `${x * 100}%`,
                          top: `${y * 100}%`,
                          width: `${width * 100}%`,
                          height: `${height * 100}%`,
                          opacity:
                            data?.isVisible === false
                              ? 0.28
                              : !data && [13, 14, 16].includes(enumIndex(entry.Action, ACTIONS))
                                ? 1
                                : (profile.Opacity ?? 0.78),
                          rotate: data ? `${Number(data.rotation ?? 0)}deg` : undefined,
                          borderRadius: circle ? '50%' : Number(data?.cornerRadius ?? 4),
                          ...(data?.type === 'button' ? {
                            background: 'transparent',
                            border: 0,
                            color: `color-mix(in srgb, ${color(data.textColor, 0xffffffff)} ${Number(data.textOpacity ?? 0.9) * 100}%, transparent)`,
                          } : entry.CustomTexture
                            ? { background: 'transparent', borderColor: 'transparent' }
                            : data && {
                                background: `color-mix(in srgb, ${color(data.bgColor, 0xff2a2a2a)} ${Number(data.opacity ?? 0.35) * 100}%, transparent)`,
                                borderColor: `color-mix(in srgb, ${color(data.strokeColor, 0xffaaaaaa)} ${Number(data.borderOpacity ?? 0.5) * 100}%, transparent)`,
                                color: `color-mix(in srgb, ${color(data.textColor, 0xffffffff)} ${Number(data.textOpacity ?? 0.9) * 100}%, transparent)`,
                              }),
                        }}
                        {...(previewOnly ? {
                          onPointerDown: (event) => event.currentTarget.setPointerCapture(event.pointerId),
                        } : dragProps({
                          id: entry.Id,
                          box: { x, y, width, height },
                          rotation: Number(data?.rotation ?? 0),
                          fitSize: (w, h) => {
                            const ratio = Math.max(w / width, h / height);
                            if (data) {
                              const baseWidth = (entry.Size * viewport.height) / viewport.width;
                              return {
                                width:
                                  baseWidth *
                                  clamp(
                                    (data.isSizeRatioLocked === false ? w : width * ratio) /
                                      baseWidth,
                                    0.005,
                                    3,
                                  ),
                                height:
                                  entry.Size *
                                  clamp(
                                    (data.isSizeRatioLocked === false ? h : height * ratio) /
                                      entry.Size,
                                    0.005,
                                    3,
                                  ),
                              };
                            }
                            const min =
                              !isStick(entry) && enumIndex(entry.Action, ACTIONS) === 12 ? 1 : 0.35;
                            const base = controlBounds(
                              { ...entry, Size: 1 },
                              profile,
                              true,
                              viewport,
                            );
                            const size = clamp(Math.max(w / base.width, h / base.height), min, 2);
                            return { width: base.width * size, height: base.height * size };
                          },
                          commit: (box, resized) => {
                            const position = {
                              X: clamp(box.x - (data ? box.width / 2 : 0)),
                              Y: clamp(box.y - (data ? box.height / 2 : 0)),
                            };
                            if (!resized) return editControl(entry.Id, position);
                            if (!data) {
                              const base = controlBounds(
                                { ...entry, Size: 1 },
                                profile,
                                true,
                                viewport,
                              );
                              return editControl(entry.Id, {
                                ...position,
                                Size: box.width / base.width,
                              });
                            }
                            editControl(entry.Id, {
                              ...position,
                              Launcher: {
                                ...entry.Launcher,
                                Data: {
                                  ...data,
                                  width:
                                    (box.width * viewport.width) / (entry.Size * viewport.height),
                                  height: box.height / entry.Size,
                                },
                              },
                            });
                          },
                        }))}
                      >
                        {data?.type === 'button' && !entry.CustomTexture && (
                          <ButtonSurface data={data} width={width * viewport.width} height={height * viewport.height} />
                        )}
                        <ControlArtwork
                          control={entry}
                          mode={builtInMode === 1 ? 'Advanced' : 'Simple'}
                          data={data}
                          textures={controlTextures}
                          builtIn={!customControls}
                          jumpHint={profile.JoystickUpJumps !== false}
                        />
                        {data?.type === 'radialmenu' && <RadialPreview data={data} />}
                        <span className="controls-resize-handle" aria-hidden="true" />
                      </button>
                    );
                  })
                : sceneRegions.map((entry) => renderRegionArt(entry, true))}
            </div>
          </div>
        </div>
        {panel === 'palette' && !previewOnly && (
          <section className="controls-float-panel controls-palette" style={palettePosition.style} aria-label={t('控件组件')}>
            <div className="controls-inspector-header" {...palettePosition.handle}>
              <h3>{t('添加控件')}</h3>
              <Button aria-label={t('关闭')} symbol="close" onClick={() => setPanel(undefined)} />
            </div>
            <div className="controls-palette-grid">
              <Button symbol="game" onClick={() => newControl(0)}>
                {t('游戏摇杆')}
              </Button>
              {componentTypes.map(([type, label], kind) => (
                <Button
                  key={type}
                  symbol={type === 'joystick' ? 'game' : type === 'radialmenu' ? 'sun' : 'plus'}
                  onClick={() =>
                    newControl(kind, {
                      type,
                      name: t(type === 'button' ? '跳跃' : label),
                      width: type === 'button' ? 0.12 : 0.16,
                      height: type === 'button' ? 0.12 : 0.16,
                      ...(type === 'button' && {
                        shape: 'RECTANGLE', cornerRadius: 8, opacity: 0.8, keycode: 'ACTION_Jump',
                      }),
                    })
                  }
                >
                  {t(label)}
                </Button>
              ))}
            </div>
          </section>
        )}
        {(panel === 'inspector' || selectionOnly) && !previewOnly && (
          <aside
            key={selectionOnly ? selected : 'inspector'}
            className="controls-float-panel controls-inspector"
            style={inspectorPosition.style}
            data-selection-side={
              selectionOnly ? (selectionPanelRight ? 'right' : 'left') : undefined
            }
            aria-label={t('属性')}
          >
            <div className="controls-inspector-header" {...inspectorPosition.handle}>
              <h3>
                {controlEditor
                  ? control
                    ? controlLabel(control, t)
                    : t('控件属性')
                  : t(region.label)}
              </h3>
              <Button
                aria-label={t('关闭属性')}
                title={t('关闭属性')}
                symbol="close"
                onClick={() => setPanel(undefined)}
              />
            </div>
            <fieldset disabled={pending} inert={pending}>
              {controlEditor ? (
                <>
                  {!selectionOnly && (
                    <>
                      <LabeledSelect
                        className="controls-field"
                        label={t('布局模式')}
                        value={String(mode)}
                        onChange={(value) => edit({ ControlLayout: Number(value) })}
                        options={[
                          ['0', t('简单')],
                          ['1', t('高级')],
                          ['2', t('自定义')],
                        ]}
                      />
                      <LabeledSelect
                        className="controls-field"
                        label={t('控制预设')}
                        value={mode === 2 ? String(presetIndex) : ''}
                        onChange={(value) => {
                          setSelected('');
                          edit({
                            ActiveCustomPresetIndex: Number(value),
                            ControlLayout: 2,
                          });
                        }}
                        options={[
                          ['', t('选择预设'), true],
                          ...[0, 1, 2, 3].map(
                            (index) =>
                              [
                                String(index),
                                profile.CustomPresets?.[index]?.Name ||
                                  t('预设 {number}', { number: index + 1 }),
                              ] as const,
                          ),
                        ]}
                      />
                      {mode === 2 && (
                        <label className="controls-field">
                          {t('预设名称')}
                          <input
                            value={preset?.Name ?? t('预设 {number}', { number: presetIndex + 1 })}
                            maxLength={64}
                            onChange={(event) => editPreset(controls, event.target.value)}
                          />
                        </label>
                      )}
                      <LabeledSelect
                        className="controls-field"
                        label={t('选中控件')}
                        value={control?.Id ?? ''}
                        onChange={setSelected}
                        options={[
                          ['', t('选择控件')],
                          ...controls.map((entry) => [entry.Id, controlLabel(entry, t)] as const),
                        ]}
                      />
                    </>
                  )}
                  {control && (
                    <>
                      <div className="controls-editor-actions">
                        <Button disabled={controls.length >= 256} onClick={duplicate}>
                          {t('复制控件')}
                        </Button>
                        <Button
                          onClick={() => {
                            editPreset(controls.filter((entry) => entry.Id !== selected));
                            setSelected('');
                            if (selectionOnly) setPanel(undefined);
                          }}
                        >
                          {t('删除控件')}
                        </Button>
                      </div>
                      {controlData && (
                        <ControlBindings
                          key={`bindings-${control.Id}`}
                          data={controlData}
                          actions={KEY_BINDINGS}
                          edit={(changes) =>
                            editControl(control.Id, {
                              Launcher: { Data: { ...controlData, ...changes } },
                            })
                          }
                        />
                      )}
                      <Range
                        label="单个控件大小"
                        value={control.Size}
                        min={0.35}
                        max={2}
                        step={0.05}
                        onChange={(Size) => editControl(control.Id, { Size })}
                      />
                      <ControlTexturePicker
                        key={'texture-' + control.Id}
                        value={control.CustomTexture}
                        onChange={(CustomTexture) => editControl(control.Id, { CustomTexture })}
                      />
                      <Range
                        label="控件横向位置"
                        value={control.X}
                        min={0}
                        max={1}
                        step={0.01}
                        onChange={(X) => editControl(control.Id, { X })}
                      />
                      <Range
                        label="控件纵向位置"
                        value={control.Y}
                        min={0}
                        max={1}
                        step={0.01}
                        onChange={(Y) => editControl(control.Id, { Y })}
                      />
                      <label className="controls-toggle">
                        {t('允许多点触控')}
                        <Switch
                          label={t('允许多点触控')}
                          checked={control.Multitouch ?? true}
                          onChange={(Multitouch) => editControl(control.Id, { Multitouch })}
                        />
                      </label>
                      {!controlData && isStick(control) && (
                        <LabeledSelect
                          className="controls-field"
                          label={t('摇杆用途')}
                          value={String(enumIndex(control.JoystickRole, STICK_ROLES))}
                          onChange={(value) =>
                            editControl(control.Id, {
                              JoystickRole: Number(value),
                              TextureName: '',
                            })
                          }
                          options={STICK_LABELS.map((label, index) => [String(index), t(label)])}
                        />
                      )}
                      {!controlData &&
                        !isStick(control) &&
                        (control.Kind === 2 || control.Kind === 'RadialMenu' ? (
                          Array.from({ length: 4 }, (_, slot) => (
                            <LabeledSelect
                              key={slot}
                              className="controls-field"
                              label={t('轮盘动作 {number}', { number: slot + 1 })}
                              value={String(
                                enumIndex(control.RadialActions?.[slot] ?? 22, ACTIONS),
                              )}
                              onChange={(value) =>
                                editControl(control.Id, {
                                  RadialActions: Array.from({ length: 4 }, (_, i) =>
                                    i === slot ? Number(value) : (control.RadialActions?.[i] ?? 22),
                                  ),
                                })
                              }
                              options={ACTION_LABELS.map((label, index) => [
                                String(index),
                                t(label),
                                [19, 20, 21, 32].includes(index),
                              ])}
                            />
                          ))
                        ) : (
                          <LabeledSelect
                            className="controls-field"
                            label={t('按键动作')}
                            value={String(enumIndex(control.Action ?? 22, ACTIONS))}
                            onChange={(value) =>
                              editControl(control.Id, {
                                Action: Number(value),
                                TextureName: '',
                              })
                            }
                            options={ACTION_LABELS.map((label, index) => [
                              String(index),
                              t(label),
                              [21, 32].includes(index),
                            ])}
                          />
                        ))}
                      {!controlData && enumIndex(control.Action, ACTIONS) === 31 && (
                        <label className="controls-field">
                          {t('模组按键标识')}
                          <input
                            value={control.ModKeybind ?? ''}
                            onChange={(event) =>
                              editControl(control.Id, { ModKeybind: event.target.value })
                            }
                          />
                        </label>
                      )}
                      {!controlData && enumIndex(control.Action, ACTIONS) === 33 && (
                        <label className="controls-field">
                          {t('键盘按键')}
                          <input
                            value={control.KeyboardKey ?? ''}
                            onChange={(event) =>
                              editControl(control.Id, { KeyboardKey: event.target.value })
                            }
                          />
                        </label>
                      )}
                    </>
                  )}
                  {!selectionOnly && (
                    <details className="controls-details" open={!control}>
                      <summary>{t('全局触控设置')}</summary>
                      <Range
                        label="控件大小"
                        value={profile.ControlScale ?? defaultControlScale}
                        min={0.6}
                        max={1.8}
                        step={0.05}
                        onChange={(ControlScale) => edit({ ControlScale })}
                      />
                      <Range
                        label="控件透明度"
                        value={profile.Opacity ?? 0.78}
                        min={0.35}
                        max={1}
                        step={0.01}
                        onChange={(Opacity) => edit({ Opacity })}
                      />
                      <Range
                        label="摇杆死区"
                        value={profile.JoystickDeadzone ?? 0.25}
                        min={0.05}
                        max={0.55}
                        step={0.05}
                        onChange={(JoystickDeadzone) => edit({ JoystickDeadzone })}
                      />
                      {(
                        [
                          ['JoystickUpJumps', '上推跳跃', true],
                          ['EnablePinchZoom', '双指缩放', true],
                          ['ShowTopButtons', '显示顶部按钮', true],
                          ['EnableTouchInput', '启用触摸输入', true],
                          ['HideGameControls', '隐藏游戏控件', false],
                        ] as const
                      ).map(([key, label, defaultValue]) => (
                        <label className="controls-toggle" key={key}>
                          {t(label)}
                          <Switch
                            label={t(label)}
                            checked={profile[key] ?? defaultValue}
                            onChange={(value) => edit({ [key]: value })}
                          />
                        </label>
                      ))}
                    </details>
                  )}
                  {!selectionOnly && mode === 2 && (
                    <Button
                      onClick={() => {
                        editPreset(null);
                        setSelected('');
                      }}
                    >
                      {t('恢复预设')}
                    </Button>
                  )}
                </>
              ) : (
                <>
                  <LabeledSelect
                    className="controls-field"
                    label={t('界面区域')}
                    value={region.id}
                    onChange={(id) => {
                      setSelected(id);
                      if (id === 'Hotbar') setScene('hotbar');
                      else if (id === 'Crafting') setScene('crafting');
                      else if (id === 'Equipment' || scene === 'hotbar') setScene('inventory');
                    }}
                    options={regions.map((entry) => [entry.id, t(entry.label)])}
                  />
                  <LabeledSelect
                    className="controls-field"
                    label={t('快捷栏方向')}
                    value={String(enumIndex(profile.HotbarOrientation, ['Vertical', 'Horizontal']))}
                    onChange={(value) => edit({ HotbarOrientation: Number(value) })}
                    options={[
                      ['0', t('纵向')],
                      ['1', t('横向')],
                    ]}
                  />
                  <Range
                    label="区域大小"
                    value={regionPosition.Scale}
                    min={0.5}
                    max={maxScale(region)}
                    step={0.01}
                    onChange={(Scale) => editRegion(region.id, { ...regionPosition, Scale })}
                  />
                  <Range
                    label="区域横向位置"
                    value={regionPosition.CenterX}
                    min={0}
                    max={1}
                    step={0.01}
                    onChange={(CenterX) => editRegion(region.id, { ...regionPosition, CenterX })}
                  />
                  <Range
                    label="区域纵向位置"
                    value={regionPosition.CenterY}
                    min={0}
                    max={1}
                    step={0.01}
                    onChange={(CenterY) => editRegion(region.id, { ...regionPosition, CenterY })}
                  />
                  <Range
                    label="游戏字体大小"
                    value={profile.UIFontScale ?? 1.18}
                    min={0.9}
                    max={1.4}
                    step={0.02}
                    onChange={(UIFontScale) => edit({ UIFontScale })}
                  />
                  <Range
                    label="屏幕边距"
                    value={profile.EdgePadding ?? 18}
                    min={0}
                    max={96}
                    step={1}
                    onChange={(EdgePadding) => edit({ EdgePadding })}
                  />
                  <div className="controls-editor-actions">
                    <Button onClick={() => editRegion(region.id)}>{t('重置区域')}</Button>
                    <Button
                      onClick={() =>
                        edit({
                          HotbarOrientation: 0,
                          InterfaceLayout: { FormatVersion: 1, Regions: {} },
                          EdgePadding: 18,
                          UIFontScale: 1.18,
                        })
                      }
                    >
                      {t('重置界面')}
                    </Button>
                  </div>
                </>
              )}
            </fieldset>
          </aside>
        )}
        {panel === 'menu' && (
          <section className="controls-float-panel" style={menuPosition.style} aria-label={t('编辑菜单')}>
            <div className="controls-inspector-header" {...menuPosition.handle}>
              <h3>{t('编辑菜单')}</h3>
              <Button aria-label={t('关闭')} symbol="close" onClick={() => setPanel(undefined)} />
            </div>
            <div className="controls-menu-group" role="group" aria-label={t('编辑模式')}>
              <Button aria-pressed={controlEditor} onClick={() => changeCategory('controls')}>
                {t('游戏控制')}
              </Button>
              <Button aria-pressed={!controlEditor} onClick={() => changeCategory('interface')}>
                {t('游戏界面')}
              </Button>
            </div>
            <div className="controls-menu-group" role="group" aria-label={t('预览场景')}>
              <h4>{t('预览场景')}</h4>
              {(
                [
                  ['world', '游戏'],
                  ['hotbar', '快捷栏'],
                  ['inventory', '背包'],
                  ['crafting', '制作'],
                ] as const
              ).map(
                ([id, label]) =>
                  (controlEditor || id !== 'world') && (
                    <Button key={id} aria-pressed={scene === id} onClick={() => selectScene(id)}>
                      {t(label)}
                    </Button>
                  ),
              )}
            </div>
            <div className="controls-menu-group" role="group" aria-label={t('编辑工具')}>
              <h4>{t('编辑工具')}</h4>
              <Button
                aria-pressed={showToolbar}
                disabled={previewOnly}
                onClick={() => setShowToolbar(!showToolbar)}
              >
                {t('工具栏')}
              </Button>
              {!showToolbar && !previewOnly && (
                <>
                  {controlEditor && (
                    <Button
                      disabled={pending || controls.length >= 256}
                      onClick={() => setPanel('palette')}
                    >
                      {t('添加控件')}
                    </Button>
                  )}
                  <Button onClick={() => setPanel('inspector')}>{t('属性')}</Button>
                  <Button variant="primary" disabled={pending} onClick={() => void save()}>
                    {t('保存布局')}
                  </Button>
                </>
              )}
              <Button aria-pressed={grid} disabled={previewOnly} onClick={() => setGrid(!grid)}>
                {t('网格')}
              </Button>
              <Button aria-pressed={snap} disabled={previewOnly} onClick={() => setSnap(!snap)}>
                {t('网格吸附')}
              </Button>
              <Button
                aria-pressed={previewOnly}
                onClick={() => {
                  setPanel(undefined);
                  setPreviewOnly(!previewOnly);
                }}
              >
                {t('纯预览')}
              </Button>
              <Button
                aria-label={t('缩小画布')}
                title={t('缩小画布')}
                symbol="minus"
                disabled={zoom === 100}
                onClick={() => setZoom(zoom - 25)}
              />
              <Button
                aria-label={t('放大画布')}
                title={t('放大画布')}
                symbol="plus"
                disabled={zoom === 200}
                onClick={() => setZoom(zoom + 25)}
              />
            </div>
            <div className="controls-menu-group">
              {controlEditor && (
                <Button
                  disabled={pending || previewOnly}
                  onClick={() => {
                    edit({
                      ControlLayout: defaultControlMode,
                      ControlScale: defaultControlScale,
                      LeftStickOffsetX: 0,
                      LeftStickOffsetY: 0,
                      RightStickOffsetX: 0,
                      RightStickOffsetY: 0,
                    });
                    setSelected('');
                    setPanel(undefined);
                  }}
                >
                  {t('使用默认控件')}
                </Button>
              )}
              <Button disabled={pending} onClick={requestClose}>
                {t('退出编辑')}
              </Button>
            </div>
          </section>
        )}
        {showToolbar && !previewOnly && (
          <div className="controls-quickbar" role="group" style={toolbarPosition.style} aria-label={t('编辑工具')}>
            <span className="controls-toolbar-grip" title={t('拖动工具栏')} {...toolbarPosition.handle} />
            {controlEditor && (
              <Button
                symbol="plus"
                aria-label={t('添加控件')}
                title={t('添加控件')}
                aria-expanded={panel === 'palette'}
                disabled={pending || controls.length >= 256}
                onClick={() => setPanel(panel === 'palette' ? undefined : 'palette')}
              />
            )}
            <Button
              symbol="sliders"
              aria-label={t('属性')}
              title={t('属性')}
              aria-expanded={panel === 'inspector' || selectionOnly}
              onClick={() =>
                setPanel(panel === 'inspector' || selectionOnly ? undefined : 'inspector')
              }
            />
            {selected && (
              <Button
                symbol="close"
                aria-label={t('清空选择')}
                title={t('清空选择')}
                onClick={clearSelection}
              />
            )}
            <span className="controls-tool-separator" />
            <Button
              symbol="reset"
              aria-label={t('撤销')}
              title={t('撤销')}
              disabled={pending || !history.current.past.length}
              onClick={() => traverse(false)}
            />
            <Button
              aria-label={t('重做')}
              title={t('重做')}
              disabled={pending || !history.current.future.length}
              onClick={() => traverse(true)}
            >
              <Icon name="reset" className="controls-redo-icon" />
            </Button>
            <span className="controls-tool-separator" />
            <Button variant="primary" symbol="check" disabled={pending} onClick={() => void save()}>
              {t('保存布局')}
            </Button>
          </div>
        )}
      </div>
    );
  };

  return createPortal(
    <dialog
      id="game-layout-editor"
      className="controls-editor-screen"
      ref={dialog}
      aria-label={t('游戏布局编辑')}
      onCancel={(event) => {
        event.preventDefault();
        if (confirmExit) setConfirmExit(false);
        else if (panel && !pending) setPanel(undefined);
        else requestClose();
      }}
    >
      {profile && viewport?.width && viewport.height
        ? renderEditor(profile, viewport)
        : !failure && <p role="status">{t('正在读取布局…')}</p>}
      <div className="controls-dock-track">
        <button
          type="button"
          className="controls-dock"
          aria-label={t('悬浮菜单')}
          aria-expanded={panel === 'menu'}
          style={{ left: `${dock.x * 100}%`, top: `${dock.y * 100}%` }}
          onPointerDown={(event) => {
            const track = event.currentTarget.parentElement?.getBoundingClientRect();
            if (
              event.button !== 0 ||
              !event.isPrimary ||
              dockDrag.current ||
              !track?.width ||
              !track.height
            )
              return;
            dockDrag.current = {
              id: event.pointerId,
              startX: event.clientX,
              startY: event.clientY,
              width: track.width,
              height: track.height,
              moved: false,
            };
            event.currentTarget.setPointerCapture(event.pointerId);
          }}
          onPointerMove={(event) => {
            const active = dockDrag.current;
            if (active?.id !== event.pointerId) return;
            const dx = event.clientX - active.startX;
            const dy = event.clientY - active.startY;
            if (!active.moved && Math.hypot(dx, dy) < 4) return;
            active.moved = true;
            const x = clamp(dock.x + dx / active.width);
            const y = clamp(dock.y + dy / active.height);
            event.currentTarget.style.transform = `translate(${(x - dock.x) * active.width}px, ${(y - dock.y) * active.height}px)`;
          }}
          onPointerUp={(event) => {
            const active = dockDrag.current;
            if (active?.id !== event.pointerId) return;
            const dx = event.clientX - active.startX;
            const dy = event.clientY - active.startY;
            if (active.moved || Math.hypot(dx, dy) >= 4)
              setDock({
                x: clamp(dock.x + dx / active.width),
                y: clamp(dock.y + dy / active.height),
              });
            else toggleMenu();
            event.currentTarget.style.removeProperty('transform');
            dockDrag.current = null;
            event.currentTarget.releasePointerCapture(event.pointerId);
          }}
          onLostPointerCapture={(event) => {
            if (dockDrag.current?.id !== event.pointerId) return;
            event.currentTarget.style.removeProperty('transform');
            dockDrag.current = null;
          }}
          onClick={(event) => {
            if (!event.detail) toggleMenu();
          }}
        >
          <Icon name="home" />
        </button>
      </div>
      {failure && (
        <p className="controls-editor-error" role="alert">
          {t(failure)}
        </p>
      )}
      {confirmExit && (
        <div className="controls-exit">
          <section aria-label={t('退出编辑')}>
            <h3>{t('布局有未保存的修改')}</h3>
            <div className="controls-editor-actions">
              <Button onClick={() => setConfirmExit(false)}>{t('继续编辑')}</Button>
              <Button onClick={onClose}>{t('放弃修改')}</Button>
              <Button
                variant="primary"
                onClick={() => {
                  setConfirmExit(false);
                  void save();
                }}
              >
                {t('保存并退出')}
              </Button>
            </div>
          </section>
        </div>
      )}
    </dialog>,
    document.body,
  );
}
