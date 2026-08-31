# MD Card Mod Tool 2.0.3

面向《游戏王 Master Duel》的 .NET 8 x64 资源查看与 Mod 制作工具。2.0.3 使用 Master Duel 风格的四栏目单窗口工作区，整合卡图、卡框／超框、视觉资源、怪兽动画预览与 Mod 管理，并修复连续操作后出现的按钮文字、导航与动画页残影。EXE 文件名仍保留“MD卡图查看替换器”，以兼容旧版启动脚本。

## 下载

从 [GitHub Releases](https://github.com/noah-ad/MD-Card-Mod-Tool/releases/latest) 下载 `MD-Card-Mod-Tool-v2.0.3-test-win-x64.zip`。解压后根目录只有 `MD卡图查看替换器.exe` 与 `data` 文件夹，直接运行 EXE 即可；若文件带有 Windows 下载标记，可先运行 `data\首次启动-解除下载封锁.bat`。

## 主要功能

- 自动从 Steam 注册表、`libraryfolders.vdf` 与 `appmanifest_1449850.acf` 定位实际游戏目录，正确处理 `Yu-Gi-Oh!  Master Duel` 中的双空格。
- 枚举所有 `LocalData/<账号>/0000`，记住每个游戏安装上次使用的账号，并允许在顶栏切换。
- 四语界面：简体中文、繁体中文、日语、英语可即时切换；设置、主题与减少动画偏好独立保存。
- 四语卡名和卡号检索仅出现在“卡片资源”。输入时显示带卡号、当前语言卡名和类型的候选，可用方向键与 Enter 直接定位；内建紧凑 Brotli 卡片目录包含卡号、四语卡名、卡片类型与卡框信息，检测到 Steam build ID 变化后会合并游戏内新卡数据。
- 现代化 Master Duel 深色工作区使用侧栏一级导航、圆角控件和按 DPI 缩放的矢量导航图标；视觉资源页顶部直接提供场地、壁纸、卡套、卡盒、硬币与头像装饰筛选。
- 2.0.3 将圆角按钮改为控件私有离屏缓冲并整帧提交，移除页面切换和嵌入动画页中的同步重绘／嵌套消息循环；连续 hover、播放暂停、切页及窗口／DPI 缩放后不会再把旧文字或边框画到相邻区域。
- 预览、导出和替换共用资源解析器。Bundle、serialized file、PathID 或 Texture2D 位置过期时，会按容器、名称、尺寸、逻辑路径及 Bundle 内资产重新定位；写入前会在临时副本重新验证。
- 支持卡图、透明超框、壁纸、大厅背景、决斗场地、卡套、头像、头像框、卡盒与硬币。普通卡与已有超框卡共用同一个“制作超框”编辑器：可直接读取当前原卡图，在最终卡面上拖动主体，以滚轮／滑杆缩放，也可随时更换卡图、添加或清除叠底背景；关闭后再次打开会恢复卡图位置和缩放。
- 卡框选择固定分成透明卡框、透明炫彩卡框、炫彩卡框和普通卡框，每类 16 套；“炫酷卡框”已统一更名为“炫彩卡框”。透明炫彩卡框使用 Floowan OfGradient 的炫彩 RGB 与 Astellar 的透明 Alpha 几何实时派生，不是给普通资源换标签。新卡首次制作默认进入透明超框并按卡片类型选择正确框种。
- 编辑器、导出与主界面预览统一为真实 Alpha：透明区域默认显示棋盘格，不再把 Alpha=0 像素强制投影成不透明颜色；最终预览 PNG 与实际写入 Texture2D 的 RGBA 完全一致。隐藏 RGB 仍会在 PNG 编码与 Bundle 往返中原样保留，只能通过显式诊断入口查看。需要拖动或缩放时切到“构图编辑”。
- 超框最终图层固定为“叠底背景 → 卡框 → 透明主体”，主体可真正跨过框线；透明与透明炫彩模式会在 Astellar Dirty Alpha 几何处清零 Alpha，同时保留游戏着色器需要的 RGB。
- 2.0 修正透明卡框实际写入后变成不透明的问题：Dirty Alpha 会覆盖主体与框线的交叠区域，并排除效果文本框几何；输出 PNG／Texture2D 将 Alpha 正确清零，同时保留透明像素的 RGB 数据。PSD 模板图层也以直通 Alpha 提取，不再对半透明边缘重复乘 Alpha。
- 图片预览使用版本化异步任务，并统一释放位图，避免快速切换资源时旧请求覆盖新选择或发生闪退。资源列表默认显示 Texture2D 原图，不会再自动叠加灵摆框；只有明确进入卡框预览或超框制作时才合成卡框，并按卡片类型推荐通常、效果、融合、同调、超量、灵摆或链接框。

## 怪兽动画

工具可直接读取尚未替换的原版六 Bundle，并在本机预览 Spine 4.2 动画。渲染器是独立的只读实现，不捆绑官方 Spine Runtime；使用 SkiaSharp 渲染并支持本机官方资源实际出现的骨骼、槽位、皮肤、region、mesh、linkedmesh、clipping、IK／path／transform／physics 约束、deform、drawOrder、双色 tint、PMA 混合及新旧 atlas 格式。

导入 GIF、视频或图片序列只用于替换一张已经拥有完整官方召唤演出的怪兽。写入前会确认同一地区的 HighEnd_HD／SD Texture、Atlas、Skeleton 均完整，并在失败时回滚；没有官方演出的怪兽不能启用导入或写入。

“给所有怪兽新增召唤动画”和运行时注入功能已正式移除。实机与 IL2CPP 分析确认：`IsMonsterCutin` 只决定游戏是否尝试加载，真正播放还依赖官方时间轴和内部登记；仅复制六个 Bundle 并强制资格判断会造成召唤卡顿但不播放。若旧测试版曾留下实验性 `Created` 事务，动画页仍会识别它，并只提供预览与一键还原／删除，避免遗留文件无法清理。

## 构建与发布

```powershell
dotnet build '.\MdCardModTool\MdCardModTool.csproj' -c Release -p:Platform=x64

dotnet publish '.\MdCardModTool\MdCardModTool.csproj' `
  -c Release -r win-x64 -p:Platform=x64 --self-contained true `
  -o '.\发布\MD-Card-Mod-Tool-v2.0.3-test'
```

发布包根目录只允许包含 EXE 与 `data`。`data` 内必须包含 `classdata.tpk`、预绑定卡图／动画索引、Astellar 超框模板、Floowan 卡框、`tools/texconv.exe`、`tools/ffmpeg.exe`、说明与第三方许可文件。

## 安全与数据边界

- 替换或恢复前必须完全退出 Master Duel。
- 首次修改每个正式 Bundle 前会备份到 `<游戏目录>\_MD卡图备份\<资源类型>\...`。
- 开发写回测试只针对新建的临时 LocalData 镜像或 Bundle 副本。
- 发布物不包含游戏 Bundle、游戏图片、兼容扫描报告、`test-artifacts` 或原生构建目录。
- 工具不会修改 `GameAssembly.dll`，也不会向 Master Duel 进程注入第三方 DLL。

## 第三方项目

- [AstellarTool](https://github.com/LLKSENsei/AstellarTool-Master-Duel-Modding-Tool)：四语卡片目录、元数据提取与视觉资源目录工作流参考，MIT；见 [ASTELLAR-NOTICE.txt](ASTELLAR-NOTICE.txt)。
- [Floowan / master-duel-modding](https://github.com/AmidoriA/master-duel-modding)：普通与 OfGradient 卡框及超框工作流参考，MIT；见 [FLOOWAN-NOTICE.txt](FLOOWAN-NOTICE.txt)。
- [SkiaSharp](https://github.com/mono/SkiaSharp)：Spine 预览渲染，MIT；其许可与第三方声明随发布包放在 `data/licenses`。

完整总览见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)；发布包中的对应文件为 `data/licenses/THIRD-PARTY-NOTICES.txt`。

本项目以 [MIT License](LICENSE) 开源。Master Duel、游戏资源与相关商标归其权利人所有；发布物不包含游戏本体文件。
