# 2.0.30 验证记录

## 原因与兼容范围

截图在导入媒体阶段显示 FFmpeg `Unrecognized option 'vsync'`，与卡号 15994 的资源定位无关。2.0.29 代码在所有视频／GIF 抽帧请求中硬编码 `-vsync 0`，不适用于删除或未提供此选项的 FFmpeg。

本机沿用完整包中的 FFmpeg 8.1.1（SHA-256 `228D7A8556258DE907FDB55F36850078EBC7680B84EC30D84EA02E99BEC1D1EB`），它仍接受旧参数，因此未在本机谎称复现了截图的具体 FFmpeg 构建。修复后的生产参数不再包含 `vsync`；对不支持新版 `fps_mode` 的旧构建，用模拟错误触发一次回退，再由真实 FFmpeg 执行无同步选项的抽帧。

## 新增媒体回归

`--test-ffmpeg-media <独立输出目录>` 使用合成素材，不读取用户原视频，不改游戏：

- 24 帧／12 FPS 的真实无损 FFV1 视频：原分辨率逐帧 RGBA 全像素一致；帧数、帧率、时长正确。
- 含中文、空格与 `&` 的文件路径安全传参；起始秒、帧数上限、降帧率与等比缩放。
- 实际可变帧时长 GIF 按指定 FPS 取帧，不受循环标记影响重复生成。
- 真实视频去绿幕的透明 Alpha 与非绿主体保留。
- 新版参数全程不含 `-vsync`；模拟旧构建拒绝 `fps_mode` 后清理临时输出，只重试一次并完成真实抽帧。
- 解码错误不误触发兼容回退，保留真实错误和 FFmpeg 路径；回退也失败时停止。
- 取消真实限速 FFmpeg 进程后等待退出，清理临时帧；损坏视频报告输入问题。
- 实际 WinForms 动画窗体的自动画质探测／固定画质导入，读取到正确帧数，退出忙碌状态并恢复资源提示。

## 发行回归

x64 Debug／Release 构建、自包含发布成功，0 个编译错误，保留既有的 360 个源码警告。FFmpeg 沿用 2.0.29 相同二进制，不替换编解码库；根目录仍为 EXE + data。

最终自包含 EXE 的 11 个测试入口全部退出码 0、stderr 为空，无超时：

- `--test-ffmpeg-media`（9.31 秒）：上述真实视频、可变时长 GIF、全像素检查、模拟旧选项回退、取消、输入失败与实际窗体导入。
- `--test-original-animation`（24.37 秒）：无损视频抽帧、RGBA32、已登记多页写回与容量限制、失败回滚／还原；真实游戏 fixture 哈希不变。
- `--test-animation-discovery`（76.42 秒）：2.0.29 的原生 Timeline 分类、别名与后续页、旧缓存补全和四语窗体。
- `--test-texture-mod-restore`（13.75 秒）：实际界面入口还原贴图与超框开关，批量／失败回滚。
- `--test-new-animation-links`（20.08 秒）：22524 同 Build 配套动画资源。
- `--test-animation-alias-6969`（82.05 秒）：双页预览、替换、回滚／还原。
- `--test-animation-write-card ... 7302 ...`（17.01 秒）：补页后的动画镜像写回。
- `--test-mod-update-recovery`（108.24 秒）：游戏更新恢复库、透明像素、单卡超框登记、账号隔离。
- `--test-mod-management`（61.77 秒）：多选／选中导出／排序。
- `--test-mod-packages`（30.93 秒）：直接替换 ZIP 与工具包。
- `--test-main-form-layout`（11.84 秒）：2.0.30 标题、侧栏版本与程序版本一致、导航与搜索显隐；不等同于跨屏 DPI 实测。

被测试的 EXE 为 78,394,250 字节，文件版本 `2.0.30.0`／产品版本 `2.0.30`，SHA-256 `6BE7D73BDBDCACE81EDE22FDE8C0C6E11AE1DAAB65D06E87FB62DAF2ED483A1C`。验证后仅同步本说明，不重新构建程序。证据与两组结果清单位于工作区 `recovery-evidence/release-2.0.30/published-regressions`、`published-media-regressions`，不随发行包分发。

## 边界

未取得截图电脑的 FFmpeg／原视频，未逐个测试全部 FFmpeg 版本与编码格式；旧版兼容分支的参数拒绝为模拟，抽帧为实际执行。真实游戏召唤、其他电脑与跨屏 DPI 未验收，手机端未开发或测试。
