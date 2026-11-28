import { useState } from 'react';
import { LabeledSelect } from '../../components/form-controls';
import { Badge, Button, EmptyState } from '../../components/ui';
import { useTranslation } from '../../i18n';
import { nativeRequest } from '../../native/bridge';
import { useLauncher } from '../../state/launcher';

export function NativeVersionManager({ compact = false }: { compact?: boolean }) {
  const { t, locale } = useTranslation();
  const { nativeState, notify } = useLauncher();
  const [pending, setPending] = useState<string | null>(null);
  const [removing, setRemoving] = useState<string | null>(null);
  const [failure, setFailure] = useState<Error | string>();
  const failureMessage = failure instanceof Error ? failure.message : failure && t(failure);
  const versions = nativeState?.installedVersions || [];
  const selected = versions.find((version) => version.active && version.available);
  const available = versions.filter((version) => version.available);
  const busy = !!pending || !!nativeState?.busy;
  const run = async (method: 'versions.select' | 'versions.uninstall', id: string) => {
    if (busy) return;
    setPending(id);
    setFailure(undefined);
    try {
      await nativeRequest(method, { id });
      setRemoving(null);
      notify(
        t(
          method === 'versions.select'
            ? '当前启动版本已切换。'
            : '所选版本已卸载，存档与模组已保留。',
        ),
        '配置',
      );
    } catch (error) {
      setFailure(error instanceof Error ? error : '操作失败，请刷新后重试。');
    } finally {
      setPending(null);
    }
  };
  if (!nativeState) return <p role="status">{t('正在读取已安装版本…')}</p>;
  if (!nativeState.installedVersions)
    return (
      <p role="status">
        {t('{version} · 请更新启动器以使用多版本管理。', {
          version: nativeState.tml || t('尚未安装 tModLoader'),
        })}
      </p>
    );
  return (
    <div className="native-versions" aria-label={t('tML 版本管理')} aria-busy={busy}>
      {versions.length ? (
        <>
          {available.length > 0 && (
            <LabeledSelect
              className="version-selector"
              label={t('切换 tModLoader 启动版本')}
              value={selected?.id ?? ''}
              options={[
                ...(!selected ? ([['', t('选择版本'), true]] as const) : []),
                ...available.map((version) => [version.id, version.version] as const),
              ]}
              disabled={busy || (!!selected && available.length === 1)}
              onChange={(id) => void run('versions.select', id)}
            />
          )}
          <p className="version-hint">
            {t('共 {count} 个版本 · 模组需与所选 tML 版本兼容。', {
              count: versions.length.toLocaleString(locale),
            })}
          </p>
          <div className="version-list">
            {versions.map((version) => (
              <div className="version-item" key={version.id} data-version-id={version.id}>
                <div className="version-summary">
                  <span className="resource-icon">
                    <img src="/tmodloader.png" alt="" width={40} height={40} />
                  </span>
                  <div className="resource-copy">
                    <h3>{version.version}</h3>
                    <p>
                      {t(version.available ? '发行文件已校验' : '文件缺失或校验失败，请重新安装')}
                    </p>
                  </div>
                  {version.active && (
                    <Badge tone={version.available ? 'green' : ''}>{t('当前版本')}</Badge>
                  )}
                </div>
                <div className="version-actions">
                  {(!compact || !version.active) && (
                    <Button
                      disabled={busy || version.active || !version.available}
                      onClick={() => void run('versions.select', version.id)}
                    >
                      {t(
                        pending === version.id && removing !== version.id
                          ? '正在切换…'
                          : version.active
                            ? '已选为启动版本'
                            : '设为启动版本',
                      )}
                    </Button>
                  )}
                  <Button
                    disabled={busy}
                    onClick={() => {
                      setRemoving(version.id);
                      setFailure(undefined);
                    }}
                  >
                    {t('卸载')}
                  </Button>
                </div>
                {removing === version.id && (
                  <div
                    className="version-confirm"
                    role="group"
                    aria-label={t('确认卸载 {version}', { version: version.version })}
                  >
                    <strong>{t('卸载 {version}？', { version: version.version })}</strong>
                    <p>
                      {t(
                        version.active
                          ? '仅删除此版本的发行文件，保留存档、模组和 Terraria 资源。卸载后自动选择其他可用版本；没有可用版本时需重新安装。'
                          : '仅删除此版本的发行文件，保留存档、模组和 Terraria 资源。当前启动版本不受影响。',
                      )}
                    </p>
                    <div className="version-actions">
                      <Button
                        disabled={busy}
                        onClick={() => {
                          setRemoving(null);
                          setFailure(undefined);
                        }}
                      >
                        {t('取消卸载')}
                      </Button>
                      <Button
                        disabled={busy}
                        onClick={() => void run('versions.uninstall', version.id)}
                      >
                        {t(pending === version.id ? '正在卸载…' : '确认卸载')}
                      </Button>
                    </div>
                  </div>
                )}
              </div>
            ))}
          </div>
        </>
      ) : (
        <EmptyState
          symbol="game"
          title={t('尚未安装 tModLoader')}
          description={t('下载官方版本或导入本地发行 ZIP。安装后可在此选择启动版本。')}
        />
      )}
      {failureMessage && (
        <pre className="native-log version-error" role="alert" tabIndex={0}>
          {failureMessage}
        </pre>
      )}
    </div>
  );
}
