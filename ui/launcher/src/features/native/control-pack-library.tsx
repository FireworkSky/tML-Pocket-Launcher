import { useEffect, useState } from 'react';
import { Button } from '../../components/ui';
import { useTranslation } from '../../i18n';
import { nativeRequest } from '../../native/bridge';
import { useLauncher } from '../../state/launcher';
import './control-pack-library.css';

interface Pack {
  id: string;
  name: string;
  createdAt: number;
  sizeBytes: number;
}

type Action = 'save' | 'import' | 'apply' | 'delete' | 'export' | 'share';
type PackList = { packs: Pack[] };

export function ControlPackLibrary() {
  const { nativeState } = useLauncher();
  return <Library key={nativeState?.activeInstance ?? 'default'} />;
}

function Library() {
  const { t, locale } = useTranslation();
  const { nativeState, notify } = useLauncher();
  const [packs, setPacks] = useState<Pack[]>([]);
  const [generation, setGeneration] = useState(0);
  const [loading, setLoading] = useState(true);
  const [running, setRunning] = useState(false);
  const [error, setError] = useState('');
  const [name, setName] = useState('');
  const [creating, setCreating] = useState(false);
  const [confirmation, setConfirmation] = useState<{ action: 'apply' | 'delete'; id: string }>();
  const busy = loading || running || nativeState?.busy;

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError('');
    nativeRequest<PackList>('controls.packs.list')
      .then((result) => {
        if (active) setPacks(result.packs);
      })
      .catch((failure: Error) => {
        if (active) setError(failure.message);
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [generation]);

  async function run(action: Action, pack?: Pack) {
    if (busy) return;
    setRunning(true);
    setError('');
    setConfirmation(undefined);
    try {
      const method = 'controls.packs.' + action;
      if (action === 'save' || action === 'import' || action === 'delete') {
        const result = await nativeRequest<PackList>(
          method,
          action === 'save' ? { name: name.trim() } : { id: pack?.id },
        );
        setPacks(result.packs);
        if (action === 'save') {
          setName('');
          setCreating(false);
        }
      } else if (action === 'apply') {
        await nativeRequest(method, { id: pack?.id });
        notify(t('布局已应用，下次启动游戏时生效。'));
      } else {
        const result = await nativeRequest<{ completed: boolean }>(method, { id: pack?.id, name: pack?.name });
        if (action === 'export' && result.completed) notify(t('布局包已导出'));
      }
    } catch (failure) {
      setError((failure as Error).message);
    } finally {
      setRunning(false);
    }
  }

  return (
    <section className="control-pack-library panel" aria-labelledby="screen-title">
      {!creating && <header>
        <Button disabled={busy} onClick={() => setCreating(true)}>
          {t('保存当前布局为包')}
        </Button>
        <Button variant="ghost" disabled={busy} onClick={() => void run('import')}>
          {t('导入布局包')}
        </Button>
      </header>}
      {creating && <form
        onSubmit={(event) => {
          event.preventDefault();
          if (name.trim()) void run('save');
        }}
      >
        <label>
          {t('布局包名称')}
          <input autoFocus enterKeyHint="done" value={name} maxLength={64} onChange={(event) => setName(event.target.value)} />
        </label>
        <Button type="submit" disabled={busy || !name.trim()}>
          {t('保存')}
        </Button>
        <Button type="button" disabled={running} onClick={() => setCreating(false)}>{t('取消')}</Button>
      </form>}
      {loading && <p role="status">{t('正在读取布局包…')}</p>}
      {!loading && !error && !packs.length && (
        <p>{t('尚无布局包。保存当前布局，或导入他人分享的 ZIP。')}</p>
      )}
      {error && (
        <div role="alert">
          {t(error)}{' '}
          <Button disabled={busy} onClick={() => setGeneration((n) => n + 1)}>
            {t('刷新')}
          </Button>
        </div>
      )}
      {packs.map((pack) => (
        <article key={pack.id}>
          <div>
            <h4>{pack.name}</h4>
            <small>
              {new Date(pack.createdAt).toLocaleDateString(locale)} ·{' '}
              {Math.max(1, Math.ceil(pack.sizeBytes / 1024))} KiB
            </small>
          </div>
          {confirmation?.id === pack.id ? (
            <div className="pack-actions">
              <span>
                {t(confirmation.action === 'delete' ? '删除这个布局包？' : '用此包替换当前布局？')}
              </span>
              <Button disabled={busy} onClick={() => void run(confirmation.action, pack)}>
                {t('确认')}
              </Button>
              <Button onClick={() => setConfirmation(undefined)}>{t('取消')}</Button>
            </div>
          ) : (
            <div className="pack-actions">
              <Button
                disabled={busy}
                onClick={() => setConfirmation({ action: 'apply', id: pack.id })}
              >
                {t('应用')}
              </Button>
              <Button disabled={busy} onClick={() => void run('export', pack)}>
                {t('导出')}
              </Button>
              <Button disabled={busy} onClick={() => void run('share', pack)}>
                {t('分享')}
              </Button>
              <Button
                disabled={busy}
                onClick={() => setConfirmation({ action: 'delete', id: pack.id })}
              >
                {t('删除')}
              </Button>
            </div>
          )}
        </article>
      ))}
    </section>
  );
}
