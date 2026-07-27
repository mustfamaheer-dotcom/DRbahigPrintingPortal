@echo off
cd /d "%~dp0"
echo Installing BookShopPrintAgent...
echo.

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: Right-click this file and select "Run as Administrator".
    pause
    exit /b 1
)

set DEST=%ProgramFiles%\BookShopPrintAgent
echo Copying to %DEST%...
if not exist "%DEST%" mkdir "%DEST%"
xcopy /E /I /Y "%~dp0*" "%DEST%" >nul

schtasks /create /tn "BookShopPrintAgent" /tr "\"%DEST%\BookShopPrintAgent.exe\"" /sc onstart /ru SYSTEM /rl highest /f >nul 2>&1

if %errorlevel% equ 0 (
    echo Scheduled task created - runs at system startup.
) else (
    echo WARNING: Could not create scheduled task.
)

echo Starting agent...
start "" "%DEST%\BookShopPrintAgent.exe"

echo.
echo ================================================
echo INSTALLATION COMPLETE
echo Agent running on http://localhost:8080
echo Auto-starts on every boot.
echo ================================================
pause
