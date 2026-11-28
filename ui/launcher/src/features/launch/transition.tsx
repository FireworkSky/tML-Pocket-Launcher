import { LaunchEmblem } from './emblem';
import { useTranslation } from '../../i18n';
import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { Dialog } from 'radix-ui';
import { Button, Icon } from '../../components/ui';
import { nativeRequest, onNativeSnapshot } from '../../native/bridge';
import { useLauncher } from '../../state/launcher';

type Phase = 'checking' | 'handoff' | 'failed';
interface LaunchState {
  phase: Phase;
  stage: number;
  message: string;
  source?: 'steam' | 'gog' | 'android' | null;
  error?: string;
  missingTerrariaPe?: boolean;
  origin: DOMRect;
}
const LaunchContext = createContext<{
  start(trigger: HTMLElement): void;
  active: boolean;
} | null>(null);
const stageIndex = { resources: 0, ownership: 0, mods: 1, handoff: 2 };
const cancelLaunch = () => void nativeRequest('cancel', { operation: 'launch.check' }).catch(() => {});

export function LaunchTransitionProvider({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const launcher = useLauncher();
  const [launch, setLaunch] = useState<LaunchState | null>(null);
  const [logs, setLogs] = useState<string | null>(null);
  const locked = useRef(false);
  const phase = useRef<Phase | null>(null);
  const trigger = useRef<HTMLElement | null>(null);
  const surface = useRef<HTMLDivElement | null>(null);
  const logButton = useRef<HTMLButtonElement | null>(null);
  const history = useRef<string[]>([]);
  const entrance = useRef<Animation | null>(null);
  phase.current = launch?.phase || null;

  const close = () => {
    entrance.current?.cancel();
    locked.current = false;
    setLaunch(null);
    setLogs(null);
  };
  const start = (element: HTMLElement) => {
    if (locked.current || (launcher.nativeState?.launchBlocked ?? launcher.nativeState?.busy)) return;
    locked.current = true;
    trigger.current = element;
    history.current = [];
    setLogs(null);
    const origin = element.getBoundingClientRect();
    launcher.closeDialog(false);
    setLaunch({
      phase: 'checking',
      stage: 0,
      message: '正在检查游戏资源…',
      origin,
      source: launcher.nativeState?.terrariaSource,
    });
    void nativeRequest<{ missingTerrariaPe?: boolean }>('launch')
      .then((result) => {
        if (result.missingTerrariaPe) {
          setLaunch((current) =>
            current && {
              ...current,
              phase: 'failed',
              error: '本机未检测到 Google Play 版 Terraria PE。',
              missingTerrariaPe: true,
            },
          );
          return;
        }
        setLaunch((current) =>
          current?.phase === 'checking'
            ? { ...current, phase: 'handoff', stage: 2, message: '正在进入游戏…' }
            : current,
        );
      })
      .catch((error: Error) => {
        if (error.message.includes('超时')) cancelLaunch();
        setLaunch((current) =>
          current
            ? { ...current, phase: 'failed', message: '启动检查未通过', error: error.message }
            : current,
        );
        history.current.push(error.message);
      });
  };
  useEffect(
    () =>
      onNativeSnapshot((snapshot) => {
        if (!locked.current || !snapshot.launchStage || !snapshot.busy) return;
        const message = snapshot.launchMessage || snapshot.message;
        if (history.current.at(-1) !== message)
          history.current = [...history.current, message].slice(-50);
        const stage = snapshot.launchStage ? stageIndex[snapshot.launchStage] : undefined;
        setLaunch((current) =>
          current?.phase === 'checking'
            ? {
                ...current,
                stage: stage ?? current.stage,
                message,
              }
            : current,
        );
      }),
    [],
  );
  useEffect(() => {
    const progress = (event: Event) => {
      if (!locked.current) return;
      const detail: string = (event as CustomEvent<string>).detail;
      if (history.current.at(-1) !== detail) {
        history.current = [...history.current, detail].slice(-50);
        setLogs((current) => current === null ? null : current + '\n' + detail);
      }
      setLaunch((current) => current && current.phase !== 'failed'
        ? { ...current, phase: 'handoff', stage: 3, message: detail }
        : current);
    };
    const returned = () => {
      close();
      void nativeRequest('state').catch(() => {});
    };
    const back = (event: Event) => {
      if (!locked.current) return;
      event.stopImmediatePropagation();
      if (phase.current === 'failed') close();
      else if (phase.current === 'checking') cancelLaunch();
    };
    window.addEventListener('launcher:game-startup', progress);
    window.addEventListener('launcher:game-returned', returned);
    window.addEventListener('launcher:back', back, true);
    return () => {
      window.removeEventListener('launcher:game-startup', progress);
      window.removeEventListener('launcher:game-returned', returned);
      window.removeEventListener('launcher:back', back, true);
    };
  }, []);
  const enter = () => {
    if (!launch || !surface.current) return;
    const root = surface.current;
    const origin = launch.origin;
    entrance.current?.cancel();
    entrance.current = launcher.motionEnabled
      ? root.animate(
          [
            {
              clipPath: `inset(${Math.max(0, origin.top)}px ${Math.max(0, innerWidth - origin.right)}px ${Math.max(0, innerHeight - origin.bottom)}px ${Math.max(0, origin.left)}px round 24px)`,
              opacity: 0.85,
            },
            { clipPath: 'inset(0px round 0px)', opacity: 1 },
          ],
          { duration: 450, easing: 'cubic-bezier(0.77, 0, 0.175, 1)' },
        )
      : root.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 120 });
  };
  const readLogs = async () => {
    setLogs(history.current.join('\n') || t('正在读取启动日志…'));
    try {
      const result = await nativeRequest<{ text: string }>('logs');
      if (locked.current) setLogs([...history.current, '', result.text].join('\n'));
    } catch (error) {
      if (locked.current) setLogs([...history.current, (error as Error).message].join('\n'));
    }
  };
  return (
    <LaunchContext.Provider value={{ start, active: !!launch }}>
      {children}
      <Dialog.Root
        open={!!launch}
        onOpenChange={(open) => {
          if (!open && launch?.phase === 'failed') close();
        }}
      >
        <Dialog.Portal>
          {launch && (
            <Dialog.Content
              ref={surface}
              className="game-launch-screen"
              data-phase={launch.phase}
              data-motion={launcher.motionEnabled ? 'full' : 'reduced'}
              aria-describedby="game-launch-description"
              onOpenAutoFocus={(event) => {
                event.preventDefault();
                surface.current?.focus();
                enter();
              }}
              onCloseAutoFocus={(event) => {
                event.preventDefault();
                trigger.current?.isConnected && trigger.current.focus({ preventScroll: true });
              }}
              onPointerDownOutside={(event) => event.preventDefault()}
              onEscapeKeyDown={(event) => {
                event.preventDefault();
                if (logs !== null) {
                  setLogs(null);
                  logButton.current?.focus();
                } else if (launch.phase === 'failed') close();
                else if (launch.phase === 'checking') cancelLaunch();
              }}
            >
              <header className="game-launch-top">
                <span>
                  tModLoader <small>Mobile</small>
                </span>
                <span className="game-launch-version">{launcher.nativeState?.tml}</span>
              </header>
              <div className="game-launch-center">
                <LaunchEmblem />
                <Dialog.Title>
                  {launch.phase === 'failed' ? t('暂时无法启动') : t('准备出发')}
                </Dialog.Title>
                <Dialog.Description id="game-launch-description">
                  {launch.phase === 'failed'
                    ? t('检查问题后，可以再次尝试。')
                    : t('你的世界，即将继续。')}
                </Dialog.Description>
              </div>
              <div
                className="game-launch-status"
                role="status"
                aria-live="polite"
                aria-atomic="true"
              >
                {launch.phase !== 'failed' && (
                  <div className="game-launch-steps" aria-hidden="true">
                    {[0, 1, 2, 3].map((index) => (
                      <i
                        key={index}
                        className={
                          index < launch.stage
                            ? 'complete'
                            : index === launch.stage
                              ? 'current'
                              : ''
                        }
                      />
                    ))}
                  </div>
                )}
                <span>{t(launch.error || launch.message)}</span>
                <small>
                  {launch.phase === 'failed'
                    ? t('可查看日志了解原因')
                    : launch.phase === 'handoff'
                      ? t('首次启动可能需要更长时间')
                      : t('保留你的模组、角色与世界')}
                </small>
              </div>
              {launch.phase === 'failed' && (
                <div className="game-launch-actions">
                  {launch.missingTerrariaPe ? (
                    <Button
                      variant="primary"
                      onClick={() =>
                        void nativeRequest('terraria.mobile.openStore').catch(() =>
                          launcher.notify(t('无法打开 Google Play。')),
                        )
                      }
                    >
                      {t('前往 Google Play')}
                    </Button>
                  ) : launch.error?.includes('Steam') ? (
                    <Button
                      variant="primary"
                      onClick={() => {
                        close();
                        launcher.openWorkspace('steam-login');
                      }}
                    >
                      {t('前往 Steam')}
                    </Button>
                  ) : (
                    <Button
                      variant="primary"
                      onClick={() => {
                        locked.current = false;
                        start(trigger.current?.isConnected ? trigger.current : surface.current!);
                      }}
                    >
                      {t('重新尝试')}
                    </Button>
                  )}
                  <Button onClick={close}>{t('返回启动器')}</Button>
                </div>
              )}
              <footer className="game-launch-bottom">
                <span>
                  <i />
                  {launch.source === 'android'
                    ? t('Android 版资源')
                    : launch.source === 'steam'
                      ? t('Steam 资源')
                      : t('本地启动')}
                </span>
                <button ref={logButton} onClick={() => void readLogs()}>
                  <Icon name="file" />
                  {t('查看日志')}
                </button>
              </footer>
              {logs !== null && (
                <aside className="game-launch-log" aria-label="启动日志">
                  <div>
                    <strong>启动日志</strong>
                    <button
                      aria-label={t('关闭日志')}
                      onClick={() => {
                        setLogs(null);
                        logButton.current?.focus();
                      }}
                    >
                      <Icon name="close" />
                    </button>
                  </div>
                  <pre tabIndex={0}>{logs}</pre>
                </aside>
              )}
            </Dialog.Content>
          )}
        </Dialog.Portal>
      </Dialog.Root>
    </LaunchContext.Provider>
  );
}

export function useLaunchTransition() {
  const value = useContext(LaunchContext);
  if (!value) throw new Error('LaunchTransitionProvider is required');
  return value;
}
