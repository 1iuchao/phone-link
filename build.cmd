@echo off
rem Double-click to build. Uses the Roslyn compiler from VS Build Tools.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
pause
