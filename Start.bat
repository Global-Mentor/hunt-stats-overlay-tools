@echo off
cd /d "%~dp0"
if exist "%~dp0app\HuntStatsOverlayTools.exe" (
    start "" "%~dp0app\HuntStatsOverlayTools.exe" --root "%~dp0."
    exit /b
)
if not exist "%~dp0.local\build\HuntStatsOverlayTools.exe" (
    powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Build.ps1"
    if errorlevel 1 (
        pause
        exit /b 1
    )
)
start "" "%~dp0.local\build\HuntStatsOverlayTools.exe" --root "%~dp0."
