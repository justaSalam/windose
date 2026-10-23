@echo off
title ELF Studio - GUI Designer for ELF Programs
cd /d "%~dp0"
echo Starting ELF Studio...
python run.py
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Application exited with error code %ERRORLEVEL%.
    pause
)
