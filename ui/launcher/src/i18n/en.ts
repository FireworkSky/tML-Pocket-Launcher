import shell from './en-shell';
import demo from './en-demo';
import editors from './en-editors';
import library from './en-library';
import settings from './en-settings';
import stores from './en-stores';

const english: Record<string, string> = {
  ...demo,
  ...editors,
  ...library,
  ...settings,
  ...stores,
  ...shell,
  界面语言: 'Language',
  '选择启动器的显示语言。': 'Choose the launcher’s display language.',
  跟随系统: 'Follow system',
  首页: 'Home',
  游戏: 'Games',
  模组: 'Mods',
  补丁: 'Patches',
  设置: 'Settings',
  资源: 'Resources',
  模组包: 'Mod packs',
  下载: 'Downloads',
  下载进度: 'Download progress',
  '下载 - 游戏': 'Downloads · Games',
  '下载 - 模组': 'Downloads · Mods',
  '下载 - 资源': 'Downloads · Resources',
  '下载 - 模组包': 'Downloads · Mod packs',
  任务仍在进行: 'A task is still running',
  '正在检查安装状态…': 'Checking installation status…',
  '正在取消…': 'Canceling…',
  '正在处理…': 'Working…',
  操作完成: 'Done',
  游戏异常退出: 'The game closed unexpectedly',
  游戏崩溃报告: 'Game crash report',
  复制报告: 'Copy report',
  分享诊断包: 'Share diagnostic ZIP',
  分享最近一次崩溃诊断包: 'Share latest crash diagnostic ZIP',
  没有已保存的崩溃报告: 'No saved crash report',
  无法分享诊断包: 'Could not share the diagnostic ZIP',
  无法复制报告: 'Could not copy the report',
  'Android 操作失败': 'Android operation failed',
  '此功能需要 Android 启动器': 'This feature requires the Android launcher',
  '操作等待超时，请查看任务或重试':
    'The operation timed out. Check Downloads and tasks or try again.',
  '正在通过 Steam 验证 Terraria 所有权…': 'Verifying Terraria ownership with Steam…',
  '正在检查模组与补丁…': 'Checking mods and patches…',
  'Steam 未能确认 Terraria 所有权，请登录拥有游戏的账户后重试':
    'Steam could not verify Terraria ownership. Sign in with an account that owns the game and try again.',
  '关闭启动器会取消当前操作，请在下载与任务中先完成或取消。':
    'Closing the launcher will cancel the current operation. Let it finish or cancel it in Downloads and tasks.',
};

export default english;
