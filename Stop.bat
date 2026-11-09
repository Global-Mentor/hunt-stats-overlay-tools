@echo off
cd /d "%~dp0"
if exist "%~dp0app\HuntStatsOverlayTools.exe" (
    start "" /wait "%~dp0app\HuntStatsOverlayTools.exe" --root "%~dp0." --stop
    exit /b
)
if exist "%~dp0.local\build\HuntStatsOverlayTools.exe" start "" /wait "%~dp0.local\build\HuntStatsOverlayTools.exe" --root "%~dp0." --stop
