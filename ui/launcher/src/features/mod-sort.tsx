import { Select } from '../components/form-controls';
import type { LauncherState, Mod } from '../domain/types';
import { useTranslation } from '../i18n';
import { useLauncher } from '../state/launcher';

const sorts = [
  ['name', '按名称排序'],
  ['size', '按大小排序'],
  ['enabled', '已启用优先'],
  ['updated', '最近变更优先'],
] as const;

export function ModSortSelect() {
  const { state, update } = useLauncher();
  const { t } = useTranslation();
  return (
    <Select
      label={t('排序方式')}
      action="mod-sort"
      value={state.localSort}
      options={sorts.map(([id, label]) => [id, t(label)])}
      onChange={(value) => {
        const sort = sorts.find(([id]) => id === value);
        if (sort) update((current) => ({ ...current, localSort: sort[0] }));
      }}
    />
  );
}

type SortableMod = Pick<Mod, 'id' | 'title' | 'size' | 'enabled' | 'changed'>;

export function compareInstalledMods(sort: LauncherState['localSort'], locale: string) {
  const collator = new Intl.Collator(locale, { numeric: true, sensitivity: 'base' });
  return (a: SortableMod, b: SortableMod) => {
    let result = 0;
    if (sort === 'size') result = b.size - a.size;
    if (sort === 'enabled') result = Number(b.enabled) - Number(a.enabled);
    if (sort === 'updated') result = (b.changed ?? 0) - (a.changed ?? 0);
    return result || collator.compare(a.title, b.title) || collator.compare(a.id, b.id);
  };
}
