# 2.0.23 本地验证记录

日期：2026-10-05。Windows x64 / .NET 8。所有游戏写回测试只操作临时镜像，已有游戏文件和备份保持不变。

## 只读规则核对

读取本机 masterduel_Data/data.unity3d 中的 CardPicture prefab、卡片着色器和相关方法元数据／静态代码：

- Cardframe 使用高度控制宽度，aspect = 590 / 860，height = 1054。
- Illust_pendulum RawImage 的尺寸为 622.2×797.64，锚定位置 (0, -62.22)，中心 pivot；对应旧 Image prefab 的 (610, 782)×1.02。
- RawImage 的 UV 是 (0, 0, 1, 1)，着色器按传入 UV 取样。载图回调只设置纹理，没有把整张高图缩进可见插图区。
- 转换到 704×1024 卡框后，完整插图画布约位于 (49.1, 185.0)，大小 605.8×774.9；默认普通灵摆卡框的透明插图区只有 605×452。两者不是同一画布。

只读提取和反汇编用于确认坐标，不调用游戏代码，不修改 GameAssembly，不启动或注入游戏。分析产物仅留在开发证据目录，不打包游戏材质、纹理、Prefab、Bundle 或 DummyDll。

## 实际构图与 Bundle 回归

--test-pendulum-crop 使用当前 19716 的真实 Bundle 副本（允许原文件已包含用户 Mod），打开实际 ImageCropForm 并执行确认：

- 覆盖 card_frame13 / 14 / 15 / 16 / 17 / 19 六种灵摆卡框；15 和 17 使用缩放 0.73、位移 (38, -21)。
- 构图直接提供原生存储 PNG，替换后逐像素解码相等，没有额外缩放。
- 调用真实 MainForm 的读取／预览流程，圆形保持等比。
- 从原始存储纹理合成卡框预览；圆形只在插图区内检测，排除红色灵摆宝石干扰。
- 独立使用从 Prefab 读出的浮点坐标模拟游戏布局；圆形横纵误差不超过 3 像素，中心位置误差不超过 2 像素，渲染结果与卡框预览一致。
- 同一构图通过 2.0.22 的旧流程生成后，在游戏布局模拟中复现拉长，作为负向对照。
- 恢复首次备份后镜像 Bundle SHA-256 与初始文件相同；原游戏 Bundle 哈希未变化。

日志：recovery-evidence/pendulum-crop-2.0.23/full-chain-final.log；截图和生成 PNG 位于同名目录。测试不是工具内部单独的正反缩放自洽检查。

## 其他回归及限制

以下回归通过，日志中 ready=True，标准错误为空：

- --test-card-resource-fixes：青眼白龙／废品同步士内置卡图定位、原卡图超框、共享属性／星级图标的写回及还原；临时 LocalData 和 StreamingAssets 中验证可见区域灵摆映射，原游戏资源哈希不变。
- --test-texture-display-mapping：卡套原有映射、旧超框完整插图解码和裁剪窗口比例；此项保留完整插图的 512×683 源图测试，与实装插图区测试分开。
- --test-packaged-card-frames：64 个随包卡框及其透明区、炫彩／透明炫彩 Alpha。
- --test-overframe-workflow：四类卡框、换卡图、叠底背景、旧源图迁移及高缩放；源图正常化仍保留 512×683 / 512×512 / 704×1024 的各自规则。
- --test-main-form-layout：四语页面、导航与资源动作网格，clipped=False。
- --test-card-preview-raw：原始透明 RGB 预览不受影响。

对应日志位于 recovery-evidence/pendulum-crop-2.0.23。x64 Release 自包含发布成功；最终 EXE 的文件版本为 2.0.23.0。最终发布目录内的 EXE 再次运行全部六类灵摆构图链路及四语界面启动／布局检查，均退出 0，标准错误为空，gameWrites=False。日志为 published-chain.log 与 published-layout.log。恢复源码仍有原有 nullable 警告。

发布包根目录只有 EXE 和 data，保留 FFmpeg、既有许可说明、随包卡框及索引数据，不含本次游戏资源分析产物。

本版仅确认本机 PC 资源布局和副本写回；真实游戏视觉效果、未来游戏版本的 Prefab 布局变化及跨屏 DPI 行为不在本次已验收范围。已有错误 Mod 需用原图片重新构图。本地测试版未上传 GitHub。
