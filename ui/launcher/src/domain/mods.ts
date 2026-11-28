import type { Mod, ModFilters, ModPack, Profile } from './types';

export const catalog: Mod[] = [
  {
    id: 'content',
    title: '示例内容模组',
    author: '示例作者 A',
    description: '新生物群落、物品与冒险内容',
    version: '1.3.0',
    kind: '内容扩展',
    art: 'C',
    deps: ['library'],
    side: 'both',
    channel: 'stable',
    size: 120,
    hot: 90,
    days: 2,
    enabled: false,
    installed: false,
  },
  {
    id: 'helper',
    title: '示例辅助模组',
    author: '示例作者 B',
    description: '整理物品与制作配方',
    version: '2.0.1',
    kind: '实用工具',
    art: 'U',
    deps: [],
    side: 'client',
    channel: 'stable',
    size: 4,
    hot: 80,
    days: 6,
    enabled: false,
    installed: false,
  },
  {
    id: 'library',
    title: '示例前置库',
    author: '示例作者 A',
    description: '内容模组的通用组件',
    version: '1.2.0',
    kind: '前置库',
    art: 'L',
    deps: [],
    side: 'both',
    channel: 'stable',
    size: 2,
    hot: 70,
    days: 3,
    enabled: false,
    installed: false,
  },
  {
    id: 'adventure',
    title: '示例冒险扩展',
    author: '示例作者 C',
    description: '更多探索事件与装备',
    version: '1.0.0',
    kind: '内容扩展',
    art: 'A',
    deps: ['library'],
    side: 'both',
    channel: 'stable',
    size: 36,
    hot: 60,
    days: 15,
    enabled: false,
    installed: false,
  },
  {
    id: 'preview',
    title: '示例预览扩展',
    author: '示例作者 D',
    description: '用于展示版本不兼容时的处理',
    version: '0.2.0',
    kind: '内容扩展',
    art: 'P',
    deps: [],
    side: 'both',
    channel: 'preview',
    size: 8,
    hot: 50,
    days: 1,
    enabled: false,
    installed: false,
  },
];
export const demoMods = catalog.slice(0, 3).map((mod) => ({
  ...mod,
  version: mod.id === 'content' ? '1.2.0' : mod.id === 'library' ? '1.1.0' : mod.version,
  enabled: true,
  installed: false,
}));
export const defaultFilters: ModFilters = {
  query: '',
  searchBy: 'name',
  sort: 'hot',
  status: 'all',
  tag: 'all',
  side: 'all',
  period: 'all',
};
export const modTitle = (id: string) => catalog.find((mod) => mod.id === id)?.title || id;
export const needsUpdate = (mod: Mod) =>
  catalog.some((item) => item.id === mod.id && item.version !== mod.version);

export function resolveMods(ids: string[], installed: Mod[] = [], preferLocal = false): Mod[] {
  const visiting = new Set<string>();
  const visited = new Set<string>();
  const result: Mod[] = [];
  const visit = (id: string) => {
    if (visited.has(id)) return;
    if (visiting.has(id)) throw new Error('发现循环依赖：' + modTitle(id));
    const mod = preferLocal
      ? installed.find((item) => item.id === id) || catalog.find((item) => item.id === id)
      : catalog.find((item) => item.id === id);
    if (!mod) throw new Error('无法识别模组：' + id);
    visiting.add(id);
    mod.deps.forEach(visit);
    visiting.delete(id);
    visited.add(id);
    result.push(mod);
  };
  ids.forEach(visit);
  return result;
}
export function enabledDependents(id: string, installed: Mod[]): string[] {
  const found = new Set<string>();
  const visit = (dependency: string) =>
    installed
      .filter((mod) => mod.enabled && mod.deps.includes(dependency))
      .forEach((mod) => {
        if (!found.has(mod.id)) {
          found.add(mod.id);
          visit(mod.id);
        }
      });
  visit(id);
  return [...found];
}
export function filterCatalog(filters: ModFilters, installed: Mod[], profile: Profile): Mod[] {
  const query = filters.query.trim().toLocaleLowerCase();
  return catalog
    .filter((mod) => {
      const local = installed.find((item) => item.id === mod.id);
      const haystack =
        filters.searchBy === 'author' ? mod.author : `${mod.title} ${mod.id} ${mod.author}`;
      return (
        haystack.toLocaleLowerCase().includes(query) &&
        (filters.tag === 'all' || mod.kind === filters.tag) &&
        (filters.side === 'all' || mod.side === filters.side) &&
        (filters.period === 'all' || mod.days <= Number(filters.period)) &&
        (filters.status === 'all' ||
          (filters.status === 'updates' && local && needsUpdate(local)) ||
          (filters.status === 'installed' && local) ||
          (filters.status === 'not-installed' && !local) ||
          (filters.status === 'compatible' && mod.channel === profile))
      );
    })
    .sort((a, b) =>
      filters.sort === 'hot'
        ? b.hot - a.hot
        : filters.sort === 'name'
          ? a.title.localeCompare(b.title, 'zh-CN')
          : a.days - b.days,
    );
}
export function parsePack(text: string): Omit<ModPack, 'id'> {
  const data: unknown = JSON.parse(text);
  if (!data || typeof data !== 'object') throw new Error('不支持的清单格式');
  const value = data as Record<string, unknown>;
  if (
    value.format !== 'tml-mobile-mod-list' ||
    value.version !== 1 ||
    typeof value.name !== 'string' ||
    !value.name.trim() ||
    value.name.length > 64 ||
    !['stable', 'preview'].includes(String(value.profile)) ||
    !Array.isArray(value.mods) ||
    value.mods.length > 1000 ||
    !value.mods.every((id) => typeof id === 'string' && /^[\p{L}\p{Nl}\p{Nd}_][\p{L}\p{Nl}\p{M}\p{Nd}\p{Pc}]*$/u.test(id))
  )
    throw new Error('不支持的清单格式');
  return {
    name: value.name.trim(),
    profile: value.profile as Profile,
    mods: [...new Set(value.mods as string[])],
  };
}
