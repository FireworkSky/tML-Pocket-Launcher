import { useEffect, useState } from 'react';
import { Badge, Button } from '../../components/ui';
import { LabeledSelect } from '../../components/form-controls';
import { useTranslation } from '../../i18n';
import { useLauncher } from '../../state/launcher';
import { nativeRequest } from '../../native/bridge';
import { translateDownloadProgress, useDownloads } from './download-state';

interface GogAccount {
  phase: string;
  message: string;
  account: string;
  owned: boolean;
}
interface GogInstaller {
  id: string;
  name: string;
  version: string;
  language: string;
  size: number;
}

export function NativeGogPanel() {
  const { t } = useTranslation();
  const { nativeState } = useLauncher();
  const downloads = useDownloads();
  const [account, setAccount] = useState<GogAccount | null>(null);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState('');
  const [connectionError, setConnectionError] = useState('');
  const [installers, setInstallers] = useState<GogInstaller[]>([]);
  const [selection, setSelection] = useState('');
  const ready = account?.phase === 'ready';
  const refresh = async () =>
    setAccount((await nativeRequest<{ gog: GogAccount }>('store.status')).gog);
  useEffect(() => {
    let disposed = false;
    let timer: ReturnType<typeof setTimeout>;
    const poll = async () => {
      try {
        const result = await nativeRequest<{ gog: GogAccount }>('store.status');
        if (!disposed) {
          setAccount(result.gog);
          setConnectionError('');
        }
      } catch (failure) {
        if (!disposed) setConnectionError((failure as Error).message);
      }
      if (!disposed) timer = setTimeout(poll, 1800);
    };
    void poll();
    return () => {
      disposed = true;
      clearTimeout(timer);
    };
  }, []);
  const run = async (method: string, params: object = {}) => {
    setPending(true);
    setError('');
    try {
      const result = await nativeRequest(method, params);
      await refresh();
      return result;
    } catch (failure) {
      setError((failure as Error).message);
    } finally {
      setPending(false);
    }
  };
  return (
    <div className="native-store-panel">
      <div className="resource-row">
        <div className="resource-copy">
          <strong>{account?.account || 'GOG'}</strong>
          <p role="status">
            {account?.message
              ? account.phase === 'error'
                ? account.message
                : t(account.message)
              : t('正在准备商店连接…')}
          </p>
        </div>
        {ready && (
          <Badge tone={account?.owned ? 'green' : ''}>
            {account?.owned ? t('拥有 Terraria') : t('未拥有游戏')}
          </Badge>
        )}
      </div>
      {(error || connectionError) && (
        <p className="native-store-error" role="alert">
          {error || connectionError}
        </p>
      )}
      {!ready && (
        <Button
          variant="primary"
          disabled={pending}
          onClick={() => void run('store.gog.authorize')}
        >
          {t('打开 GOG 官方登录')}
        </Button>
      )}
      {ready && account?.owned && (
        <>
          <div className="native-store-form">
            <Button
              disabled={pending || nativeState?.busy}
              onClick={() => {
                setPending(true);
                setError('');
                void nativeRequest<GogInstaller[]>('store.gog.installers')
                  .then((items) => {
                    setInstallers(items);
                    setSelection(items[0]?.id || '');
                  })
                  .catch((failure: Error) => setError(failure.message))
                  .finally(() => setPending(false));
              }}
            >
              {t('读取可下载版本')}
            </Button>
            {installers.length > 0 && (
              <LabeledSelect
                className="native-field"
                label={t('Terraria Linux 资源版本')}
                value={selection}
                onChange={setSelection}
                options={installers.map((item) => [
                  item.id,
                  `${item.version || item.name} · ${item.language} · ${Math.ceil(item.size / 1048576)} MB`,
                ])}
              />
            )}
          </div>
          <Button
            variant="primary"
            disabled={pending || nativeState?.busy || !selection}
            onClick={() =>
              downloads.start({
                method: 'store.gog.download',
                params: { id: selection },
                name: 'Terraria',
                source: 'GOG · Linux 游戏资源',
              })
            }
          >
            {t('下载 Terraria 资源')}
          </Button>
        </>
      )}
      {(ready || account?.phase === 'error') && (
        <Button
          disabled={pending || nativeState?.busy}
          onClick={() => {
            setInstallers([]);
            void run('store.gog.logout');
          }}
        >
          {t('退出登录')}
        </Button>
      )}
      {nativeState?.busy && nativeState.operation.startsWith('store.gog.') && (
        <div role="status" className="native-store-progress">
          <p>{translateDownloadProgress(nativeState.message, t)}</p>
          <progress
            aria-label={t('下载进度')}
            max={100}
            value={nativeState.progress >= 0 ? nativeState.progress : undefined}
          />
          <Button onClick={() => void nativeRequest('cancel')}>{t('取消任务')}</Button>
        </div>
      )}
      <p className="list-caption">
        {t(
          '在独立的 GOG 官方页面完成授权；仅提取安装包中的游戏数据。密码不会保存；登录令牌由 Android Keystore 加密保存。',
        )}
      </p>
    </div>
  );
}
