import { useEffect, useState } from 'react';
import { Button } from '../../components/ui';
import { useTranslation } from '../../i18n';
import { nativeRequest } from '../../native/bridge';
import { useLauncher } from '../../state/launcher';
import './crash-report.css';

interface CrashReport {
  session: string;
  text: string;
}

export function CrashReportHost() {
  const { nativeMode, dialog, openDialog, closeDialog, notify } = useLauncher();
  const { t } = useTranslation();
  const [pending, setPending] = useState<CrashReport | null>(null);

  useEffect(() => {
    if (!nativeMode) return;
    const receive = (event: Event) => {
      const report = (event as CustomEvent<CrashReport>).detail;
      if (typeof report?.session === 'string' && typeof report.text === 'string')
        setPending(report);
    };
    window.addEventListener('launcher:crash-report', receive);
    void nativeRequest('crashReport.ready').catch(console.error);
    return () => window.removeEventListener('launcher:crash-report', receive);
  }, [nativeMode]);

  useEffect(() => {
    if (!pending || dialog?.open) return;
    const { session, text } = pending;
    setPending(null);
    openDialog({
      title: t('游戏异常退出'),
      description: t('游戏崩溃报告'),
      body: (
        <pre className="crash-report" tabIndex={0} aria-label={t('游戏崩溃报告')}>
          {text}
        </pre>
      ),
      actions: (
        <>
          <Button
            onClick={async () => {
              try {
                await nativeRequest('crashReport.copy', { session });
                closeDialog();
              } catch {
                notify(t('无法复制报告'));
              }
            }}
          >
            {t('复制报告')}
          </Button>
          <Button onClick={() => void nativeRequest('crashReport.share', { session })
            .catch(() => notify(t('无法分享诊断包')))}>
            {t('分享诊断包')}
          </Button>
          <Button variant="primary" onClick={() => closeDialog()}>
            {t('关闭')}
          </Button>
        </>
      ),
      onDismiss: () => {
        void nativeRequest('crashReport.dismiss', { session }).catch(console.error);
      },
    });
  }, [pending, dialog?.open, openDialog, closeDialog, notify, t]);
  return null;
}
