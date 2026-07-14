# MD 卡图查看替换器

用于查看、导出、替换《Yu-Gi-Oh! Master Duel》本体中已下载卡图资源的 Windows 工具。

## 下载与使用

在右侧 **Releases** 下载最新的分享 ZIP，解压后双击 `启动 MD卡图查看替换器.bat`。

工具会自动定位游戏目录内的 `LocalData\\<用户哈希>\\0000` 与 `StreamingAssets\\AssetBundle`。首次建立本地索引后，之后启动直接读取缓存；异画、Token 与杂图的分类同样保存在本地。

## 主要功能

- 本地卡图、游戏内图片与 704×1024 卡框的分类浏览、搜索、导入替换和 PNG 导出
- 拖入图片替换、拖出条目导出
- 自动备份，并可还原所选 Bundle
- 超框卡图替换、单卡卡框选择/编辑和卡框预览
- 异画卡筛选：仅百鸽未收录且编号处于 `20567–22747` 的资源归为异画；其他未收录资源归为 Token／杂图

## 注意

替换会直接写入游戏文件。首次修改每个 Bundle 前，工具会在游戏目录创建 `_MD卡图备份`。完成替换或还原后，请完全退出并重启 Master Duel。

## 源码构建

项目使用 .NET 8 WinForms。当前工程依赖参考工具目录中的 `AssetsTools.NET`、`AssetsTools.NET.Texture`、`SixLabors.ImageSharp` 和 `classdata.tpk`；可按 `MdCardModTool.csproj` 中的引用位置准备这些文件，再执行：

```powershell
dotnet publish .\MdCardModTool\MdCardModTool.csproj -c Release -r win-x64 --self-contained true
```


云盘下载渠道：https://pan.quark.cn/s/a6bfde027547
