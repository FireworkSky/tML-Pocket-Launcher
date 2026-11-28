import { useEffect, useState } from 'react';
import { Button, SettingRow, Switch } from '../../components/ui';
import { useTranslation } from '../../i18n';
import { nativeRequest } from '../../native/bridge';
import { useLauncher } from '../../state/launcher';
import { NativeField } from './controls';
import { useNativeFileBrowser } from './files';

export function NativeInstances() {
  const { t } = useTranslation();
  const { nativeState } = useLauncher();
  const [name, setName] = useState('');
  const [renaming, setRenaming] = useState<string>();
  const [error, setError] = useState('');
  const [pending, setPending] = useState(false);
  const busy = pending || nativeState?.busy;
  const run = async (method: string, params: object) => {
    setPending(true);
    setError('');
    try {
      await nativeRequest(method, params);
      setName('');
      setRenaming(undefined);
    } catch (failure) {
      setError((failure as Error).message);
    } finally {
      setPending(false);
    }
  };
  return (
    <div className="native-stack">
      {nativeState?.instances?.map((instance) => (
        <div key={instance.id} className="resource-row">
          <div className="resource-copy">
            <h3>
              {instance.id === 'default' && instance.name === '默认实例'
                ? t(instance.name)
                : instance.name}
            </h3>
            <p>
              {[
                nativeState.installedVersions?.find((version) => version.id === instance.versionId)
                  ?.version ?? (instance.active ? nativeState.tml : ''),
                instance.archived ? t('已归档') : instance.active ? t('当前实例') : '',
              ]
                .filter(Boolean)
                .join(' · ')}
            </p>
          </div>
          {!instance.active && !instance.archived && (
            <Button
              disabled={busy}
              onClick={() => void run('instances.select', { id: instance.id })}
            >
              {t('切换实例')}
            </Button>
          )}
          <Button
            disabled={busy}
            onClick={() => {
              setRenaming(instance.id);
              setName(instance.name);
            }}
          >
            {t('重命名')}
          </Button>
          {instance.id !== 'default' && (
            <Button
              disabled={busy}
              onClick={() =>
                void run('instances.archive', { id: instance.id, archived: !instance.archived })
              }
            >
              {t(instance.archived ? '取消归档' : '归档实例')}
            </Button>
          )}
        </div>
      ))}
      <form
        onSubmit={(event) => {
          event.preventDefault();
          if (busy || !name.trim()) return;
          void run(renaming ? 'instances.rename' : 'instances.create', { name, id: renaming });
        }}
      >
        <NativeField label="实例名称" value={name} onChange={setName} />
        <Button type="submit" disabled={busy || !name.trim()}>
          {t(renaming ? '保存名称' : '创建实例')}
        </Button>
        {renaming && (
          <Button
            type="button"
            onClick={() => {
              setRenaming(undefined);
              setName('');
            }}
          >
            {t('取消')}
          </Button>
        )}
      </form>
      {error && <p role="alert">{t(error)}</p>}
    </div>
  );
}

interface Backup {
  id: string;
  name: string;
  created: number;
  size: number;
}

export function NativeSaveBackups() {
  const { t, locale } = useTranslation();
  const { nativeState, notify } = useLauncher();
  const { browse } = useNativeFileBrowser();
  const [entries, setEntries] = useState<Backup[]>();
  const [pending, setPending] = useState(false);
  const [restore, setRestore] = useState<string>();
  const [removing, setRemoving] = useState<string>();
  const [error, setError] = useState('');
  const busy = pending || nativeState?.busy;
  const run = async (method: string, params = {}) => {
    setPending(true);
    setError('');
    try {
      if (method !== 'backups.list') {
        await nativeRequest(method, params);
        setRestore(undefined);
        setRemoving(undefined);
        if (method === 'backups.restore') notify(t('存档已恢复。'));
      }
      setEntries(await nativeRequest<Backup[]>('backups.list'));
    } catch (failure) {
      setError((failure as Error).message);
    } finally {
      setPending(false);
    }
  };
  useEffect(() => {
    let active = true;
    void nativeRequest<Backup[]>('backups.list').then(
      (value) => {
        if (active) setEntries(value);
      },
      (failure: Error) => {
        if (active) setError(failure.message);
      },
    );
    return () => {
      active = false;
    };
  }, []);
  return (
    <div className="native-stack">
      <SettingRow title={t('启动前自动备份')}>
        <Switch
          label={t('启动前自动备份')}
          checked={nativeState?.preferences.autoBackup === true}
          onChange={(autoBackup) =>
            void nativeRequest('settings.save', { preferences: { autoBackup } }).catch(
              (failure: Error) => setError(failure.message),
            )
          }
        />
      </SettingRow>
      <div className="native-toolbar">
        <Button disabled={busy} onClick={() => void run('backups.create')}>
          {t('备份角色和世界')}
        </Button>
        <Button
          disabled={busy}
          onClick={() =>
            browse({
              kind: 'backup-import',
              choose: ([file]) => nativeRequest('backups.import', file),
              complete: () => notify(t('存档备份已导入。')),
            })
          }
        >
          {t('导入备份')}
        </Button>
      </div>
      {entries?.length === 0 && <p>{t('还没有存档备份')}</p>}
      {entries?.map((entry) => (
        <div className="resource-row" key={entry.id}>
          <div className="resource-copy">
            <h3>{new Date(entry.created).toLocaleString(locale)}</h3>
            <p>{(entry.size / 1048576).toLocaleString(locale, { maximumFractionDigits: 2 })} MB</p>
            {restore === entry.id && <p>{t('恢复会替换当前角色和世界，并先备份当前存档。')}</p>}
            {removing === entry.id && <p>{t('确定删除这份备份？')}</p>}
          </div>
          {restore === entry.id ? (
            <>
              <Button disabled={busy} onClick={() => void run('backups.restore', { id: entry.id })}>
                {t('确认恢复')}
              </Button>
              <Button disabled={busy} onClick={() => setRestore(undefined)}>
                {t('取消')}
              </Button>
            </>
          ) : removing === entry.id ? (
            <>
              <Button disabled={busy} onClick={() => void run('backups.delete', { id: entry.id })}>
                {t('删除备份')}
              </Button>
              <Button disabled={busy} onClick={() => setRemoving(undefined)}>
                {t('取消')}
              </Button>
            </>
          ) : (
            <>
              <Button
                disabled={busy}
                onClick={() => {
                  setRemoving(undefined);
                  setRestore(entry.id);
                }}
              >
                {t('恢复存档')}
              </Button>
              <Button
                disabled={busy}
                onClick={() =>
                  browse({
                    kind: 'backup-export',
                    name: `${entry.name}.zip`,
                    choose: (_, destination) =>
                      nativeRequest('backups.export', { ...destination, id: entry.id }),
                    complete: () => notify(t('存档备份已导出。')),
                  })
                }
              >
                {t('导出')}
              </Button>
              <Button
                disabled={busy}
                onClick={() => {
                  setRestore(undefined);
                  setRemoving(entry.id);
                }}
              >
                {t('删除备份')}
              </Button>
            </>
          )}
        </div>
      ))}
      {pending && <p role="status">{t('正在处理…')}</p>}
      {error && <p role="alert">{t(error)}</p>}
    </div>
  );
}
