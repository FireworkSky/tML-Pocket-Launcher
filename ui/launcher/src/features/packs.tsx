import { useTranslation } from '../i18n';
import { useRef, useState } from 'react';
import { Button, EmptyState, Icon } from '../components/ui';
import { modTitle, parsePack, resolveMods } from '../domain/mods';
import type { Mod, ModPack } from '../domain/types';
import { describeModResolutionError, useModActions } from './mod-actions';

function usePackActions() {
  const { t } = useTranslation();
  const actions = useModActions();
  const { state, installed, update, openDialog, closeDialog, notice, notify, plan } = actions;
  const edit = (pack?: ModPack) =>
    openDialog({
      title: pack ? t('重命名模组包') : t('保存当前启用清单'),
      description: t('只保存模组内部名称及目标配置；不会复制模组文件或存档。'),
      body: <PackNameEditor pack={pack} />,
    });
  const apply = (pack: ModPack, add = false) => {
    if (pack.profile !== state.profile) {
      notice(t('配置版本不匹配'), t('请先选择这个清单对应的 tML 配置。'));
      return;
    }
    let closure: Mod[];
    try {
      closure = resolveMods(pack.mods, installed, true);
    } catch (error) {
      notice(t('清单依赖无法解析'), describeModResolutionError(error, t));
      return;
    }
    if (closure.some((mod) => !installed.some((local) => local.id === mod.id))) {
      notice(t('清单中有未安装模组'), t('先使用「下载缺失模组」补齐，再应用启用清单。'));
      return;
    }
    if (closure.some((mod) => mod.channel !== state.profile)) {
      notice(t('清单版本不兼容'), t('请检查模组与当前 tML 配置。'));
      return;
    }
    const ids = closure.map((mod) => mod.id);
    update((current) => ({
      ...current,
      mods: current.mods.map((mod) => ({
        ...mod,
        enabled: ids.includes(mod.id) || (add && mod.enabled),
      })),
    }));
    closeDialog(false);
    notify('已应用示例启用清单。', '配置');
  };
  const exportPack = (pack: ModPack) => {
    const payload = {
      format: 'tml-mobile-mod-list',
      version: 1,
      name: pack.name,
      profile: pack.profile,
      mods: pack.mods,
    };
    const url = URL.createObjectURL(
      new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' }),
    );
    const link = document.createElement('a');
    link.href = url;
    link.download = 'mod-list.json';
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    notify('已导出原型清单 JSON。');
  };
  const details = (pack: ModPack) =>
    openDialog({
      title: pack.name,
      description: t('模组启用清单 · {profile}', {
        profile: t(pack.profile === 'stable' ? '稳定配置' : '预览配置'),
      }),
      body: (
        <>
          <div className="mod-plan">
            {pack.mods.length ? (
              pack.mods.map((id) => (
                <div key={id}>
                  <strong>{modTitle(id)}</strong>
                  <span>{installed.some((mod) => mod.id === id) ? t('已安装') : t('未安装')}</span>
                </div>
              ))
            ) : (
              <p>{t('这是空的启用清单。')}</p>
            )}
          </div>
          <div className="pack-options">
            <Button variant="soft" data-action="mw-pack-apply" onClick={() => apply(pack)}>
              {t('仅启用此清单')}
            </Button>
            <Button data-action="mw-pack-add" onClick={() => apply(pack, true)}>
              {t('加入当前启用')}
            </Button>
            <Button data-action="mw-pack-download" onClick={() => plan(pack.mods)}>
              {t('下载缺失模组')}
            </Button>
            <Button
              data-action="mw-pack-update"
              onClick={() => {
                const replacement = {
                  ...pack,
                  mods: installed.filter((mod) => mod.enabled).map((mod) => mod.id),
                  profile: state.profile,
                };
                update((current) => ({
                  ...current,
                  packs: current.packs.map((item) => (item.id === pack.id ? replacement : item)),
                }));
                details(replacement);
              }}
            >
              {t('用当前启用更新')}
            </Button>
            <Button data-action="mw-pack-rename" onClick={() => edit(pack)}>
              {t('重命名')}
            </Button>
            <Button data-action="mw-pack-export" onClick={() => exportPack(pack)}>
              {t('导出 JSON')}
            </Button>
            <Button
              data-action="mw-pack-delete"
              onClick={() =>
                openDialog({
                  title: t('删除模组包清单？'),
                  description: t('只删除此启用清单，保留模组文件和存档。'),
                  actions: (
                    <>
                      <Button onClick={() => closeDialog()}>{t('取消')}</Button>
                      <Button
                        variant="primary"
                        data-action="mw-confirm-delete-pack"
                        onClick={() => {
                          update((current) => ({
                            ...current,
                            packs: current.packs.filter((item) => item.id !== pack.id),
                          }));
                          closeDialog(false);
                        }}
                      >
                        {t('删除清单')}
                      </Button>
                    </>
                  ),
                })
              }
            >
              {t('删除清单')}
            </Button>
          </div>
        </>
      ),
      drawer: true,
    });
  return { ...actions, edit, details };
}
export function PacksPanel() {
  const { t } = useTranslation();
  const { state, installed, update, notify, edit, details } = usePackActions();
  const file = useRef<HTMLInputElement>(null);
  return (
    <>
      <div className="pack-toolbar">
        <Button variant="soft" symbol="plus" data-action="mw-save-pack" onClick={() => edit()}>
          {t('保存当前清单')}
        </Button>
        <Button symbol="folder" data-action="mw-import-pack" onClick={() => file.current?.click()}>
          {t('导入清单')}
        </Button>
      </div>
      {state.packs.length ? (
        <section className="panel mod-list">
          {state.packs.map((pack) => (
            <Button
              key={pack.id}
              className="pack-row"
              data-action={`mw-pack-${pack.id}`}
              onClick={() => details(pack)}
            >
              <Icon name="mod" />
              <span>
                <strong>{pack.name}</strong>
                <small>
                  {t('{count} 项 · {profile} · {missing} 项未安装', {
                    count: pack.mods.length,
                    profile: t(pack.profile === 'stable' ? '稳定配置' : '预览配置'),
                    missing: pack.mods.filter((id) => !installed.some((mod) => mod.id === id))
                      .length,
                  })}
                </small>
              </span>
              <Icon name="chevron" />
            </Button>
          ))}
        </section>
      ) : (
        <EmptyState
          symbol="mod"
          title={t('保存一套模组组合')}
          description={t(
            '保存启用清单，方便切换、分享或补齐缺失模组。清单只记录模组名称和配置信息。',
          )}
        />
      )}
      <p className="list-caption">
        <Icon name="file" />
        {t('清单包管理 · 不加载模组，也不编译源码')}
      </p>
      <input
        ref={file}
        type="file"
        id="modpack-file"
        accept="application/json,.json"
        hidden
        onChange={(event) => {
          const file = event.target.files?.[0];
          event.target.value = '';
          if (!file) return;
          if (file.size > 1024 * 1024) return notify('清单文件请小于 1 MB。');
          file
            .text()
            .then((text) => {
              const parsed = parsePack(text);
              update((current) => ({
                ...current,
                packs: [...current.packs, { ...parsed, id: crypto.randomUUID() }],
              }));
              notify('已导入清单；模组文件需要单独下载。');
            })
            .catch(() => notify('无法读取清单，请选择有效的启动器清单 JSON。'));
        }}
      />
    </>
  );
}
function PackNameEditor({ pack }: { pack?: ModPack }) {
  const { t } = useTranslation();
  const { state, installed, update, closeDialog, notify } = usePackActions();
  const [name, setName] = useState(pack?.name || t('我的模组组合'));
  return (
    <>
      <label className="pack-name-field">
        {t('名称')}
        <input
          id="pack-name"
          maxLength={64}
          value={name}
          aria-label={t('模组包名称')}
          onChange={(event) => setName(event.target.value)}
        />
      </label>
      <div className="dialog-actions feature-actions">
        <Button onClick={() => closeDialog()}>{t('取消')}</Button>
        <Button
          variant="primary"
          data-action="mw-confirm-save-pack"
          onClick={() => {
            if (!name.trim()) return notify('请输入名称。');
            update((current) => ({
              ...current,
              packs: pack
                ? current.packs.map((item) =>
                    item.id === pack.id ? { ...item, name: name.trim() } : item,
                  )
                : [
                    ...current.packs,
                    {
                      id: crypto.randomUUID(),
                      name: name.trim(),
                      profile: state.profile,
                      mods: installed.filter((mod) => mod.enabled).map((mod) => mod.id),
                    },
                  ],
            }));
            closeDialog(false);
          }}
        >
          {t('保存')}
        </Button>
      </div>
    </>
  );
}
