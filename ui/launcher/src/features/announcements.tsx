import { useEffect, useState } from 'react';
import { Button } from '../components/ui';
import { useTranslation, type LauncherLocale } from '../i18n';
import './announcements.css';

interface Announcement {
  date: string;
  category: string;
  title: string;
  body: string;
  links: { label: string; url: string }[];
}

const site = 'https://fireworksky.github.io/tML-Pocket-Launcher/';
const refreshInterval = 60_000;

function isFeed(value: unknown): value is Announcement[] {
  return (
    Array.isArray(value) &&
    value.every(
      (item) =>
        item &&
        typeof item.date === 'string' &&
        /^\d{4}-\d{2}-\d{2}$/.test(item.date) &&
        typeof item.title === 'string' &&
        typeof item.category === 'string' &&
        typeof item.body === 'string' &&
        Array.isArray(item.links) &&
        item.links.every(
          (link: Announcement['links'][number]) =>
            link &&
            typeof link.label === 'string' &&
            typeof link.url === 'string' &&
            /^https:\/\//i.test(link.url),
        ),
    )
  );
}

function readCache(key: string) {
  try {
    const cached: unknown = JSON.parse(localStorage.getItem(key) ?? 'null');
    if (isFeed(cached)) return cached;
  } catch {
    /* Storage may be unavailable. */
  }
  return null;
}

export function Announcements() {
  const { locale } = useTranslation();
  return <NewsPanel key={locale} locale={locale} />;
}

function NewsPanel({ locale }: { locale: LauncherLocale }) {
  const english = locale === 'en';
  const cacheKey = 'launcher.news.v1.' + locale;
  const [items, setItems] = useState(() => readCache(cacheKey));
  const [failed, setFailed] = useState(false);
  const [attempt, setAttempt] = useState(0);
  const retry = () => setAttempt((value) => value + 1);

  useEffect(() => {
    let disposed = false;
    let active: AbortController | undefined;
    async function sync() {
      if (document.hidden || active || disposed) return;
      active = new AbortController();
      const timeout = window.setTimeout(() => active?.abort(), 12000);
      try {
        // A minute-specific URL also refreshes the GitHub Pages CDN entry.
        const url = site + 'news-' + locale + '.json?v=' + Math.floor(Date.now() / refreshInterval);
        const response = await fetch(url, {
          signal: active.signal,
          cache: 'no-store',
          credentials: 'omit',
          redirect: 'error',
        });
        if (!response.ok) throw new Error('News unavailable');
        const data: unknown = await response.json();
        if (!isFeed(data)) throw new Error('Invalid news feed');
        if (disposed) return;
        setItems(data);
        setFailed(false);
        try {
          localStorage.setItem(cacheKey, JSON.stringify(data));
        } catch {
          /* Optional offline cache. */
        }
      } catch {
        if (!disposed) setFailed(true);
      } finally {
        window.clearTimeout(timeout);
        active = undefined;
      }
    }
    void sync();
    const timer = window.setInterval(sync, refreshInterval);
    document.addEventListener('visibilitychange', sync);
    window.addEventListener('online', sync);
    return () => {
      disposed = true;
      active?.abort();
      window.clearInterval(timer);
      document.removeEventListener('visibilitychange', sync);
      window.removeEventListener('online', sync);
    };
  }, [locale, cacheKey, attempt]);

  return (
    <details className="panel launcher-news">
      <summary
        onClick={(event) => {
          if (!event.currentTarget.parentElement?.hasAttribute('open')) retry();
        }}
      >
        <strong>{english ? 'Updates' : '更新公告'}</strong>
        {items?.[0] && <span>{items[0].title}</span>}
      </summary>
      <div className="launcher-news-body">
        <Button className="quiet-action" onClick={retry}>
          {english ? 'Refresh' : '刷新公告'}
        </Button>
        {failed && (
          <p role="status">
            {items
              ? english
                ? 'Offline · showing saved updates.'
                : '同步失败，正在显示上次保存的公告。'
              : english
                ? 'Could not load updates. Check your connection and retry.'
                : '无法获取公告，请检查网络后重试。'}
          </p>
        )}
        {!failed && !items && <p role="status">{english ? 'Loading updates…' : '正在获取公告…'}</p>}
        {items?.length === 0 && <p>{english ? 'No updates yet.' : '暂无更新公告。'}</p>}
        {items?.map((item, index) => (
          <article key={item.date + '-' + index}>
            <small>
              <time dateTime={item.date}>{item.date}</time> · {item.category}
            </small>
            <h3>{item.title}</h3>
            {item.body && <p>{item.body}</p>}
            {item.links.map((link) => (
              <a key={link.url} href={link.url}>
                {link.label}
              </a>
            ))}
          </article>
        ))}
      </div>
    </details>
  );
}
