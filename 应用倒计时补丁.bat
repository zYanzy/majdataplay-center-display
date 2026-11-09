@echo off
chcp 65001 >nul
cd /d "%~dp0"
setlocal

set "GAME=%~1"
if "%GAME%"=="" set /p "GAME=请输入游戏目录（含 MajdataPlay.exe）: "
set "GAME=%GAME:"=%"
if "%GAME%"=="" (
  echo 未提供游戏目录。
  pause
  exit /b 1
)

where py >nul 2>nul
if errorlevel 1 (
  echo 未找到 py 启动器，请安装 Python 3 后重试。
  pause
  exit /b 1
)

py -3 apply_countdown.py "%GAME%"
echo.
pause
