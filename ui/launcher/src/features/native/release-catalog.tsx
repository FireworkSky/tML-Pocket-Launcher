import { useEffect, useRef, useState } from 'react';
import { Badge, Button, EmptyState, Icon, IconButton } from '../../components/ui';
import { useTranslation } from '../../i18n';
import { nativeRequest } from '../../native/bridge';
import type { NativeRelease, ReleasePage } from '../../native/models';
import { useLauncher } from '../../state/launcher';
import { useNativeActions } from './actions';
import { useDownloads } from './download-state';
import {
  ResourceBrowserLayout,
  ResourceSearch,
  ResourceSelect,
  ResourceReset,
} from './resource-layout';

const RELEASE_SOURCE_KEY = 'tml-release-source';
type SourceChoice = 'auto' | 'gitee' | 'github';

function savedReleaseSource(): SourceChoice {
  try {
    const saved = localStorage.getItem(RELEASE_SOURCE_KEY);
    if (saved === 'gitee' || saved === 'github') return saved;
  } catch {
    // The selector still works if storage is unavailable.
  }
  return 'auto';
}

export function NativeReleaseCatalog() {
  const { t, locale } = useTranslation();
  const launcher = useLauncher();
  const { nativeState, closeDialog, openDialog } = launcher;
  const { installedVersions, importInfo } = useNativeActions(launcher);
  const downloads = useDownloads();
  const [sourceChoice, setSourceChoice] = useState(savedReleaseSource);
  const regionalSource = nativeState?.releaseSource;
  const [items, setItems] = useState<NativeRelease[]>([]);
  const [page, setPage] = useState(1);
  const [more, setMore] = useState(true);
  const [loading, setLoading] = useState(false);
  const [failure, setFailure] = useState<Error | string>();
  const failureMessage = failure instanceof Error ? failure.message : failure && t(failure);
  const [channel, setChannel] = useState('all');
  const [query, setQuery] = useState('');
  const [sort, setSort] = useState('latest');
  const pending = useRef(false);
  const generation = useRef(0);
  const retryRequest = useRef({ page: 1, replace: false });
  const load = async (next: number, replace = false, source = sourceChoice) => {
    if (pending.current) return;
    pending.current = true;
    retryRequest.current = { page: next, replace };
    const token = ++generation.current;
    setLoading(true);
    setFailure(undefined);
    try {
      const result = await nativeRequest<ReleasePage>('releases', { page: next, source });
      if (token !== generation.current) return;
      setItems((old) => {
        const previous = replace ? [] : old;
        return [
          ...previous,
          ...result.releases.filter((release) => !previous.some((item) => item.id === release.id)),
        ];
      });
      setMore(result.hasMore);
      setPage(next + 1);
    } catch (error) {
      if (token === generation.current)
        setFailure(error instanceof Error ? error : '版本列表读取失败，请重试。');
    } finally {
      if (token === generation.current) {
        pending.current = false;
        setLoading(false);
      }
    }
  };
  useEffect(() => {
    void load(1);
    return () => {
      generation.current++;
      pending.current = false;
    };
  }, []);
  const changeSource = (value: string) => {
    if (value !== 'auto' && value !== 'gitee' && value !== 'github') return;
    if (sourceChoice === value || pending.current || nativeState?.busy) return;
    setSourceChoice(value);
    setItems([]);
    setMore(false);
    setPage(1);
    try {
      localStorage.setItem(RELEASE_SOURCE_KEY, value);
    } catch {
      // This selection still applies while the catalog is open.
    }
    void load(1, true, value);
  };
  const reset = () => {
    setChannel('all');
    setQuery('');
    setSort('latest');
  };
  const visible = items
    .filter(
      (item) =>
        (channel === 'all' || (item.preview ? 'preview' : 'stable') === channel) &&
        `${item.tag} ${item.name}`.toLowerCase().includes(query.trim().toLowerCase()),
    )
    .sort((a, b) =>
      sort === 'name'
        ? b.tag.localeCompare(a.tag, undefined, { numeric: true })
        : sort === 'oldest'
          ? a.id.localeCompare(b.id, undefined, { numeric: true })
          : b.id.localeCompare(a.id, undefined, { numeric: true }),
    );
  const install = (release: NativeRelease) =>
    openDialog({
      title: t('安装 {version}', { version: release.tag }),
      description: t(
        '{size}官方发行包。独立安装并设为当前版本；同版本重新安装只替换该版本文件。保留模组与存档。',
        { size: release.size > 0 ? `${(release.size / 1048576).toFixed(1)} MB · ` : '' },
      ),
      actions: (
        <>
          <Button onClick={() => closeDialog()}>{t('取消')}</Button>
          <Button
            variant="primary"
            onClick={() =>
              downloads.start({
                method: 'download',
                params: { id: release.id },
                name: `tModLoader ${release.tag}`,
                source: release.id.startsWith('gitee-') ? 'Gitee' : 'GitHub',
              })
            }
          >
            {t('下载并安装')}
          </Button>
        </>
      ),
    });
  return (
    <ResourceBrowserLayout
      title={t('tModLoader 发行版本')}
      summary={t(
        more
          ? '{matches} 个匹配 · 已加载 {loaded} 个版本，可继续加载'
          : '{matches} 个匹配 · 已加载 {loaded} 个版本',
        {
          matches: visible.length.toLocaleString(locale),
          loaded: items.length.toLocaleString(locale),
        },
      )}
      toolbar={
        <>
          <div className="resource-channels" role="group" aria-label={t('发行通道')}>
            {[
              ['all', '全部'],
              ['stable', '稳定版'],
              ['preview', '预览版'],
            ].map(([id, label]) => (
              <Button
                key={id}
                aria-pressed={channel === id}
                className={`resource-channel ${channel === id ? 'active' : ''}`}
                onClick={() => setChannel(id)}
              >
                {channel === id && <Icon name="check" />}
                {t(label)}
              </Button>
            ))}
          </div>
          <IconButton
            symbol="refresh"
            label={t('刷新版本列表')}
            disabled={loading || nativeState?.busy}
            onClick={() => void load(1, true)}
          />
        </>
      }
      filters={
        <>
          <ResourceSearch
            label={t('搜索游戏版本')}
            value={query}
            onChange={setQuery}
            placeholder={t('版本号或发行名称')}
          />
          <ResourceSelect
            label={t('排序方式')}
            value={sort}
            onChange={setSort}
            options={[
              ['latest', t('发行较新优先')],
              ['oldest', t('发行较早优先')],
              ['name', t('版本号')],
            ]}
          />
          <ResourceSelect
            label={t('版本来源')}
            value={sourceChoice}
            onChange={changeSource}
            disabled={loading || nativeState?.busy}
            options={[
              [
                'auto',
                regionalSource
                  ? t('按地区自动（{source}）', { source: regionalSource })
                  : t('按地区自动'),
              ],
              ['gitee', 'Gitee'],
              ['github', 'GitHub'],
            ]}
          />
          <Button onClick={() => importInfo('tml')}>{t('导入 ZIP')}</Button>
          <Button onClick={installedVersions}>{t('管理已安装版本')}</Button>
          <ResourceReset onClick={reset} />
        </>
      }
    >
      {failureMessage && (
        <div className="resource-catalog-error" role="alert">
          <p>{failureMessage}</p>
          <Button
            disabled={loading || nativeState?.busy}
            onClick={() => void load(retryRequest.current.page, retryRequest.current.replace)}
          >
            {t('重试')}
          </Button>
        </div>
      )}
      {visible.map((release) => (
        <article className="release-row resource-catalog-card" key={release.id}>
          <span className="resource-cover">
            <img src="/tmodloader.png" alt="" />
          </span>
          <div className="resource-card-copy">
            <h3>{release.name}</h3>
            <div className="resource-card-meta">
              <strong>{release.tag}</strong>
              <Badge>{t(release.preview ? '预览版' : '稳定版')}</Badge>
              {nativeState?.tml?.replace(/^v/, '') === release.tag.replace(/^v/, '') && (
                <Badge tone="green">{t('当前版本')}</Badge>
              )}
            </div>
            <p>
              {t('{size}tModLoader 官方发行包', {
                size: release.size > 0 ? `${(release.size / 1048576).toFixed(1)} MB · ` : '',
              })}
            </p>
          </div>
          <div className="resource-card-end">
            <span className="resource-provider">
              {release.id.startsWith('gitee-') ? 'Gitee' : 'GitHub'}
            </span>
            <Button disabled={nativeState?.busy} onClick={() => install(release)}>
              <Icon name="download" />
              {t(locale === 'en' ? '下载此版本' : '下载')}
            </Button>
          </div>
        </article>
      ))}
      {!loading && !failureMessage && !visible.length && (
        <EmptyState
          symbol="search"
          title={t(items.length ? '没有匹配的版本' : '暂未找到发行版本')}
          description={t(
            more ? '可调整筛选或继续加载更多官方版本。' : '请调整关键词、切换发行通道或刷新列表。',
          )}
        >
          <ResourceReset onClick={reset} />
        </EmptyState>
      )}
      {loading && (
        <div className="resource-loading" role="status">
          <span />
          {t('正在读取官方版本…')}
        </div>
      )}
      {more && !failureMessage && (
        <Button
          className="resource-load-more"
          disabled={loading || nativeState?.busy}
          onClick={() => void load(page)}
        >
          {t('加载更多')}
        </Button>
      )}
    </ResourceBrowserLayout>
  );
}
