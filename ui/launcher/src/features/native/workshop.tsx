import { useCallback, useEffect, useRef, useState } from 'react';
import { Badge, Button, EmptyState, Icon } from '../../components/ui';
import { LabeledSelect } from '../../components/form-controls';
import { nativeRequest } from '../../native/bridge';
import { useLauncher } from '../../state/launcher';
import { useNativeActions } from './actions';
import { useSteam } from './steam/state';
import { useDownloads } from './download-state';
import { NativeField } from './controls';
import { ResourceBrowserLayout, ResourceSearch } from './resource-layout';
import { useTranslation } from '../../i18n';

type WorkshopApp = 105600 | 1281930;
type SortMode =
  | 'popular'
  | 'highestRated'
  | 'newest'
  | 'updated'
  | 'subscriptions'
  | 'playtimeTrend'
  | 'averagePlaytimeTrend'
  | 'totalPlaytime'
  | 'averagePlaytime'
  | 'sessionsTrend'
  | 'totalSessions';
interface WorkshopItem {
  appId: WorkshopApp;
  id: string;
  title: string;
  description: string;
  creator: string;
  previewUrl: string;
  subscriptions: number;
  votesUp: number;
  votesDown: number;
  tags: string[];
  keyValues: Record<string, string>;
  dependencyIds?: string[];
}
interface WorkshopPage {
  items: WorkshopItem[];
  total: number;
}

const WORKSHOP_PAGE_SIZE = 20;
let modFrontPage: { scope: string; expires: number; value: WorkshopPage } | undefined;

function WorkshopPreview({ item }: { item: WorkshopItem }) {
  const [failedUrl, setFailedUrl] = useState<string>();
  return item.previewUrl && failedUrl !== item.previewUrl ? (
    <img
      src={item.previewUrl}
      alt=""
      loading="lazy"
      onError={() => setFailedUrl(item.previewUrl)}
    />
  ) : (
    <Icon name={item.appId === 1281930 ? 'mod' : 'wallpaper'} />
  );
}

const SORTS: readonly { id: SortMode; label: string }[] = [
  { id: 'popular', label: '最热门' },
  { id: 'highestRated', label: '最受好评（发布至今）' },
  { id: 'newest', label: '最近发行' },
  { id: 'updated', label: '最新更新' },
  { id: 'subscriptions', label: '不重复订阅者总计' },
  { id: 'playtimeTrend', label: '近期游戏时间' },
  { id: 'averagePlaytimeTrend', label: '近期平均游戏时间' },
  { id: 'totalPlaytime', label: '总计游戏时间' },
  { id: 'averagePlaytime', label: '总计平均游戏时间' },
  { id: 'sessionsTrend', label: '游戏会话数' },
  { id: 'totalSessions', label: '总计游戏会话数' },
];
const DAY_RANGES = [
  [1, '今天'],
  [7, '1 周'],
  [30, '30 天'],
  [90, '3 个月'],
  [180, '6 个月'],
  [365, '1 年'],
  [0, '发布至今'],
] as const;
const TERRARIA_WORLD_CATEGORIES = [
  ['Adventure Worlds', 'Adventure'],
  ['Golf Worlds', 'Golf'],
  ['All Items Worlds', 'All Items'],
  ['Starter Worlds', 'Starter'],
  ['Journey Worlds', 'Journey'],
  ['Classic Worlds', 'Classic'],
  ['Expert Worlds', 'Expert'],
  ['Master Worlds', 'Master'],
  ['Challenge Worlds', 'Challenge'],
  ['Corruption Worlds', 'Corruption'],
  ['Crimson Worlds', 'Crimson'],
  ['Small Worlds', 'Small'],
  ['Medium Worlds', 'Medium'],
  ['Large Worlds', 'Large'],
  ['Other Worlds', 'Other Worlds'],
] as const;
const TERRARIA_PACK_CATEGORIES = [
  ['From Terraria Mods', 'From Terraria Mods'],
  ['Popular Culture', 'Popular Culture'],
  ['Fun/Silly', 'Fun/Silly'],
  ['High Resolution', 'High Resolution'],
  ['Music', 'Music'],
  ['Language/Translations', 'Language/Translations'],
  ['Overhaul', 'Overhaul'],
  ['Tweaks', 'Tweaks'],
  ['Other Packs', 'Other Packs'],
] as const;
const TML_CATEGORIES = [
  'New Content',
  'Utilities',
  'Library',
  'Translation',
  'Quality of Life',
  'Gameplay Tweaks',
  'Audio Tweaks',
  'Visual Tweaks',
  'Custom World Gen',
];
const TML_VERSIONS = ['1.3', '1.4.3', '1.4.4', '1.4.4-Transitive'];
const TML_LANGUAGES = [
  'English',
  'German',
  'Italian',
  'French',
  'Spanish',
  'Russian',
  'Chinese',
  'Portuguese',
  'Polish',
];

