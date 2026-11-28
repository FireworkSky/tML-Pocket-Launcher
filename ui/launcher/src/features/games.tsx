import { useTranslation } from '../i18n';
import { Badge, Button, ContextNote, Icon, WorkspaceTabs } from '../components/ui';
import { useGameActions } from './game-actions';
import { SteamBrand } from './native/steam/brand';
import { profileTitle } from '../state/launcher';

export function AccountRows({ detailed = false }: { detailed?: boolean }) {
  const { t } = useTranslation();
  const { accounts } = useGameActions();
  return (
    <>
      {(['steam', 'gog'] as const).map((platform) => (
        <div className="account-row" key={platform}>
          {platform === 'steam' ? <SteamBrand /> : <span className="brand-word gog">gog.com</span>}
          <div className="account-copy">
            <strong>
              {detailed
                ? t('尚未连接 {platform} 账户', {
                    platform: platform === 'steam' ? 'Steam' : 'GOG',
                  })
                : t('尚未连接')}
            </strong>
            <small>
              {detailed
                ? t('登录后验证已购 Terraria，并选择资源下载版本')
                : t('验证购买后下载资源')}
            </small>
          </div>
          <Button data-action={`account-${platform}`} onClick={() => accounts(platform)}>
            {t('连接账户')} <Icon name="chevron" />
          </Button>
        </div>
      ))}
    </>
  );
}
function Sources() {
  const { t } = useTranslation();
  const { accounts, importInfo, versions } = useGameActions();
  return (
    <>
      <div className="source-options">
        {(
          [
            {
              id: 'steam',
              title: '从 Steam 获取',
              detail: '登录、验证游戏所有权，下载 Terraria 资源。',
            },
            {
              id: 'gog',
              title: '从 GOG 获取',
              detail: '授权后选择已购游戏，下载并提取安装资源。',
            },
            {
              id: 'local',
              title: '导入本地资源',
              detail: '选择 Terraria 资源 ZIP，自动识别并校验。',
            },
          ] as const
        ).map((source) => (
          <Button
            key={source.id}
            className="source-card"
            data-action={source.id === 'local' ? 'import-terraria' : `account-${source.id}`}
            onClick={() => (source.id === 'local' ? importInfo('terraria') : accounts(source.id))}
          >
            {source.id === 'local' ? (
              <Icon name="folder" />
            ) : source.id === 'steam' ? (
              <SteamBrand />
            ) : (
              <span className="brand-word gog">gog.com</span>
            )}
            <h2>{t(source.title)}</h2>
            <p>{t(source.detail)}</p>
            <small>
              {source.id === 'local' ? t('选择文件') : t('连接账户')} <Icon name="arrow" />
            </small>
          </Button>
        ))}
      </div>
      <div className="section-title">
        <h2>{t('tModLoader 发行包')}</h2>
      </div>
      <section className="panel">
        <div className="resource-row">
          <span className="resource-icon">
            <img src="/tmodloader.png" alt="" />
          </span>
          <div className="resource-copy">
            <h3>{t('官方版本与本地导入')}</h3>
            <p>{t('浏览稳定版与预览版，或导入已下载的发行 ZIP。')}</p>
          </div>
          <div className="resource-actions">
            <Button variant="soft" data-action="versions" onClick={versions}>
              {t('选择版本')}
            </Button>
            <Button onClick={() => importInfo('tml')}>{t('导入 ZIP')}</Button>
          </div>
        </div>
      </section>
    </>
  );
}
function Installed() {
  const { t } = useTranslation();
  const { state, getTerraria, instances, versions, importInfo } = useGameActions();
  return (
    <>
      <div className="two-column">
        <section className="panel">
          <div className="panel-header">
            <h2>{t('游戏资源')}</h2>
            <Badge tone={state.demo ? 'green' : ''}>
              {state.demo ? t('已就绪') : t('等待准备')}
            </Badge>
          </div>
          {['terraria', 'tml'].map((kind) => (
            <div className="resource-row" key={kind}>
              <span className="resource-icon">
                <img src={kind === 'tml' ? '/tmodloader.png' : '/terraria.png'} alt="" />
              </span>
              <div className="resource-copy">
                <h3>{kind === 'tml' ? 'tModLoader' : 'Terraria'}</h3>
                <p>
                  {state.demo
                    ? kind === 'tml'
                      ? t('{profile} · GitHub Releases', {
                          profile: t(profileTitle(state.profile)),
                        })
                      : t('示例资源包 · 本地导入')
                    : kind === 'tml'
                      ? t('选择适合模组的官方版本')
                      : t('运行游戏所需的原版资源')}
                </p>
                <Badge tone={state.demo ? 'green' : ''}>
                  {state.demo ? (kind === 'tml' ? t('已安装') : t('已校验')) : t('尚未安装')}
                </Badge>
              </div>
              <div className="resource-actions">
                {kind === 'tml' ? (
                  <>
                    <Button variant="soft" data-action="instances" onClick={instances}>
                      {t('选择版本')}
                    </Button>
                    <Button data-action="versions" onClick={versions}>
                      {t('下载版本')}
                    </Button>
                    <Button onClick={() => importInfo('tml')}>{t('导入')}</Button>
                  </>
                ) : (
                  <Button variant="soft" data-action="get-terraria" onClick={getTerraria}>
                    {state.demo ? t('管理资源') : t('获取资源')}
                  </Button>
                )}
              </div>
            </div>
          ))}
          <div className="panel-note">
            <Icon name="info" />
            {t('安装完成后验证资源完整性，失败时保留上一个可用版本。')}
          </div>
        </section>
        <section className="panel">
          <div className="panel-header">
            <h2>{t('关联账户')}</h2>
          </div>
          <AccountRows />
          <div className="panel-note">{t('本地导入无需登录账户。')}</div>
        </section>
      </div>
      <ContextNote>{t('游戏版本与资源统一管理；下载任务可在任意页面打开。')}</ContextNote>
    </>
  );
}
export function GamesPage() {
  const { t } = useTranslation();
  const { state, update } = useGameActions();
  return (
    <WorkspaceTabs
      value={state.gameTab}
      onChange={(gameTab) => update((current) => ({ ...current, gameTab }))}
      label={t('游戏资源分类')}
      items={[
        {
          value: 'installed',
          label: t('已安装'),
          data: { 'data-game-tab': 'installed' },
          content: <Installed />,
        },
        {
          value: 'sources',
          label: t('获取资源'),
          data: { 'data-game-tab': 'sources' },
          content: <Sources />,
        },
        {
          value: 'accounts',
          label: t('账户'),
          data: { 'data-game-tab': 'accounts' },
          content: (
            <>
              <div className="panel account-detail">
                <AccountRows detailed />
                <div className="panel-note">
                  <Icon name="lock" />
                  {t('登录、验证与下载分别展示状态；退出账户时可选择清除已保存的会话。')}
                </div>
              </div>
              <ContextNote>
                {t('Steam 支持验证与 Steam Guard 流程；GOG 使用网页授权流程。')}
              </ContextNote>
            </>
          ),
        },
      ]}
    />
  );
}
