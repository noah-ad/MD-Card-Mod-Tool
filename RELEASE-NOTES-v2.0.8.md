# 2.0.8 本地测试版

- 新卡名称：不再只比较 Steam Build；检测活动账号与游戏内资源文件元数据变化，旧 v2 缓存自动失效。读取期间资源变化则保留旧目录，等待下次刷新。新下载的账号字典优先于内置字典，不再由并行任务先完成者决定。
- DPI：先设置缩放模式，再设置 96 DPI 设计基准，避免 WinForms 的 None → Dpi 切换清空基准。侧栏标题简化为 MD STUDIO，并为副标题明确分行。
- 动画：取消生成器暗中施加的缩小开场、持续放大和上下漂移，画布与附件中心对齐。16:9 素材的 100% 尺寸不再被二次缩小；非 16:9 内容仍等比居中。
- 保留 2.0.7 的动画文件占用预检、临时构建验证、首次备份与失败回滚。

使用：退出游戏，用新版重新导入视频并替换 22524；此前写入的 Mod 不会自动重写。新卡名在加载游戏目录后后台更新。本机解析 22920 名称为「小丑戏帮 哈特」。

限制：真实游戏不写入、不注入、不自动启动。工具内画布覆盖测试不代表游戏相机／外部缩放已验收；实际 150% 桌面、跨屏缩放和游戏召唤画面需要用户复测。此包尚未上传 GitHub。

技术依据：WinForms 8 的 AutoScaleMode setter 在模式切换时清空 AutoScaleDimensions，参见 [Microsoft WinForms 源码](https://github.com/dotnet/winforms/blob/v8.0.0/src/System.Windows.Forms/src/System/Windows/Forms/ContainerControl.cs)。
