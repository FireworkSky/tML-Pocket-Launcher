import * as SelectPrimitive from '@animate-ui/react/primitives/radix-ui/select';
import * as SliderPrimitive from '@animate-ui/react/primitives/radix-ui/slider';
import { useCallback, useState } from 'react';
import { Icon } from './ui';

type SelectProps = {
  label: string;
  options: readonly (readonly [value: string, label: string, disabled?: boolean])[];
  value?: string;
  defaultValue?: string;
  onChange?: (value: string) => void;
  action?: string;
  disabled?: boolean;
};

// Radix reserves the empty string for clearing a select. Filters can offer it as a choice.
function optionValue(value: string) {
  return '=' + value;
}

export function Select({
  label,
  options,
  value,
  defaultValue,
  onChange,
  action,
  disabled,
}: SelectProps) {
  const [dialog, setDialog] = useState<HTMLDialogElement | null>(null);
  const triggerRef = useCallback((element: HTMLButtonElement | null) => {
    setDialog(element?.closest('dialog') ?? null);
  }, []);
  return (
    <SelectPrimitive.Root
      value={value === undefined ? undefined : optionValue(value)}
      defaultValue={defaultValue === undefined ? undefined : optionValue(defaultValue)}
      disabled={disabled}
      onValueChange={(next) => onChange?.(next.slice(1))}
    >
      <SelectPrimitive.Trigger
        ref={triggerRef}
        className="select-trigger"
        aria-label={label}
        data-action={action}
      >
        <SelectPrimitive.Value />
        <SelectPrimitive.Icon className="select-chevron">
          <Icon name="chevron" />
        </SelectPrimitive.Icon>
      </SelectPrimitive.Trigger>
      <SelectPrimitive.Portal container={dialog}>
        <SelectPrimitive.Content
          className="select-content primitive-popup"
          position="popper"
          sideOffset={6}
          collisionPadding={12}
        >
          <SelectPrimitive.ScrollUpButton className="select-scroll select-scroll-up">
            <Icon name="chevron" />
          </SelectPrimitive.ScrollUpButton>
          <SelectPrimitive.Viewport className="select-viewport">
            {options.map(([id, title, itemDisabled]) => (
              <SelectPrimitive.Item
                className="select-item"
                value={optionValue(id)}
                key={id}
                disabled={itemDisabled}
              >
                <SelectPrimitive.ItemText>{title}</SelectPrimitive.ItemText>
                <SelectPrimitive.ItemIndicator className="select-check">
                  <Icon name="check" />
                </SelectPrimitive.ItemIndicator>
              </SelectPrimitive.Item>
            ))}
          </SelectPrimitive.Viewport>
          <SelectPrimitive.ScrollDownButton className="select-scroll">
            <Icon name="chevron" />
          </SelectPrimitive.ScrollDownButton>
        </SelectPrimitive.Content>
      </SelectPrimitive.Portal>
    </SelectPrimitive.Root>
  );
}

export function LabeledSelect({ className, ...props }: SelectProps & { className?: string }) {
  return (
    <label className={className}>
      <span>{props.label}</span>
      <Select {...props} />
    </label>
  );
}

export function Slider({
  label,
  value,
  min,
  max,
  step = 1,
  onChange,
  id,
  unit = '%',
}: {
  label: string;
  value: number;
  min: number;
  max: number;
  step?: number;
  onChange: (value: number) => void;
  id?: string;
  unit?: string;
}) {
  return (
    <div className="range-control">
      <SliderPrimitive.Root
        className="slider-control"
        min={min}
        max={max}
        step={step}
        value={[value]}
        onValueChange={([next]) => {
          if (next !== undefined) onChange(next);
        }}
      >
        <SliderPrimitive.Track className="slider-track">
          <SliderPrimitive.Range className="slider-range" />
        </SliderPrimitive.Track>
        <SliderPrimitive.Thumb
          className="slider-thumb"
          id={id}
          aria-label={label}
          aria-valuetext={`${Number(value.toFixed(2))}${unit}`}
        />
      </SliderPrimitive.Root>
      <output aria-live="off">
        {Number(value.toFixed(2))}
        {unit}
      </output>
    </div>
  );
}
