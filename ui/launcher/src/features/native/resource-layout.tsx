import { useEffect, useRef, useState, type ReactNode } from 'react';
import { Button, Icon } from '../../components/ui';
import { Select } from '../../components/form-controls';
import { useTranslation } from '../../i18n';

export function ResourceBrowserLayout({
  title,
  summary,
  toolbar,
  filters,
  children,
  compactFilters = false,
}: {
  title: string;
  summary: string;
  toolbar?: ReactNode;
  filters: ReactNode;
  compactFilters?: boolean;
  children: ReactNode;
}) {
  const { t } = useTranslation();
  const root = useRef<HTMLDivElement>(null);
  const [expanded, setExpanded] = useState(false);
  useEffect(() => {
    const container = root.current?.closest('.main');
    if (!container || compactFilters) return;
    let wide: boolean | undefined;
    const observer = new ResizeObserver(([entry]) => {
      const nextWide = entry.contentRect.width > 659;
      if (nextWide !== wide) setExpanded(nextWide);
      wide = nextWide;
    });
    observer.observe(container);
    return () => observer.disconnect();
  }, [compactFilters]);
  return (
    <div className={compactFilters ? "resource-browser workshop-compact" : "resource-browser"} ref={root} data-filters-open={expanded}>
      <div className="resource-browser-toolbar">
        <div className="resource-browser-heading">
          <h2>{title}</h2>
          <p role="status">{summary}</p>
        </div>
        {toolbar}
      </div>
      <details
        className="resource-browser-filters"
        open={expanded}
        onToggle={(event) => setExpanded(event.currentTarget.open)}
      >
        <summary>
          <Icon name="sliders" />
          <span>{t('搜索与筛选')}</span>
          <Icon name="chevron" />
        </summary>
        <div className="resource-filter-body">{filters}</div>
      </details>
      <section className="resource-browser-results" aria-label={title}>
        {children}
      </section>
    </div>
  );
}

export function ResourceSearch({
  label,
  value,
  onChange,
  placeholder,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
}) {
  const { t } = useTranslation();
  return (
    <label className="resource-search">
      <span>{label}</span>
      <div>
        <input
          type="search"
          aria-label={label}
          value={value}
          placeholder={placeholder ?? t('搜索名称')}
          onChange={(event) => onChange(event.target.value)}
        />
        <Icon name="search" />
      </div>
    </label>
  );
}

export function ResourceSelect({
  label,
  value,
  onChange,
  options,
  disabled,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  options: readonly (readonly [string, string])[];
  disabled?: boolean;
}) {
  return (
    <div className="resource-filter-select">
      <span>{label}</span>
      <Select label={label} value={value} onChange={onChange} options={options} disabled={disabled} />
    </div>
  );
}

export function ResourceReset({ onClick }: { onClick: () => void }) {
  const { t } = useTranslation();
  return (
    <Button className="resource-reset" onClick={onClick}>
      {t('重置筛选')}
    </Button>
  );
}
