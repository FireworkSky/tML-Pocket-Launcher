import { useTranslation } from '../i18n';
import {
  Badge,
  Button,
  EmptyState,
  Icon,
  IconButton,
  Switch,
  WorkspaceTabs,
} from '../components/ui';
import { filterCatalog, needsUpdate, defaultFilters } from '../domain/mods';
import { compareInstalledMods, ModSortSelect } from './mod-sort';
import { CatalogFilters, useModActions } from './mod-actions';
import { PacksPanel } from './packs';
import type { Mod } from '../domain/types';

function ModArt({ mod }: { mod: Mod }) {
  return <span className={`mod-art ${mod.side === 'client' ? 'blue' : ''}`}>{mod.art}</span>;
}
export function ModsPage() {
  const { t } = useTranslation();
  const { state, update, installed } = useModActions();
  return (
    <WorkspaceTabs
      value={state.modTab}
      onChange={(modTab) => update((current) => ({ ...current, modTab }))}
      label={t('模组工作区')}
      className="mods-tabs"
      items={[
        {
          value: 'installed',
          label: t('已安装'),
          count: installed.length,
          id: 'mod-tab-installed',
          data: { 'data-action': 'mw-tab-installed' },
          content: <LocalMods />,
        },
        {
          value: 'browse',
          label: t('下载模组'),
          id: 'mod-tab-browse',
          data: { 'data-action': 'mw-tab-browse' },
          content: <Catalog />,
        },
        {
          value: 'packs',
          label: t('模组包'),
          id: 'mod-tab-packs',
          data: { 'data-action': 'mw-tab-packs' },
          content: <PacksPanel />,
        },
      ]}
    />
  );
}
function LocalMods() {
  const { t, locale } = useTranslation();
  const {
    state,
    installed,
    update,
    openDialog,
    closeDialog,
    toggle,
    details,
    updateAll,
    bulk,
    showInfo,
  } = useModActions();
  const query = state.localQuery.toLocaleLowerCase().trim();
  const filtered = installed
    .filter(
      (mod) =>
        (state.localFilter === 'all' || mod.enabled === (state.localFilter === 'enabled')) &&
        `${mod.title} ${mod.id} ${mod.author} ${mod.kind}`.toLocaleLowerCase().includes(query),
    )
    .sort(compareInstalledMods(state.localSort, locale));
  const menu = () =>
    openDialog({
      title: t('本地模组操作'),
      description: t('批量操作作用于当前已安装列表；不会启动或重新加载游戏。'),
      body: (
        <div className="pack-options">
          <Button variant="soft" data-action="mw-enable-all" onClick={() => bulk(true)}>
            {t('全部启用')}
          </Button>
          <Button data-action="mw-disable-all" onClick={() => bulk(false)}>
            {t('全部停用')}
          </Button>
          <Button data-action="mod-folder" onClick={() => showInfo('mod-folder')}>
            {t('查看模组目录')}
          </Button>
          <Button
            data-action="mod-profiles"
            onClick={() => {
              update((current) => ({ ...current, modTab: 'packs' }));
              closeDialog(false);
            }}
          >
            {t('管理模组包')}
          </Button>
        </div>
      ),
      drawer: true,
    });
  return (
    <>
      <div className="mod-search-tools">
        <label className="search-field">
          <Icon name="search" />
          <input
            type="search"
            id="mod-search"
            aria-label={t('搜索本地模组')}
            placeholder={t('搜索名称或作者')}
            value={state.localQuery}
            onChange={(event) =>
              update((current) => ({ ...current, localQuery: event.target.value }))
            }
          />
        </label>
        <IconButton
          symbol="sliders"
          label={t('模组批量操作')}
          data-action="mw-local-menu"
          onClick={menu}
        />
      </div>
      <div className="mod-filter-line">
        <div className="filter-group" aria-label={t('筛选模组')}>
          {(
            [
              ['all', '全部'],
              ['enabled', '已启用'],
              ['disabled', '未启用'],
            ] as const
          ).map(([localFilter, label]) => (
            <Button
              key={localFilter}
              className={`filter ${state.localFilter === localFilter ? 'active' : ''}`}
              aria-pressed={state.localFilter === localFilter}
              data-mod-filter={localFilter}
              onClick={() => update((current) => ({ ...current, localFilter }))}
            >
              {t(label)}
            </Button>
          ))}
        </div>
        <ModSortSelect />
        <Button className="mod-text-action" data-action="mw-update-all" onClick={updateAll}>
          <Icon name="download" />
          {t('检查更新')}
        </Button>
      </div>
      <section className="panel mod-list" id="mods-list">
        {!installed.length ? (
          <EmptyState
            symbol="mod"
            title={t('添加你的第一个模组')}
            description={t('可以浏览和下载游戏模组，也可以从本地导入。')}
          >
            <Button
              variant="soft"
              symbol="download"
              onClick={() => update((current) => ({ ...current, modTab: 'browse' }))}
            >
              {t('下载模组')}
            </Button>
          </EmptyState>
        ) : !filtered.length ? (
          <EmptyState
            symbol="search"
            title={t('没有匹配的模组')}
            description={t('尝试其他关键词或筛选条件。')}
          >
            <Button
              variant="soft"
              data-action="clear-mod-search"
              onClick={() =>
                update((current) => ({
                  ...current,
                  localQuery: '',
                  localFilter: 'all',
                }))
              }
            >
              {t('清除筛选')}
            </Button>
          </EmptyState>
        ) : (
          filtered.map((mod) => (
            <div className="mod-row" key={mod.id}>
              <ModArt mod={mod} />
              <div className="row-main">
                <h3>
                  {mod.title}
                  {mod.channel !== state.profile ? (
                    <Badge>{t('版本不兼容')}</Badge>
                  ) : needsUpdate(mod) ? (
                    <Badge tone="blue">{t('可更新')}</Badge>
                  ) : null}
                </h3>
                <p>
                  v{mod.version} · {mod.author}
                </p>
                <small>
                  {mod.kind}
                  {mod.deps.length > 0 && t(' · {count} 项依赖', { count: mod.deps.length })}
                </small>
              </div>
              <Switch
                label={t('启用{mod}', { mod: mod.title })}
                checked={mod.enabled}
                action={`mod-${mod.id}`}
                onChange={() => toggle(mod)}
              />
              <IconButton
                symbol="chevron"
                label={t('查看{mod}详情', { mod: mod.title })}
                data-action={`mod-detail-${mod.id}`}
                onClick={() => details(mod)}
              />
            </div>
          ))
        )}
      </section>
    </>
  );
}
function Catalog() {
  const { t } = useTranslation();
  const { state, installed, update, openDialog, notice, details, plan, instances, updateAll } =
    useModActions();
  const filtered = filterCatalog(state.filters, installed, state.profile);
  const filters = state.filters;
  const hasFilter =
    filters.status !== 'all' ||
    filters.tag !== 'all' ||
    filters.side !== 'all' ||
    filters.period !== 'all';
  return (
    <>
      <div className="mod-search-tools">
        <label className="search-field">
          <Icon name="search" />
          <input
            type="search"
            id="catalog-search"
            aria-label={t('搜索在线模组')}
            placeholder={t('搜索模组名称或作者')}
            value={filters.query}
            onChange={(event) =>
              update((current) => ({
                ...current,
                filters: { ...current.filters, query: event.target.value },
              }))
            }
          />
        </label>
        <IconButton
          symbol="sliders"
          label={t('模组下载筛选与排序')}
          data-action="mw-filters"
          onClick={() =>
            openDialog({
              title: t('筛选与排序'),
              description: t('参考 tML 模组浏览器；这些选项筛选当前示例目录。'),
              body: <CatalogFilters />,
              drawer: true,
            })
          }
        />
      </div>
      <div id="catalog-results">
        <div className="catalog-summary">
          <span>
            {t('创意工坊 · 示例 · {count} 项', { count: filtered.length })}
            {hasFilter && t(' · 已筛选')}
          </span>
          <Button className="mod-text-action" data-action="mw-update-all" onClick={updateAll}>
            <Icon name="download" />
            {t('全部更新')}
          </Button>
        </div>
        {filtered.length ? (
          <section className="panel mod-list">
            {filtered.map((mod) => {
              const local = installed.find((item) => item.id === mod.id);
              const compatible = mod.channel === state.profile;
              const update = local && needsUpdate(local);
              return (
                <div className="catalog-row" key={mod.id}>
                  <ModArt mod={mod} />
                  <Button
                    className="catalog-copy"
                    data-action={`mw-detail-${mod.id}`}
                    onClick={() => details(mod, true)}
                  >
                    <strong>{mod.title}</strong>
                    <span>
                      {mod.author} · v{mod.version}
                    </span>
                    <small>
                      {mod.kind} · {mod.size} MB
                      {mod.deps.length > 0 && t(' · 含依赖')}
                      {t(' · 示例')}
                    </small>
                  </Button>
                  <Button
                    className={`catalog-install ${compatible ? '' : 'incompatible'}`}
                    data-action={`${compatible ? 'mw-plan-' : 'mw-incompatible-'}${mod.id}`}
                    aria-label={t(
                      !compatible
                        ? '查看兼容要求：{mod}'
                        : local
                          ? update
                            ? '更新{mod}'
                            : '查看{mod}'
                          : '下载{mod}',
                      { mod: mod.title },
                    )}
                    onClick={() =>
                      compatible
                        ? plan([mod.id])
                        : notice(
                            t('需要匹配的 tML 版本'),
                            t('{mod} 需要预览配置。请先选择匹配版本，再检查其依赖。', {
                              mod: mod.title,
                            }),
                            <Button variant="soft" onClick={instances}>
                              {t('选择游戏配置')}
                            </Button>,
                          )
                    }
                  >
                    <Icon name={!compatible ? 'info' : local && !update ? 'check' : 'download'} />
                    <span>
                      {!compatible
                        ? t('版本要求')
                        : local
                          ? update
                            ? t('更新')
                            : t('已安装')
                          : t('下载')}
                    </span>
                  </Button>
                </div>
              );
            })}
          </section>
        ) : (
          <EmptyState
            symbol="search"
            title={t('没有匹配的模组')}
            description={t('修改关键词或筛选条件后再试。')}
          >
            <Button
              variant="soft"
              data-action="mw-reset-search"
              onClick={() =>
                update((current) => ({
                  ...current,
                  filters: { ...defaultFilters },
                }))
              }
            >
              {t('重置搜索')}
            </Button>
          </EmptyState>
        )}
      </div>
      <p className="list-caption">
        <Icon name="info" />
        {t('当前展示示例目录与下载流程；未连接真实创意工坊。')}
      </p>
    </>
  );
}
