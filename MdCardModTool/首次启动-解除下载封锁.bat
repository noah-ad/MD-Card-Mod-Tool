@echo off
chcp 65001 >nul
set "MDCARD_RELEASE_DIR=%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$releaseRoot = [IO.Path]::GetFullPath($env:MDCARD_RELEASE_DIR); Get-ChildItem -LiteralPath $releaseRoot -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue; Start-Process -FilePath (Join-Path $releaseRoot 'MD卡图查看替换器.exe')"
if errorlevel 1 (
  echo 无法解除 Windows 下载封锁，请右键程序目录、打开属性后手动解除封锁。
  pause
)
