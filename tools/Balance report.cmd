@echo off
rem Opens the balance page: every day of the server's compiled statistics on one page.
rem To read a copy from another computer, drag that telemetry folder onto this file.
python -I "%~dp0telemetry_report.py" %*
if errorlevel 1 pause
