# 2.0.15 源码恢复记录（2026-09-16）

## 来源与边界

- 回收站未找到原 github-sync/recovered-src，不能宣称找回原始源码。
- 以 GitHub ab28c93（2.0.12）工程配置、依赖和资源为基础，从现有 2.0.15 EXE 提取托管程序集并用 ILSpy 9.1.0.7988 反编译，恢复 130 个应用类型文件及其后新增手机端功能。
- 原 EXE SHA-256：1100063E5BAA0C1B46E18E389C7DA618D0CDC8FF4586EB45E67806F757E7BACA。原 EXE 未覆盖。
- 解包依据 Microsoft.NET.HostModel Bundle Manifest/FileEntry 格式：
  https://github.com/dotnet/runtime/blob/v8.0.0/src/installer/managed/Microsoft.NET.HostModel/Bundle/Manifest.cs
  https://github.com/dotnet/runtime/blob/v8.0.0/src/installer/managed/Microsoft.NET.HostModel/Bundle/FileEntry.cs
- 原注释、局部变量名、源码排版、未编译进程序的脚本和未提交 Git 历史不保证可恢复。保留 GitHub 已有历史起点，不伪造丢失的本地提交。

## 编译修整

- 移除编译器保留 RefSafetyRules 声明；用 ReadOnlyCollection 重建编译器生成的只读集合辅助类型。
- 从同一版本间未改动的 GitHub 源码恢复 AnimationBundleSet、Spine42PreviewRenderer 及 ModEngine.ScanBundle，修正反编译导致的 init-only 赋值和元组类型推断错误。重复独立 Spine42CompatibilityResult 文件从编译中排除。
- 工程版本和发行说明指向 2.0.15；复制当前 data 中的卡名／动画／卡图目录和文档。

## 验证

- Release 构建成功：0 错误、364 项警告（反编译导致的可空注解和生成代码警告仍需整理）。不是原始二进制逐字节复现。
- 主窗口四语、导航、布局、动画页切换测试通过（clipped=False）。
- 统一手机测试通过：手机文件副本索引、卡图／视觉资源替换、Mod 包往返、超框表登记／还原、22524 三 Bundle 动画生成／预览／回滚／还原、3413 动画移植、主工作区预览。真实手机文件哈希未改变。
- PC Mod 包兼容与安全回归日志见工作区 recovery-evidence/pc-mod.log。
- 手机实机效果与全量资产未验收。当前恢复快照仍包含 2.0.15 的已知问题，特别是用户报告的 Mod 列表问题，不冒充已修复。

## 编译

```powershell
dotnet build MdCardModTool/MdCardModTool.csproj -c Release
```

反编译原稿、解包结果和构建日志在工作区 recovery-evidence；可维护项目在 recovered-src。恢复工作没有修改真实游戏资源或备份。
