# 2.0.24 验证记录

## 测试范围

新增 --test-mod-management 使用本机官方 10001 动画和超框登记资源的只读副本，在独立临时账号中构造卡图、重复纹理、内置卡图、视觉资源、已还原文件和六个已修改动画 Bundle。

- 验证单选及多选导出范围、同 Bundle 去重、关联动画／超框登记、视觉资源卡号碰撞隔离和已还原文件排除。
- 验证直接替换 ZIP 与工具包重新导入，映射到目标账号而非创建导出者账号。
- 验证全部导出保持原有范围，以及空选择／已还原选择拒绝创建文件。
- 验证实际文件时间、关联动画时间和公共超框表不覆盖旧卡独立时间；混用斜杠的路径先正规化。
- 通过 WinForms 控件检查默认时间排序、升降序／名称排序、多选计数、禁用空选择、排序后保留选择、四语标签、Mod 搜索范围及普通资源页恢复单选／四列。
- 检查窄窗口下四语操作栏和 150% 缩放的换行、控件边界及列宽，保存离屏截图。模拟缩放不等于真实跨屏 DPI 验收。
- 原始游戏资源 SHA-256 前后比对，开发写回仅限临时副本。

日志和截图位于 recovery-evidence/mod-management-2.0.24，不打包游戏资源或测试副本。

## 结果

x64 Release 构建和自包含发布成功，0 错误，恢复源码原有的 365 个 nullable 等警告仍存在。源码未含 FFmpeg 的发布警告由打包时从上一版复制既有工具解决，未下载新二进制。

最终发布 EXE（2.0.24.0）执行以下测试均退出 0，标准错误为空：

- --test-mod-management：selected=True、multiSelect=True、direct=True、legacy=True、animationBundles=6、gate=True、builtin=True、visual=True、restoredExcluded=True、accountMapping=True、gameWrites=False。包含仅修改动画的卡片关联，以及空／已还原选择拒绝创建 ZIP。
- WinForms 检查：languages=4、selectionPreserved=True、sortChoices=3、searchScoped=True；名称顺序、时间升降序和序列化偏好通过。
- 窄窗口 150% 模拟缩放：窗口宽 1600px，操作栏宽 1218px、高 148px，确认第二行存在且全部控件在操作栏边界内；已检查截图。
- --test-mod-packages：四类资源、旧包、直接替换包、无清单 ZIP、账号映射、首次备份及注入失败回滚通过，10 类不安全／损坏输入被拒绝，gameWrites=False。
- --test-main-form-layout：四语主界面、资源动作、动画页切换及布局检查通过，clipped=False。

最终目录保留 EXE 与 data 平行布局，既有 FFmpeg、许可、卡框和索引数据纳入打包；不含游戏 Bundle 或本次临时测试镜像。此版未上传 GitHub，未启动真实游戏；实验功能的原有游戏验收边界不变。
