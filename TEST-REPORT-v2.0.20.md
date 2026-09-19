# 2.0.20 多页动画移植验证

- Release 自包含发布成功，0 编译错误；恢复源码的既有警告尚未清理。
- `--test-atlas-merge` 通过：新旧 Atlas 坐标格式、旋转及裁边字段保留，透明 RGB 和 PMA 像素保留；混合 PMA、超尺寸、重复区域及尺寸不符拒绝测试通过。
- 发布程序实测 13669 → 10001（临时镜像）：来源 HighEnd_HD 三张纹理，SD 一张纹理；写入六 Bundle、重新载入预览、提交中途故障回滚、还原全部通过。
- 发布程序实测 3413 → 10001（临时镜像）：单页来源回归及上述事务测试全部通过。
- 两项真实资源副本测试均验证来源和真实目标文件哈希未改变。没有写入真实游戏，也未验证实际游戏召唤。
- 预览产物：工作区 recovery-evidence/transfer13669-final/transferred-preview.png、transfer3413-final/transferred-preview.png；游戏资源不包含在发行包中。