export function NativeWorkshopCatalog({ appId }: { appId: WorkshopApp }) {
  const { t, locale } = useTranslation();
  const launcher = useLauncher();
  const { nativeState, notify } = launcher;
  const { importInfo } = useNativeActions(launcher);
  const downloads = useDownloads();
  const { account } = useSteam();
  const scope = account ? JSON.stringify([account.phase, account.account]) : undefined;
  const opened = useRef(false);
  const [query, setQuery] = useState('');
  const [projectId, setProjectId] = useState('');
  const [sort, setSort] = useState<SortMode>('popular');
  const [days, setDays] = useState(90);
  const [category, setCategory] = useState('');
  const [resourceType, setResourceType] = useState<'worlds' | 'packs'>('worlds');
  const [version, setVersion] = useState('');
  const [language, setLanguage] = useState('');
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<WorkshopPage | null>(null);
  const [hasMore, setHasMore] = useState(false);
  const [detail, setDetail] = useState<WorkshopItem | null>(null);
  const [failure, setFailure] = useState<Error | string>();
  const failureMessage = failure instanceof Error ? failure.message : failure && t(failure);
  const [loading, setLoading] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const generation = useRef(0);
  const activeQuery = useRef<{
    appId: WorkshopApp;
    search: string;
    sort: SortMode;
    days: number;
    category?: string;
    version?: string;
    language?: string;
  }>(null);
  useEffect(
    () => () => {
      generation.current++;
    },
    [],
  );

  const title = t(appId === 1281930 ? 'tModLoader 模组' : 'Terraria 资源');
  const categoryLabel = t(
    appId === 1281930 ? '模组分类' : resourceType === 'worlds' ? '世界分类' : '资源包分类',
  );

  const search = useCallback(
    async (nextPage = 1, sortOverride = sort, daysOverride = days, useCache = false) => {
      if (loading || loadingMore || nativeState?.busy) return;
      const append = nextPage > 1;
      const filters = append
        ? activeQuery.current
        : {
            appId,
            search: query.trim(),
            sort: sortOverride,
            days: daysOverride,
            category: category || undefined,
            version: version || undefined,
            language: language || undefined,
          };
      if (!filters) return;
      const token = ++generation.current;
      const setPending = append ? setLoadingMore : setLoading;
      setPending(true);
      setFailure(undefined);
      if (!append) setDetail(null);
      try {
        const frontPage = !append && filters.appId === 1281930 && !filters.search &&
          !filters.category && !filters.version && !filters.language && filters.sort === 'popular' && filters.days === 90;
        const cached = useCache && frontPage && scope && modFrontPage?.scope === scope &&
          modFrontPage.expires > Date.now() ? modFrontPage.value : undefined;
        const value = cached ?? await nativeRequest<WorkshopPage>('workshop.query', {
          ...filters,
          page: nextPage,
          pageSize: WORKSHOP_PAGE_SIZE,
        });
        if (frontPage && scope && !cached) {
          modFrontPage = { scope, expires: Date.now() + 5 * 60_000, value };
        }
        if (token === generation.current) {
          activeQuery.current = filters;
          setResult((previous) => {
            const items = append ? [...(previous?.items ?? [])] : [];
            const seen = new Set(items.map((item) => item.id));
            for (const item of value.items) {
              if (seen.has(item.id)) continue;
              seen.add(item.id);
              items.push(item);
            }
            return { items, total: value.total };
          });
          setHasMore(value.items.length > 0 && nextPage * WORKSHOP_PAGE_SIZE < value.total);
          setPage(nextPage);
        }
      } catch (error) {
        if (token === generation.current)
          setFailure(error instanceof Error ? error : '工坊目录读取失败，请重试。');
      } finally {
        if (token === generation.current) setPending(false);
      }
    },
    [
      appId,
      category,
      days,
      language,
      loading,
      loadingMore,
      nativeState?.busy,
      query,
      scope,
      sort,
      version,
    ],
  );

  useEffect(() => {
    if (appId !== 1281930 || opened.current || nativeState?.busy) return;
    const timer = window.setTimeout(() => {
      opened.current = true;
      void search(1, 'popular', 90, true);
    }, 0);
    return () => window.clearTimeout(timer);
  }, [appId, nativeState?.busy, search]);

  const openDetail = async (item: WorkshopItem) => {
    if (loading || loadingMore || nativeState?.busy) return;
    const token = ++generation.current;
    setDetail(item);
    setProjectId(item.id);
    setFailure(undefined);
    try {
      const value = await nativeRequest<WorkshopItem>('workshop.details', {
        appId: item.appId,
        id: item.id,
      });
      if (token === generation.current && value.appId === item.appId && value.id === item.id)
        setDetail(value);
    } catch (error) {
      if (token === generation.current)
        setFailure(error instanceof Error ? error : '工坊项目读取失败，请重试。');
    }
  };

  const lookup = async () => {
    const id = projectId.trim();
    if (!/^[0-9]{1,20}$/.test(id) || loading || loadingMore || nativeState?.busy) return;
    const token = ++generation.current;
    setLoading(true);
    setFailure(undefined);
    try {
      const item = await nativeRequest<WorkshopItem>('workshop.details', { appId, id });
      if (token !== generation.current) return;
      if (item.appId !== appId || item.id !== id) setFailure('Steam 返回的项目与所选工坊不匹配');
      else setDetail(item);
    } catch (error) {
      if (token === generation.current)
        setFailure(error instanceof Error ? error : '工坊项目读取失败，请重试。');
    } finally {
      if (token === generation.current) setLoading(false);
    }
  };

  const declaredReferences = Object.entries(detail?.keyValues ?? {}).find(
    ([name]) => name.toLowerCase() === 'modreferences',
  )?.[1];
  const dependencyNames = [
    ...new Set(
      declaredReferences
        ?.split(',')
        .map((name) => name.trim())
        .filter(Boolean),
    ),
  ];
  const dependencyIds = detail?.dependencyIds ?? [];
  const isLoading = loading || loadingMore || nativeState?.busy;
  const categoryOptions =
    appId === 1281930
      ? TML_CATEGORIES.map((item) => [item, item] as const)
      : resourceType === 'worlds'
        ? TERRARIA_WORLD_CATEGORIES
        : TERRARIA_PACK_CATEGORIES;

  const filters = (
    <>
      <form
        className="workshop-search-form"
        onSubmit={(event) => {
          event.preventDefault();
          void search();
        }}
      >
        <ResourceSearch
          label={t('搜索创意工坊')}
          value={query}
          onChange={setQuery}
          placeholder={t('搜索名称或关键词')}
        />
        <Button type="submit" disabled={isLoading}>
          <Icon name="search" />
          <span>{t(loading ? '正在搜索…' : '搜索工坊')}</span>
        </Button>
      </form>
      {appId === 105600 && (
        <LabeledSelect
          className="workshop-category-filter"
          label={t('资源类型')}
          value={resourceType}
          onChange={(value) => {
            setResourceType(value as 'worlds' | 'packs');
            setCategory('');
          }}
          options={[
            ['worlds', t('世界')],
            ['packs', t('资源包')],
          ]}
        />
      )}
      <LabeledSelect
        className="workshop-category-filter"
        label={categoryLabel}
        value={category}
        onChange={setCategory}
        options={[['', t('全部内容')], ...categoryOptions]}
      />
      {appId === 1281930 && (
        <>
          <LabeledSelect
            className="workshop-category-filter"
            label={t('tModLoader 版本')}
            value={version}
            onChange={setVersion}
            options={[['', t('全部版本')], ...TML_VERSIONS.map((item) => [item, item] as const)]}
          />
          <LabeledSelect
            className="workshop-category-filter"
            label={t('模组语言')}
            value={language}
            onChange={setLanguage}
            options={[['', t('全部语言')], ...TML_LANGUAGES.map((item) => [item, item] as const)]}
          />
        </>
      )}
      <form
        className="resource-filter-tools"
        onSubmit={(event) => {
          event.preventDefault();
          void lookup();
        }}
      >
        <NativeField
          label={t('创意工坊项目编号')}
          value={projectId}
          onChange={setProjectId}
          placeholder={t('输入项目编号')}
        />
        <Button type="submit" disabled={isLoading || !/^[0-9]{1,20}$/.test(projectId.trim())}>
          {t('按编号查看')}
        </Button>
      </form>
      <div className="resource-source">
        <span>{t('当前工坊')}</span>
        <Badge>
          {appId} · {appId === 1281930 ? 'tModLoader' : 'Terraria'}
        </Badge>
        <p>{t('未登录时尝试匿名访问；Steam 限制访问时，请登录 Steam 账户后重试。')}</p>
      </div>
      {appId === 1281930 && <Button onClick={() => importInfo('mod')}>{t('导入本地模组')}</Button>}
    </>
  );

  return (
    <ResourceBrowserLayout
      compactFilters={appId === 1281930}
      title={title}
      summary={
        detail
          ? t('工坊项目 · {id}', { id: detail.id })
          : result
            ? t('找到 {count} 个项目', { count: result.total.toLocaleString(locale) })
            : t('搜索并浏览 Steam 创意工坊')
      }
      toolbar={
        <div className="workshop-sort-tabs" aria-label={t('工坊排序')}>
          <LabeledSelect
            label={t('排序顺序')}
            value={sort}
            disabled={isLoading}
            onChange={(selection) => {
              const value = selection as SortMode;
              setSort(value);
              void search(1, value);
            }}
            options={SORTS.map((item) => [item.id, t(item.label)])}
          />
          <LabeledSelect
            label={t('时间范围')}
            value={String(days)}
            disabled={isLoading}
            onChange={(selection) => {
              const value = Number(selection);
              setDays(value);
              void search(1, sort, value);
            }}
            options={DAY_RANGES.map(([value, label]) => [String(value), t(label)])}
          />
        </div>
      }
      filters={filters}
    >
      {failureMessage && (!result || detail) && (
        <div className="resource-catalog-error" role="alert">
          {failureMessage}
        </div>
      )}
      {loading && (
        <div className="resource-loading" role="status">
          <span />
          {t('正在读取 Steam 创意工坊…')}
        </div>
      )}
      {detail && !loading && (
        <article className="resource-catalog-card resource-workshop-card">
          <span className="resource-cover">
            <WorkshopPreview item={detail} />
          </span>
          <div className="resource-card-copy">
            <span className="resource-provider">
              {detail.appId === 1281930 ? 'tModLoader' : 'Terraria'} Workshop
            </span>
            <h3>{detail.title || t('工坊项目 {id}', { id: detail.id })}</h3>
            <p>
              {t('创作者：{creator} · 项目编号 {id}', {
                creator: detail.creator || t('未知'),
                id: detail.id,
              })}
            </p>
            <p className="resource-workshop-description">
              {detail.description?.slice(0, 2400) || t('此项目未提供简介。')}
            </p>
            <div className="resource-card-meta">
              <strong>★ {detail.votesUp.toLocaleString(locale)}</strong>
              <span>
                {t('{count} 人订阅', { count: detail.subscriptions.toLocaleString(locale) })}
              </span>
              {detail.tags.map((tag) => (
                <Badge key={tag}>{tag}</Badge>
              ))}
            </div>
            {detail.appId === 1281930 && (
              <details className="download-mod-dependencies">
                <summary>{t('查看工坊依赖')}</summary>
                {dependencyNames.length === 0 && dependencyIds.length === 0 && (
                  <p>{t('工坊未列出依赖。')}</p>
                )}
                <ul className="mod-dependency-list">
                  {dependencyNames.map((name) => {
                    const local = nativeState?.mods.find((mod) => mod.id === name && !mod.error);
                    return (
                      <li key={name}>
                        <strong>{local?.title || name}</strong>
                        <Badge>{t(local ? '已安装' : '未安装')}</Badge>
                      </li>
                    );
                  })}
                </ul>
                {dependencyIds.length > 0 && (
                  <div className="resource-pack-actions">
                    {dependencyIds.map((id) => (
                      <Button
                        key={id}
                        onClick={() =>
                          void nativeRequest('openExternal', {
                            url: `https://steamcommunity.com/sharedfiles/filedetails/?id=${id}`,
                          }).catch((error: Error) => notify(error.message))
                        }
                      >
                        {t('工坊项目 {id}', { id })}
                      </Button>
                    ))}
                  </div>
                )}
              </details>
            )}
            <div className="resource-pack-actions">
              {result && (
                <Button
                  onClick={() => {
                    generation.current++;
                    setDetail(null);
                    setFailure(undefined);
                  }}
                >
                  {t('返回列表')}
                </Button>
              )}
              <Button
                onClick={() =>
                  void nativeRequest('openExternal', {
                    url: `https://steamcommunity.com/sharedfiles/filedetails/?id=${detail.id}`,
                  }).catch((error: Error) => notify(error.message))
                }
              >
                {t('查看 Steam 页面')}
              </Button>
              <Button
                onClick={() =>
                  downloads.start({
                    method: 'workshop.download',
                    params: { appId: detail.appId, id: detail.id, title: detail.title },
                    name: detail.title || t('工坊项目 {id}', { id: detail.id }),
                    source: t('{game} 创意工坊', {
                      game: detail.appId === 1281930 ? 'tModLoader' : 'Terraria',
                    }),
                  })
                }
                disabled={nativeState?.busy}
              >
                <Icon name="download" />
                {t('下载并安装')}
              </Button>
            </div>
          </div>
        </article>
      )}
      {!detail && !loading && result && (
        <div className="workshop-results-grid">
          {result.items.map((item) => (
            <button
              className="workshop-item"
              key={`${item.appId}:${item.id}`}
              onClick={() => void openDetail(item)}
              disabled={loadingMore}
            >
              <span className="workshop-item-image">
                <WorkshopPreview item={item} />
              </span>
              <span className="workshop-item-copy">
                <strong>{item.title || t('工坊项目 {id}', { id: item.id })}</strong>
                {item.tags.length > 0 && (
                  <span className="workshop-item-tags">{item.tags.slice(0, 3).join(' · ')}</span>
                )}
                <span>
                  ★ {item.votesUp.toLocaleString(locale)}　
                  {t('{count} 人订阅', {
                    count: item.subscriptions.toLocaleString(locale),
                  })}
                </span>
              </span>
            </button>
          ))}
          {result.items.length === 0 && (
            <EmptyState
              symbol="search"
              title={t('没有找到项目')}
              description={t('试试其他关键词或分类。')}
            />
          )}
          {failureMessage && (
            <div className="resource-catalog-error" role="alert">
              {failureMessage}
            </div>
          )}
          {result.items.length > 0 && hasMore && (
            <Button
              className="resource-load-more"
              disabled={isLoading}
              onClick={() => void search(page + 1)}
            >
              {t(loadingMore ? '正在加载…' : '加载更多')}
            </Button>
          )}
        </div>
      )}
      {appId === 1281930 && failureMessage && !detail && !result && !loading && (
        <Button disabled={isLoading} onClick={() => void search()}>{t('重试')}</Button>
      )}
      {appId === 105600 && !detail && !result && !loading && (
        <EmptyState
          symbol="mod"
          title={t('浏览 Steam 创意工坊')}
          description={t('选择工坊类型、搜索内容并查看可安装项目。')}
        >
          <Button onClick={() => void search()}>{t('加载热门项目')}</Button>
        </EmptyState>
      )}
      <div className="resource-browser-note">
        <Icon name="info" />
        <p>
          {t(
            '模组、世界与资源包会先下载到暂存区，再按工坊内容类型校验并安装。匿名访问受 Steam 项目权限限制。',
          )}
        </p>
      </div>
    </ResourceBrowserLayout>
  );
}
