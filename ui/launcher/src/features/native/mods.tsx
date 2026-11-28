import { useEffect, useRef, useState } from 'react';
import { NativePacks } from './packs';
import {
  Badge,
  Button,
  EmptyState,
  Icon,
  IconButton,
  Switch,
  WorkspaceTabs,
} from '../../components/ui';
import { useTranslation } from '../../i18n';
import { useLauncher } from '../../state/launcher';
import { nativeRequest } from '../../native/bridge';
import type { NativeMod } from '../../native/models';
import { ModDependencies } from './mod-dependencies';
import { ModSortSelect, compareInstalledMods } from '../mod-sort';

export function NativeModsPage() {
  const { t, locale } = useTranslation();
  const { state, update, nativeState, openDialog, closeDialog, openWorkspace } = useLauncher();
  useEffect(() => {
    if (state.modTab !== 'browse') return;
    update((current) => ({ ...current, modTab: 'installed' }));
    openWorkspace('resource-mods');
  }, [state.modTab, update, openWorkspace]);
  const [query, setQuery] = useState('');
  const [filter, setFilter] = useState('all');
  const [icons, setIcons] = useState<Record<string, string>>({});
  const requestedIcons = useRef(new Set<string>());
  const iconKey = (mod: NativeMod) =>
    JSON.stringify([nativeState?.activeInstance, mod.id, mod.version, mod.size, mod.changed]);
  const mods = nativeState?.mods || [];
  const normalizedQuery = query.trim().toLocaleLowerCase(locale);
  const filteredMods = mods
    .filter(
      (mod) =>
        `${mod.title} ${mod.author || ''} ${mod.id}`
          .toLocaleLowerCase(locale)
          .includes(normalizedQuery) &&
        (filter === 'all' || (filter === 'enabled' ? mod.enabled : !mod.enabled)),
    )
    .sort(compareInstalledMods(state.localSort, locale));
  useEffect(() => {
    const pending = filteredMods
      .filter((mod) => !mod.error && !requestedIcons.current.has(iconKey(mod)))
      .slice(0, 24);
    if (!pending.length) return;
    pending.forEach((mod) => requestedIcons.current.add(iconKey(mod)));
    void nativeRequest<Record<string, string>>('mods.icons', {
      ids: pending.map((mod) => mod.id),
    })
      .then((loaded) => {
        setIcons((current) => ({
          ...current,
          ...Object.fromEntries(pending.filter((mod) => loaded[mod.id]).map((mod) => [iconKey(mod), loaded[mod.id]])),
        }));
      })
      .catch(() => {
        pending.forEach((mod) => requestedIcons.current.delete(iconKey(mod)));
      });
  }, [filteredMods, nativeState?.busy]);
  const run = (method: string, params: object = {}) =>
    void nativeRequest(method, params).catch((error: Error) =>
      openDialog({ title: t('操作未完成'), description: error.message }),
    );
  const confirmDelete = (id: string, title: string) =>
    openDialog({
      title: t('删除 {name}', { name: title }),
      description: t('模组文件将从当前目录删除，依赖它的模组会一并停用；存档保留。'),
      actions: (
        <>
          <Button onClick={() => closeDialog()}>{t('取消')}</Button>
          <Button
            variant="primary"
            onClick={() => {
              closeDialog();
              run('mods.delete', { id });
            }}
          >
            {t('删除模组')}
          </Button>
        </>
      ),
    });
  const installed = (
    <>
      <div className="native-mod-tools">
        <label className="search-field">
          <Icon name="search" />
          <input
            type="search"
            aria-label={t('搜索已安装模组')}
            placeholder={t('搜索名称或作者')}
            value={query}
            onChange={(event) => setQuery(event.target.value)}
          />
        </label>
        <div className="filter-group" role="group" aria-label={t('筛选模组')}>
          {[
            ['all', '全部'],
            ['enabled', '已启用'],
            ['disabled', '已停用'],
          ].map(([key, label]) => (
            <Button
              className={`filter ${filter === key ? 'active' : ''}`}
              aria-pressed={filter === key}
              key={key}
              onClick={() => setFilter(key)}
            >
              {t(label)}
            </Button>
          ))}
        </div>
        <ModSortSelect />
        <IconButton
          symbol="sliders"
          label={t('模组批量操作')}
          disabled={nativeState?.busy || !mods.some((mod) => !mod.error && !mod.builtin)}
          onClick={() =>
            openDialog({
              title: t('批量管理'),
              description: t('作用于全部可管理模组。停用时会联动停用依赖项，下次启动生效。'),
              body: (
                <div className="pack-options">
                  {[true, false].map((enabled) => (
                    <Button
                      key={String(enabled)}
                      onClick={() => {
                        closeDialog();
                        run('mods.enable', {
                          ids: mods
                            .filter((mod) => !mod.error && !mod.builtin)
                            .map((mod) => mod.id),
                          enabled,
                        });
                      }}
                    >
                      {t(enabled ? '全部启用' : '全部停用')}
                    </Button>
                  ))}
                </div>
              ),
            })
          }
        />
      </div>
      {!mods.length ? (
        <EmptyState
          symbol="mod"
          title={t('还没有模组')}
          description={t('使用右上角「添加模组」下载或导入游戏模组。')}
        />
      ) : !filteredMods.length ? (
        <EmptyState
          symbol="search"
          title={t('没有匹配的模组')}
          description={t('试试其他关键词或清除筛选。')}
        >
          <Button
            onClick={() => {
              setQuery('');
              setFilter('all');
            }}
          >
            {t('清除筛选')}
          </Button>
        </EmptyState>
      ) : (
        <section className="panel native-mod-list" aria-label={t('已安装模组')}>
          {filteredMods.map((mod) => (
            <div className="resource-row native-mod-row" key={mod.id}>
              <span className="resource-icon">
                {icons[iconKey(mod)] ? <img src={icons[iconKey(mod)]} alt="" /> : <Icon name="mod" />}
              </span>
              <div className="resource-copy">
                <h3>
                  {mod.title} <Badge>{mod.version || t('无法读取')}</Badge>{' '}
                  {mod.builtin && <Badge tone="green">{t('内置触控')}</Badge>}
                </h3>
                <p>
                  {mod.author || mod.id} · {(mod.size / 1048576).toFixed(1)} MB
                </p>
                {mod.error && <p>{mod.error}</p>}
              </div>
              <div className="native-mod-actions">
                {!mod.error && (
                  <Button
                    aria-label={t('查看 {name} 的依赖', { name: mod.title })}
                    onClick={() =>
                      openDialog({
                        title: t('{name} · 模组依赖', { name: mod.title }),
                        description: t('根据已安装模组和启用状态检查。'),
                        body: <ModDependencies mod={mod} />,
                      })
                    }
                  >
                    {t('依赖')}
                  </Button>
                )}
                {!mod.builtin && (
                  <>
                    <Switch
                      disabled={nativeState?.busy || !!mod.error}
                      label={t('启用{name}', { name: mod.title })}
                      checked={mod.enabled}
                      onChange={(enabled) => run('mods.enable', { ids: [mod.id], enabled })}
                    />
                    <Button
                      disabled={nativeState?.busy}
                      onClick={() => confirmDelete(mod.id, mod.title)}
                    >
                      {t('删除')}
                    </Button>
                  </>
                )}
              </div>
            </div>
          ))}
        </section>
      )}
    </>
  );
  const packs = <NativePacks />;
  return (
    <div className="native-mods-workspace">
      <WorkspaceTabs
        value={state.modTab === 'browse' ? 'installed' : state.modTab}
        onChange={(modTab) => update((current) => ({ ...current, modTab }))}
        label={t('模组分类')}
        items={[
          { value: 'installed', label: t('已安装'), count: mods.length, content: installed },
          {
            value: 'packs',
            label: t('模组包'),
            count: nativeState?.packs.length || 0,
            content: packs,
          },
        ]}
      />
    </div>
  );
}
