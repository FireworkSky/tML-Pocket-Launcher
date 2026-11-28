import { useRef, useState } from 'react';
import { Button } from '../../components/ui';
import { useTranslation } from '../../i18n';
import { LabeledSelect, Slider } from '../../components/form-controls';

export const componentTypes = [
  ['joystick', '按键摇杆'],
  ['button', '游戏控件'],
  ['radialmenu', '展开式轮盘'],
] as const;

export type ControlData = Record<string, unknown> & {
  type: string;
  name?: string;
  width?: number;
  height?: number;
};
type Sector = { keycode: string; label: string };

export function RadialPreview({ data }: { data: ControlData }) {
  const count = Math.max(4, Math.min(12, Number(data.sectorCount ?? 8)));
  const diameter = `${Number(data.expandedScale ?? 2.5) * 100}%`;
  const step = 360 / count;
  const sectors = data.sectors as Sector[] | undefined;
  return (
    <span className="controls-expanded-wheel" aria-hidden="true" style={{
      width: diameter,
      height: diameter,
      backgroundImage: data.showDividers === false ? undefined :
        `repeating-conic-gradient(from ${-step / 2}deg, #fff5 0deg 1deg, transparent 1deg ${step}deg)`,
    }}>
      {Array.from({ length: count }, (_, i) => {
        const angle = i * 2 * Math.PI / count;
        return <span key={i} style={{ left: `${50 + 36 * Math.sin(angle)}%`, top: `${50 - 36 * Math.cos(angle)}%` }}>
          {sectors?.[i]?.label ?? i + 1}
        </span>;
      })}
    </span>
  );
}

async function readControlPng(file: File) {
  if (file.size < 33 || file.size > 128 * 1024) throw new Error();
  const header = new DataView(await file.slice(0, 24).arrayBuffer());
  if (
    header.getUint32(0) !== 0x89504e47 ||
    header.getUint32(4) !== 0x0d0a1a0a ||
    header.getUint32(8) !== 13 ||
    header.getUint32(12) !== 0x49484452 ||
    ![header.getUint32(16), header.getUint32(20)].every((size) => size >= 1 && size <= 256)
  )
    throw new Error();
  const uri = await new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file.slice(0, file.size, 'image/png'));
  });
  const image = new Image();
  image.src = uri;
  await image.decode();
  return uri;
}

export function ControlTexturePicker({
  value,
  onChange,
}: {
  value?: string;
  onChange: (texture?: string) => void;
}) {
  const { t } = useTranslation();
  const input = useRef<HTMLInputElement>(null);
  const [error, setError] = useState(false);
  return (
    <div className="controls-field">
      <span>{t('控件贴图')}</span>
      <small>{t('PNG，最大 256 × 256 像素、128 KiB')}</small>
      <div className="controls-editor-actions">
        <Button onClick={() => input.current?.click()}>{t('选择 PNG')}</Button>
        {value && (
          <Button
            onClick={() => {
              setError(false);
              onChange();
            }}
          >
            {t('恢复默认贴图')}
          </Button>
        )}
      </div>
      <input
        hidden
        ref={input}
        type="file"
        accept="image/png"
        aria-label={t('选择 PNG')}
        onChange={async (event) => {
          const picker = event.currentTarget;
          const file = picker.files?.[0];
          picker.value = '';
          if (!file) return;
          setError(false);
          try {
            const texture = await readControlPng(file);
            if (picker.isConnected) onChange(texture);
          } catch {
            if (picker.isConnected) setError(true);
          }
        }}
      />
      {error && <span role="alert">{t('无法读取贴图，请选择符合尺寸和大小要求的 PNG。')}</span>}
    </div>
  );
}

export function Range({
  label,
  value,
  min,
  max,
  step,
  onChange,
}: {
  label: string;
  value: number;
  min: number;
  max: number;
  step: number;
  onChange: (value: number) => void;
}) {
  const { t } = useTranslation();
  const translatedLabel = t(label);
  return (
    <div className="controls-field">
      <span>{translatedLabel}</span>
      <Slider
        label={translatedLabel}
        min={min}
        max={max}
        step={step}
        value={value}
        unit=""
        onChange={onChange}
      />
    </div>
  );
}

