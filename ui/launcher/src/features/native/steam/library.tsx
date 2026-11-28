import { useEffect, useState } from 'react';
import { Badge, Button, Icon } from '../../../components/ui';
import { useTranslation } from '../../../i18n';
import { nativeRequest } from '../../../native/bridge';
import { useLauncher } from '../../../state/launcher';
import { SteamBrand } from './brand';
import { translateSteamAccountMessage, useSteam } from './state';
import { useDownloads } from '../download-state';

export function SteamLibraryPage() {
  const { t, locale } = useTranslation();
  const downloads = useDownloads();
  const { nativeState, openWorkspace } = useLauncher();
  const {
    account,
    connectionError,
    refresh,
    download,
    startDownload,
    details,
    detailsLoading,
    detailsError,
    loadDetails,
  } = useSteam();
  const [imageFailed, setImageFailed] = useState(false);
  useEffect(() => setImageFailed(false), [details?.headerImage]);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState('');
  const ready = account?.phase === 'ready';
  const listSeparator = locale === 'en' ? ', ' : '、';
  const logout = async () => {
    setPending(true);
    try {
      await nativeRequest('store.steam.logout');
      await refresh();
      openWorkspace('steam-login');
    } catch (failure) {
      setError((failure as Error).message);
    } finally {
      setPending(false);
    }
  };
  return (
    <section className="steam-page steam-library-page" aria-labelledby="steam-library-title">
      <div className="steam-library-top">
        <SteamBrand />
        <div className="steam-account-chip">
          <Icon name="user" />
          <span>
            {account?.account || t('Steam 账户')}
            <small>{ready ? t('已连接') : t('连接中断')}</small>
          </span>
          <Button
            className="steam-text-button"
            disabled={pending || nativeState?.busy}
            onClick={() => void logout()}
          >
            {t('退出登录')}
          </Button>
        </div>
      </div>
      {(error || connectionError) && (
        <p className="steam-error" role="alert">
          {error || connectionError}
        </p>
      )}
      {!ready && (
        <div className="steam-offline">
          <p role="status">{translateSteamAccountMessage(account, t) || t('正在读取账户状态…')}</p>
          <Button onClick={() => openWorkspace('steam-login')}>{t('管理 Steam 连接')}</Button>
        </div>
      )}
      <div className="steam-game-card">
        <div
          className={`steam-game-art ${details?.headerImage && !imageFailed ? 'has-artwork' : ''}`}
        >
          {details?.headerImage && !imageFailed ? (
            <img
              src={details.headerImage}
              alt={t('{name} · Steam 官方封面', { name: details.name })}
              onError={() => setImageFailed(true)}
            />
          ) : (
            <>
              <img className="steam-game-fallback" src="/terraria.png" alt="" />
              <span>{details?.name || 'Terraria'}</span>
              <small>{detailsLoading ? t('正在获取 Steam 封面…') : t('Steam 游戏资源')}</small>
            </>
          )}
        </div>
        <div className="steam-game-details">
          <div className="steam-game-label">
            <span className="steam-eyebrow">{t('你的游戏库')}</span>
            {ready && (
              <Badge tone={account?.owned ? 'green' : ''}>
                {account?.owned ? t('拥有 Terraria') : t('未拥有游戏')}
              </Badge>
            )}
          </div>
          <h2 id="steam-library-title">{details?.name || 'Terraria'}</h2>
          <p>
            {details?.description ||
              (detailsLoading
                ? t('正在从 Steam 获取游戏简介…')
                : t('从 Steam 下载已购 Terraria 的游戏资源。'))}
          </p>
          {details && (
            <>
              {details.genres.length > 0 && (
                <div className="steam-game-genres" aria-label={t('Steam 游戏类型')}>
                  {details.genres.map((genre) => (
                    <span key={genre}>{genre}</span>
                  ))}
                </div>
              )}
              <dl className="steam-store-facts">
                {details.developers.length > 0 && (
                  <div>
                    <dt>{t('开发商')}</dt>
                    <dd>{details.developers.join(listSeparator)}</dd>
                  </div>
                )}
                {details.publishers.length > 0 && (
                  <div>
                    <dt>{t('发行商')}</dt>
                    <dd>{details.publishers.join(listSeparator)}</dd>
                  </div>
                )}
                {details.releaseDate && (
                  <div>
                    <dt>{t('发行日期')}</dt>
                    <dd>{details.releaseDate}</dd>
                  </div>
                )}
              </dl>
            </>
          )}
          <dl className="steam-facts">
            <div>
              <dt>{t('分支')}</dt>
              <dd>Linux · public</dd>
            </div>
            <div>
              <dt>{t('内容')}</dt>
              <dd>{t('游戏资源 Content')}</dd>
            </div>
            <div>
              <dt>{t('安装')}</dt>
              <dd>{t('下载后自动校验')}</dd>
            </div>
          </dl>
          {download?.status === 'running' ? (
            <Button className="steam-primary" onClick={() => downloads.show(download.id)}>
              <Icon name="download" />
              {t('查看下载进度')}
            </Button>
          ) : (
            <Button
              className="steam-primary"
              disabled={!ready || !account?.owned || nativeState?.busy}
              onClick={startDownload}
            >
              <Icon name="download" />
              {t('下载 Terraria 资源')}
            </Button>
          )}
          <small className="steam-install-note">
            {nativeState?.terraria
              ? t('本机已有资源，重新下载将更新资源文件。模组与存档保留。')
              : t('下载完成后会自动安装到启动器。模组与存档保留。')}
          </small>
        </div>
      </div>
      <div className="steam-details-source">
        <span role="status">
          {detailsLoading
            ? t('正在读取 Steam 商店资料…')
            : detailsError ||
              (details?.stale
                ? t('Steam 暂时不可用，显示已缓存的详情。')
                : details
                  ? t('游戏详情来自 Steam 商店')
                  : t('尚未读取游戏详情'))}
        </span>
        <Button
          className="steam-text-button"
          disabled={detailsLoading}
          onClick={() => void loadDetails(true)}
        >
          {t('刷新详情')}
        </Button>
      </div>
      <div className="steam-library-bottom">
        <span>
          <Icon name="folder" />
          {nativeState?.terraria ? t('本机已安装 Terraria 资源') : t('本机尚未安装 Terraria 资源')}
        </span>
        <Button className="steam-text-button" onClick={() => openWorkspace('downloads')}>
          {t('查看下载')}
          <Icon name="arrow" />
        </Button>
      </div>
    </section>
  );
}
