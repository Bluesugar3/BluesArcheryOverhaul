@echo off
rem Opens the play-test button window (tools\TestBuildMenu.ps1). Double-click this file.
start "" powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0TestBuildMenu.ps1"