export function ControlBindings({
  data,
  edit,
  actions,
}: {
  data: ControlData;
  edit: (changes: Partial<ControlData>) => void;
  actions: readonly (readonly [string, string])[];
}) {
  const { t } = useTranslation();
  const { type } = data;
  const actionOptions = actions.map(([key, name]) => [key, t(name)] as const);
  const binding = (label: string, value: string, update: (key: string) => void) => (
    <LabeledSelect
      key={label}
      className="controls-field"
      label={label}
      value={value}
      onChange={update}
      options={
        actionOptions.some(([key]) => key === value)
          ? actionOptions
          : [[value, value], ...actionOptions]
      }
    />
  );
  const toggle = (key: string, label: string, fallback = false) => (
    <label className="controls-check">
      <input
        type="checkbox"
        checked={(data[key] as boolean | undefined) ?? fallback}
        onChange={(event) => edit({ [key]: event.target.checked })}
      />
      {t(label)}
    </label>
  );
  const range = (
    key: string,
    label: string,
    fallback: number,
    min: number,
    max: number,
    step: number,
  ) => (
    <Range
      label={label}
      value={Number(data[key] ?? fallback)}
      min={min}
      max={max}
      step={step}
      onChange={(value) => edit({ [key]: value })}
    />
  );
  const size = (key: 'width' | 'height', label: string) => (
    <Range
      label={label}
      value={Number(data[key] ?? 0.16)}
      min={0.02}
      max={1}
      step={0.005}
      onChange={(value) => {
        if (data.isSizeRatioLocked === false) edit({ [key]: value });
        else {
          const other = key === 'width' ? 'height' : 'width';
          const ratio = Number(data[other] ?? 0.16) / Number(data[key] ?? 0.16);
          edit({ [key]: value, [other]: Math.min(3, Math.max(0.005, value * ratio)) });
        }
      }}
    />
  );
  const color = (key: string, label: string, fallback: number) => (
    <label className="controls-color">
      {t(label)}
      <input
        type="color"
        value={'#' + (Number(data[key] ?? fallback) >>> 0).toString(16).padStart(8, '0').slice(-6)}
        onChange={(event) =>
          edit({
            [key]:
              (Number(data[key] ?? fallback) & 0xff000000) |
              parseInt(event.target.value.slice(1), 16),
          })
        }
      />
    </label>
  );
  const directions = [
    ['上', 'KEYBOARD_W'],
    ['右', 'KEYBOARD_D'],
    ['下', 'KEYBOARD_S'],
    ['左', 'KEYBOARD_A'],
  ] as const;
  const joystickKeys =
    (data.joystickKeys as string[] | undefined) ?? directions.map((entry) => entry[1]);
  const points = (data.polygonPoints as { x: number; y: number }[] | undefined) ?? [];
  const sectors = Array.from(
    { length: Number(data.sectorCount ?? 8) },
    (_, i) =>
      (data.sectors as Sector[] | undefined)?.[i] ?? {
        keycode: 'KEYBOARD_' + (i + 1),
        label: String(i + 1),
      },
  );
  const changeSector = (index: number, changes: Partial<Sector>) =>
    edit({ sectors: sectors.map((entry, i) => (i === index ? { ...entry, ...changes } : entry)) });
  return (
    <>
      <label className="controls-field">
        {t('控件名称')}
        <input
          value={data.name ?? ''}
          maxLength={64}
          onChange={(event) => edit({ name: event.target.value })}
        />
      </label>
      {type === 'button' &&
        binding(t('按键绑定'), String(data.keycode ?? 'UNKNOWN'), (keycode) => edit({ keycode }))}
      {type === 'button' && (
        <>
          <LabeledSelect className="controls-field" label={t('形状')}
            value={String(data.shape ?? 'RECTANGLE')}
            options={[['RECTANGLE', t('矩形')], ['CIRCLE', t('圆形')], ['POLYGON', t('多边形')]]}
            onChange={(shape) => edit({
              shape,
              ...(shape === 'POLYGON' && points.length < 3 && {
                polygonPoints: [{ x: 0.5, y: 0 }, { x: 1, y: 0.5 }, { x: 0.5, y: 1 }, { x: 0, y: 0.5 }],
              }),
            })}
          />
          {data.shape === 'POLYGON' && (
            <details className="controls-details">
              <summary>{t('多边形顶点')}</summary>
              {points.map((point, index) => (
                <div className="controls-point" key={index}>
                  {(['x', 'y'] as const).map((axis) => (
                    <label key={axis}>
                      {axis.toUpperCase()}
                      <input
                        aria-label={t('顶点 {number} {axis}', { number: index + 1, axis })}
                        type="number"
                        min={0}
                        max={1}
                        step={0.05}
                        value={point[axis]}
                        onChange={(event) => {
                          const value = event.target.valueAsNumber;
                          if (Number.isFinite(value))
                            edit({
                              polygonPoints: points.map((item, i) =>
                                i === index
                                  ? { ...item, [axis]: Math.max(0, Math.min(1, value)) }
                                  : item,
                              ),
                            });
                        }}
                      />
                    </label>
                  ))}
                  <Button
                    disabled={points.length <= 3}
                    aria-label={t('删除顶点 {number}', { number: index + 1 })}
                    onClick={() => edit({ polygonPoints: points.filter((_, i) => i !== index) })}
                  >
                    {t('删除')}
                  </Button>
                </div>
              ))}
              <Button
                disabled={points.length >= 64}
                onClick={() =>
                  edit({
                    polygonPoints: points.length
                      ? [...points, { x: 0.5, y: 0.5 }]
                      : [
                          { x: 0.5, y: 0.05 },
                          { x: 0.95, y: 0.95 },
                          { x: 0.05, y: 0.95 },
                        ],
                  })
                }
              >
                {t('添加顶点')}
              </Button>
            </details>
          )}

          <details className="controls-details">
            <summary>{t('大小与位置')}</summary>
            {size('width', '宽度')}
            {size('height', '高度')}
            {toggle('isSizeRatioLocked', '锁定宽高比', true)}
            {range('rotation', '旋转', 0, -180, 180, 1)}
          </details>
          <details className="controls-details">
            <summary>{t('按钮外观')}</summary>
            {(data.shape ?? 'RECTANGLE') === 'RECTANGLE' && range('cornerRadius', '圆角', 8, 0, 80, 1)}
            {range('strokeWidth', '边框宽度', 1.5, 0, 12, 0.5)}
            {range('opacity', '背景不透明度', 0.35, 0, 1, 0.01)}
            {range('borderOpacity', '边框不透明度', 0.5, 0, 1, 0.01)}
            {range('textOpacity', '文字不透明度', 0.9, 0, 1, 0.01)}
            {color('bgColor', '背景颜色', 0xff2a2a2a)}
            {color('strokeColor', '边框颜色', 0xffaaaaaa)}
            {color('textColor', '文字颜色', 0xffffffff)}
          </details>
          {toggle('isVisible', '游戏中可见', true)}
          {toggle('isToggle', '点按切换按下状态')}
        </>
      )}
      {type === 'joystick' && (
        <>
          {data.mode !== 'MOUSE' &&
            directions.map(([label, key], i) =>
              binding(t(label), joystickKeys[i] ?? key, (value) =>
                edit({
                  joystickKeys: directions.map((entry, j) =>
                    i === j ? value : (joystickKeys[j] ?? entry[1]),
                  ),
                }),
              ),
            )}
        </>
      )}
      {type === 'radialmenu' && (
        <details className="controls-details">
          <summary>{t('轮盘扇区')}</summary>
          {sectors.map((sector, index) =>
            binding(t('扇区 {number} 绑定', { number: index + 1 }), sector.keycode, (keycode) =>
              changeSector(index, { keycode }),
            ),
          )}
        </details>
      )}
    </>
  );
}
