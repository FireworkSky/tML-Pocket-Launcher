import type { ControlData } from './controls-properties';

export function ButtonSurface({ data, width, height }: {
  data: ControlData;
  width: number;
  height: number;
}) {
  const rgb = (argb: unknown) => '#' + (Number(argb) & 0xffffff).toString(16).padStart(6, '0');
  const points = data.polygonPoints as { x: number; y: number }[] | undefined;
  const stroke = Math.min(Number(data.strokeWidth ?? 1.5), width, height);
  const inset = stroke / 2;
  return (
    <svg className="controls-button-surface" aria-hidden="true"
      viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none"
      fill={rgb(data.bgColor ?? 0xff2a2a2a)} fillOpacity={Number(data.opacity ?? 0.35)}
      stroke={rgb(data.strokeColor ?? 0xffaaaaaa)} strokeOpacity={Number(data.borderOpacity ?? 0.5)}
      strokeWidth={stroke} strokeLinejoin="round">
      {data.shape === 'CIRCLE' ? (
        <circle cx={width / 2} cy={height / 2} r={(Math.min(width, height) - stroke) / 2} />
      ) : data.shape === 'POLYGON' && points && points.length >= 3 ? (
        <polygon points={points.map(({ x, y }) => `${x * width},${y * height}`).join(' ')} />
      ) : (
        <rect x={inset} y={inset} width={width - stroke} height={height - stroke}
          rx={Math.max(0, Number(data.cornerRadius ?? 8) - inset)} />
      )}
    </svg>
  );
}
