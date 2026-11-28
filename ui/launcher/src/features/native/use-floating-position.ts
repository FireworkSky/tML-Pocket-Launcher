import { useRef, useState, type CSSProperties, type PointerEvent } from 'react';

type Point = { x: number; y: number };
type Drag = {
  id: number;
  panel: HTMLElement;
  pointer: Point;
  start: Point;
  travel: Point;
};

// A fractional position stays on screen when the panel or workspace changes size.
export function useFloatingPosition() {
  const [position, setPosition] = useState<Point>();
  const drag = useRef<Drag | undefined>(undefined);
  const style: CSSProperties | undefined = position && {
    inset: 'auto',
    left: `${position.x * 100}%`,
    top: `${position.y * 100}%`,
    translate: `${-position.x * 100}% ${-position.y * 100}%`,
  };
  function offset(event: PointerEvent, active: Drag) {
    return {
      x: Math.min(active.travel.x, Math.max(0, active.start.x + event.clientX - active.pointer.x)),
      y: Math.min(active.travel.y, Math.max(0, active.start.y + event.clientY - active.pointer.y)),
    };
  }
  const handle = {
    onPointerDown(event: PointerEvent<HTMLElement>) {
      if (!event.isPrimary || event.button !== 0 || (event.target as Element).closest('button')) return;
      const panel = event.currentTarget.parentElement!;
      const bounds = panel.parentElement!.getBoundingClientRect();
      const rect = panel.getBoundingClientRect();
      drag.current = {
        id: event.pointerId, panel,
        pointer: { x: event.clientX, y: event.clientY },
        start: { x: rect.left - bounds.left, y: rect.top - bounds.top },
        travel: { x: Math.max(0, bounds.width - rect.width), y: Math.max(0, bounds.height - rect.height) },
      };
      event.currentTarget.setPointerCapture(event.pointerId);
    },
    onPointerMove(event: PointerEvent<HTMLElement>) {
      const active = drag.current;
      if (active?.id !== event.pointerId) return;
      const point = offset(event, active);
      active.panel.style.transform = `translate(${point.x - active.start.x}px, ${point.y - active.start.y}px)`;
    },
    onPointerUp(event: PointerEvent<HTMLElement>) {
      const active = drag.current;
      if (active?.id !== event.pointerId) return;
      const point = offset(event, active);
      setPosition({ x: point.x / (active.travel.x || 1), y: point.y / (active.travel.y || 1) });
      active.panel.style.removeProperty('transform');
      drag.current = undefined;
    },
    onLostPointerCapture(event: PointerEvent<HTMLElement>) {
      if (drag.current?.id !== event.pointerId) return;
      drag.current.panel.style.removeProperty('transform');
      drag.current = undefined;
    },
  };
  return { style, handle };
}
