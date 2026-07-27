@echo off
cd /d "%~dp0"
start /min "" "%~dp0BookShopPrintAgent.exe"
echo BookShopPrintAgent started on http://localhost:8080
echo Close this window to stop the agent.
timeout /t 3 /nobreak >nul
