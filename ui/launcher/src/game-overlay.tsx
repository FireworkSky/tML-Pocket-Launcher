import { useEffect, useRef, type CSSProperties } from 'react';
import { createRoot } from 'react-dom/client';
import { Dialog } from 'radix-ui';
import { Button } from './components/ui';
import { LaunchEmblem } from './features/launch/emblem';
import './styles/game-overlay.css';

interface Labels {
  local: string;
  logs: string;
  viewLogs: string;
  closeLogs: string;
  returnLabel: string;
}
interface Failure {
  version: string;
  title: string;
  detail: string;
  note: string;
}
interface Prompt {
  id: number;
  title: string;
  message?: string;
  input?: 'text' | 'password';
  buttons: string[];
  colors?: string[];
  enter: number;
  escape: number;
}
interface Notice {
  text: string;
  vertical: number;
  horizontal: number;
  x: number;
  y: number;
}
interface State {
  theme: string;
  skin: string;
  language: string;
  motion: boolean;
  keyboardInset: number;
  labels: Labels;
  failure?: Failure;
  logs?: string;
  prompt?: Prompt;
  notice?: Notice;
}
declare global {
  interface Window {
    GameOverlayNative?: {
      postMessage(message: string): void;
      onmessage: ((event: MessageEvent<string>) => void) | null;
    };
  }
}

function send(action: string, values = {}) {
  window.GameOverlayNative?.postMessage(JSON.stringify({ action, ...values }));
}

function StartupFailure({
  failure,
  labels,
  logs,
  motion,
}: {
  failure: Failure;
  labels: Labels;
  logs?: string;
  motion: boolean;
}) {
  const logButton = useRef<HTMLButtonElement>(null);
  const closeButton = useRef<HTMLButtonElement>(null);
  const showingLogs = logs !== undefined;
  useEffect(() => {
    if (showingLogs) {
      closeButton.current?.focus();
      return () => logButton.current?.focus();
    }
  }, [showingLogs]);
  return (
    <main
      className="game-launch-screen"
      data-phase="failed"
      data-motion={motion ? 'full' : 'reduced'}
      onKeyDown={(event) => {
        if (event.key !== 'Escape') return;
        if (showingLogs) send('logs', { open: false });
        else send('return');
      }}
    >
      <header className="game-launch-top">
        <span>
          tModLoader <small>Mobile</small>
        </span>
        <span className="game-launch-version">{failure.version}</span>
      </header>
      <div className="game-launch-center">
        <LaunchEmblem />
        <h2>{failure.title}</h2>
      </div>
      <div className="game-launch-status" role="status" aria-live="polite">
        <span>{failure.detail}</span>
        <small>{failure.note}</small>
      </div>
      <div className="game-launch-actions">
        <Button variant="primary" onClick={() => send('return')}>
          {labels.returnLabel}
        </Button>
      </div>
      <footer className="game-launch-bottom">
        <span>{labels.local}</span>
        <button ref={logButton} onClick={() => send('logs', { open: true })}>
          {labels.viewLogs}
        </button>
      </footer>
      {showingLogs && (
        <aside className="game-launch-log" aria-label={labels.logs}>
          <div>
            <strong>{labels.logs}</strong>
            <button ref={closeButton} onClick={() => send('logs', { open: false })}>
              {labels.closeLogs}
            </button>
          </div>
          <pre tabIndex={0}>{logs}</pre>
        </aside>
      )}
    </main>
  );
}

function GamePrompt({ prompt, keyboardInset }: { prompt: Prompt; keyboardInset: number }) {
  const input = useRef<HTMLInputElement>(null);
  const submitted = useRef(false);
  function pick(index: number) {
    if (index < 0 || submitted.current) return;
    submitted.current = true;
    send('pick', { id: prompt.id, index, text: input.current?.value });
  }
  const colors = prompt.colors;
  return (
    <Dialog.Root open>
      <Dialog.Portal>
        <Dialog.Overlay className="game-dialog-shade" />
        <div className="game-dialog-position" style={{ bottom: keyboardInset }}>
          <Dialog.Content
            className="dialog"
            style={{ backgroundColor: colors?.[0], color: colors?.[1] }}
            aria-describedby={prompt.message ? 'game-message' : undefined}
            onOpenAutoFocus={(event) => {
              if (input.current) {
                event.preventDefault();
                input.current.focus();
              }
            }}
            onPointerDownOutside={(event) => {
              event.preventDefault();
              if (prompt.input) pick(prompt.escape);
            }}
            onEscapeKeyDown={(event) => {
              event.preventDefault();
              pick(prompt.escape);
            }}
            onKeyDown={(event) => {
              if (
                event.key === 'Enter' &&
                !event.nativeEvent.isComposing &&
                prompt.enter >= 0 &&
                (event.target instanceof HTMLInputElement || !prompt.input)
              ) {
                event.preventDefault();
                pick(prompt.enter);
              }
            }}
          >
            <div className="dialog-header">
              <Dialog.Title style={{ color: colors?.[1] }}>{prompt.title}</Dialog.Title>
            </div>
            {prompt.message && (
              <Dialog.Description
                id="game-message"
                className="game-message"
                style={{ color: colors?.[1] }}
              >
                {prompt.message}
              </Dialog.Description>
            )}
            {prompt.input && (
              <input
                className="game-prompt-input"
                aria-label={prompt.title}
                ref={input}
                type={prompt.input}
                autoComplete="off"
              />
            )}
            <div className="dialog-actions">
              {prompt.buttons.map((text, index) => (
                <Button
                  key={index}
                  variant={index === prompt.enter ? 'primary' : ''}
                  style={
                    {
                      backgroundColor: colors?.[3],
                      color: colors?.[1],
                      borderColor: colors?.[2],
                      '--game-selected': colors?.[4],
                    } as CSSProperties
                  }
                  onClick={() => pick(index)}
                >
                  {text}
                </Button>
              ))}
            </div>
          </Dialog.Content>
        </div>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

function GameNotice({ notice }: { notice: Notice }) {
  const horizontal = notice.horizontal === 3 ? 'start' : notice.horizontal === 5 ? 'end' : 'center';
  const vertical = notice.vertical === 48 ? 'start' : notice.vertical === 80 ? 'end' : 'center';
  return (
    <div
      className="game-notice-position"
      style={{ alignItems: vertical, justifyContent: horizontal }}
    >
      <div
        role="status"
        className="toast show"
        style={{
          translate:
            (horizontal === 'end' ? -notice.x : notice.x) +
            'px ' +
            (vertical === 'end' ? -notice.y : notice.y) +
            'px',
        }}
      >
        {notice.text}
      </div>
    </div>
  );
}

const root = createRoot(document.getElementById('root')!);
const bridge = window.GameOverlayNative;
if (bridge) {
  bridge.onmessage = (event) => {
    const state: State = JSON.parse(event.data);
    document.documentElement.dataset.theme = state.theme;
    document.documentElement.dataset.skin = state.skin;
    document.documentElement.dataset.motion = state.motion ? 'full' : 'reduced';
    document.documentElement.lang = state.language;
    root.render(
      <>
        {state.failure && (
          <StartupFailure
            failure={state.failure}
            labels={state.labels}
            logs={state.logs}
            motion={state.motion}
          />
        )}
        {state.prompt && (
          <GamePrompt
            key={state.prompt.id}
            prompt={state.prompt}
            keyboardInset={state.keyboardInset}
          />
        )}
        {state.notice && <GameNotice notice={state.notice} />}
      </>,
    );
  };
  send('ready');
}
