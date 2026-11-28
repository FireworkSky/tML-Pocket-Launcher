import { useState } from 'react';
import { Button, Icon } from '../../../components/ui';
import { useTranslation } from '../../../i18n';
import { nativeRequest } from '../../../native/bridge';
import { useLauncher } from '../../../state/launcher';
import { SteamBrand } from './brand';
import { isSteamAuthenticating, translateSteamAccountMessage, useSteam } from './state';

interface SteamProbeReport {
  message: string;
  attempts: { route: string; ok: boolean; reason?: string }[];
}

export function SteamLoginPage() {
  const { t } = useTranslation();
  const { nativeState, openWorkspace } = useLauncher();
  const { account, connectionError, refresh } = useSteam();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [code, setCode] = useState('');
  const [pending, setPending] = useState(false);
  const [error, setError] = useState('');
  const [probe, setProbe] = useState<SteamProbeReport | null>(null);
  const phase = account?.phase || 'loading';
  const busy = isSteamAuthenticating(phase);
  const guard = phase === 'email' || phase === 'device';
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
  const testConnection = () => {
    setProbe(null);
    void run('store.steam.probe').then((result) => {
      if (result) setProbe(result as SteamProbeReport);
    });
  };
  const probeRoute = probe && /^Steam 连接可用 · (?<route>.+)$/.exec(probe.message);
  const probeText =
    probe &&
    [
      probeRoute?.groups ? t('Steam 连接可用 · {route}', probeRoute.groups) : t(probe.message),
      ...probe.attempts.map(({ route, ok, reason }) =>
        t('{route}：{result}', { route, result: ok ? t('已连接') : String(reason) }),
      ),
    ].join('\n');
  return (
    <section className="steam-page steam-login-page" aria-labelledby="steam-login-title">
      <div className="steam-intro">
        <SteamBrand />
        <div className="steam-intro-copy">
          <span className="steam-eyebrow">{t('你的游戏，随时继续')}</span>
          <h2 id="steam-login-title">{t('登录 Steam')}</h2>
          <p>
            {t('连接你的游戏库，')}
            <br />
            {t('把 Terraria 带到这里。')}
          </p>
        </div>
        <div className="steam-resource-note">
          <img src="/terraria.png" alt="" />
          <span>
            Terraria<small>{t('已购游戏资源')}</small>
          </span>
        </div>
      </div>
      <div className="steam-auth-card">
        <div className="steam-auth-heading">
          <span className="steam-eyebrow">
            {guard || phase === 'confirmation' ? 'STEAM GUARD' : t('账户登录')}
          </span>
          <span className="steam-security">
            <Icon name="lock" />
            {t('安全连接')}
          </span>
        </div>
        {(error || connectionError) && (
          <p className="steam-error" role="alert">
            {error || connectionError}
          </p>
        )}
        {phase === 'loading' ? (
          <div className="steam-wait" role="status">
            <span className="steam-spinner" />
            {t('正在读取 Steam 会话…')}
          </div>
        ) : phase === 'ready' ? (
          <div className="steam-wait">
            <Icon name="check" />
            {t('已登录')}
            <Button onClick={() => openWorkspace('steam-library')}>{t('进入资源库')}</Button>
          </div>
        ) : guard ? (
          <form
            className="steam-form"
            onSubmit={(event) => {
              event.preventDefault();
              const submitted = code;
              setCode('');
              void run('store.steam.guard', { code: submitted });
            }}
          >
            <h3>{phase === 'email' ? t('查看你的电子邮箱') : t('输入 Steam Guard 动态码')}</h3>
            <p className="steam-helper" role="status">
              {translateSteamAccountMessage(account, t)}
            </p>
            <label className="steam-field">
              <span>{t('Steam Guard 验证码')}</span>
              <input
                autoComplete="one-time-code"
                autoCapitalize="characters"
                spellCheck={false}
                value={code}
                maxLength={10}
                onChange={(event) => setCode(event.target.value)}
                required
              />
            </label>
            <Button className="steam-primary" type="submit" disabled={pending || !code.trim()}>
              {t('提交验证码')}
            </Button>
          </form>
        ) : busy ? (
          <div className="steam-wait" role="status">
            <span className="steam-spinner" />
            <h3>
              {phase === 'confirmation'
                ? t('在 Steam 手机应用中确认')
                : phase === 'connecting' || phase === 'reconnecting'
                  ? t('正在连接 Steam')
                  : t('正在验证账户')}
            </h3>
            <p>{translateSteamAccountMessage(account, t)}</p>
            {phase === 'confirmation' && <small>{t('批准本次登录后，此页面会自动继续。')}</small>}
          </div>
        ) : account?.canReconnect ? (
          <div className="steam-form">
            <h3>
              {account.account ? t('欢迎回来，{name}', { name: account.account }) : t('欢迎回来')}
            </h3>
            <p className="steam-helper" role="status">
              {translateSteamAccountMessage(account, t)}
            </p>
            <Button
              className="steam-primary"
              disabled={pending || nativeState?.busy}
              onClick={() => void run('store.steam.reconnect')}
            >
              {t('恢复 Steam 连接')}
            </Button>
          </div>
        ) : (
          <form
            className="steam-form"
            onSubmit={(event) => {
              event.preventDefault();
              const submitted = password;
              setPassword('');
              void run('store.steam.login', { username, password: submitted });
            }}
          >
            <label className="steam-field">
              <span>{t('Steam 账户名')}</span>
              <input
                autoComplete="username"
                autoCapitalize="none"
                spellCheck={false}
                value={username}
                maxLength={128}
                onChange={(event) => setUsername(event.target.value)}
                required
              />
            </label>
            <label className="steam-field">
              <span>{t('Steam 密码')}</span>
              <input
                type="password"
                autoComplete="current-password"
                value={password}
                maxLength={256}
                onChange={(event) => setPassword(event.target.value)}
                required
              />
            </label>
            {phase === 'error' && (
              <p className="steam-error" role="alert">
                {account?.message}
              </p>
            )}
            <Button
              className="steam-primary"
              type="submit"
              disabled={pending || !username.trim() || !password || nativeState?.busy}
            >
              {t('登录 Steam')}
            </Button>
          </form>
        )}
        <div className="steam-auth-footer">
          <p>
            <Icon name="lock" />
            {t('密码不会保存，会话令牌在设备上加密保存。')}
          </p>
          {busy || account?.canReconnect ? (
            <Button
              className="steam-text-button"
              disabled={pending || nativeState?.busy}
              onClick={() => void run('store.steam.logout')}
            >
              {busy ? t('取消登录') : t('使用其他账户')}
            </Button>
          ) : (
            phase !== 'loading' && (
              <Button
                className="steam-text-button"
                disabled={pending || nativeState?.busy}
                onClick={testConnection}
              >
                {pending ? t('正在检测连接…') : t('测试 Steam 连接')}
              </Button>
            )
          )}
        </div>
        {(account?.diagnostics || probe) && (
          <details className="steam-diagnostics">
            <summary>{t('Steam 连接诊断')}</summary>
            <pre role={probeText ? 'status' : undefined}>{probeText || account?.diagnostics}</pre>
          </details>
        )}
      </div>
    </section>
  );
}
