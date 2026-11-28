import { useLayoutEffect, useRef, type MouseEvent, type PointerEvent, type RefObject } from 'react';

type CanvasBox = { x: number; y: number; width: number; height: number };
type Size = Pick<CanvasBox, 'width' | 'height'>;
type DragEvent = PointerEvent<HTMLButtonElement>;
type LayoutItem = {
  id: string;
  box: CanvasBox;
  rotation?: number;
  insetX?: number;
  insetY?: number;
  fitSize: (width: number, height: number) => Size;
  commit: (box: CanvasBox, resized: boolean) => void;
};

const clamp = (value: number, low: number, high: number) => Math.min(high, Math.max(low, value));
const fitCenter = (center: number, size: number, inset = 0) =>
  clamp(center, Math.min(0.5, inset + size / 2), Math.max(0.5, 1 - inset - size / 2));

export function useLayoutGesture(
  canvas: RefObject<HTMLDivElement | null>,
  snap: boolean,
  select: (id: string) => void,
  inspect: (id: string, node: HTMLButtonElement) => void,
) {
  const gesture = useRef<{
    pointer: number;
    update: (event: DragEvent, released: boolean) => void;
    finish: (commit: boolean) => void;
  } | null>(null);
  useLayoutEffect(() => {
    const cancel = () => gesture.current?.finish(false);
    window.addEventListener('resize', cancel);
    window.addEventListener('blur', cancel);
    return () => {
      window.removeEventListener('resize', cancel);
      window.removeEventListener('blur', cancel);
      cancel();
    };
  }, []);

  return (item: LayoutItem) => ({
    onClick(event: MouseEvent<HTMLButtonElement>) {
      if (!event.detail) inspect(item.id, event.currentTarget);
    },
    onPointerDown(event: DragEvent) {
      const bounds = canvas.current?.getBoundingClientRect();
      if (
        event.button !== 0 ||
        !event.isPrimary ||
        gesture.current ||
        !bounds?.width ||
        !bounds.height
      )
        return;
      const node = event.currentTarget;
      const workspace = node.closest<HTMLElement>('.controls-editor-workspace');
      const dock = node.closest('dialog')?.querySelector<HTMLElement>('.controls-dock-track');
      const { pointerId, clientX, clientY } = event;
      const resized =
        event.target instanceof Element && !!event.target.closest('.controls-resize-handle');
      const start = item.box;
      const oldWidth = node.style.width;
      const oldHeight = node.style.height;
      const radians = ((item.rotation ?? 0) * Math.PI) / 180;
      const cos = Math.cos(radians);
      const sin = Math.sin(radians);
      let next = start;
      let moved = false;

      const update = (point: DragEvent, released: boolean) => {
        const dx = point.clientX - clientX;
        const dy = point.clientY - clientY;
        if (!moved && Math.hypot(dx, dy) < 3) return;
        moved = true;
        let { x, y, width, height } = start;
        if (resized) {
          const size = item.fitSize(
            start.width + (dx * cos + dy * sin) / bounds.width,
            start.height + (dy * cos - dx * sin) / bounds.height,
          );
          width = size.width;
          height = size.height;
          // Hold the opposite corner fixed, including on rotated custom controls.
          const dw = (width - start.width) * bounds.width;
          const dh = (height - start.height) * bounds.height;
          x += (dw * cos - dh * sin) / (2 * bounds.width);
          y += (dw * sin + dh * cos) / (2 * bounds.height);
        } else {
          x += dx / bounds.width;
          y += dy / bounds.height;
          if (released && snap) {
            const snapNear = (value: number) => {
              const nearest = Math.round(value * 10) / 10;
              return Math.abs(value - nearest) <= 0.015 ? nearest : value;
            };
            x = snapNear(x);
            y = snapNear(y);
          }
        }
        next = {
          x: fitCenter(x, width, item.insetX),
          y: fitCenter(y, height, item.insetY),
          width,
          height,
        };
        node.dataset.dragging = 'true';
        if (resized) {
          node.style.width = `${width * 100}%`;
          node.style.height = `${height * 100}%`;
        }
        const shiftX = (next.x - start.x) * bounds.width;
        const shiftY = (next.y - start.y) * bounds.height;
        node.style.translate = `calc(-50% + ${shiftX}px) calc(-50% + ${shiftY}px)`;
      };
      const finish = (commit: boolean) => {
        gesture.current = null;
        delete node.dataset.dragging;
        if (workspace) delete workspace.dataset.manipulating;
        if (dock) dock.inert = false;
        node.style.removeProperty('translate');
        node.style.width = oldWidth;
        node.style.height = oldHeight;
        if (node.hasPointerCapture(pointerId)) node.releasePointerCapture(pointerId);
        if (commit && moved) item.commit(next, resized);
        else if (commit && !resized) inspect(item.id, node);
      };
      gesture.current = { pointer: pointerId, update, finish };
      if (workspace) workspace.dataset.manipulating = 'true';
      if (dock) dock.inert = true;
      node.setPointerCapture(pointerId);
      select(item.id);
    },
    onPointerMove(event: DragEvent) {
      const active = gesture.current;
      if (active?.pointer === event.pointerId) active.update(event, false);
    },
    onPointerUp(event: DragEvent) {
      const active = gesture.current;
      if (active?.pointer !== event.pointerId) return;
      active.update(event, true);
      active.finish(true);
    },
    onPointerCancel(event: DragEvent) {
      if (gesture.current?.pointer === event.pointerId) gesture.current.finish(false);
    },
    onLostPointerCapture(event: DragEvent) {
      if (gesture.current?.pointer === event.pointerId) gesture.current.finish(false);
    },
  });
}
