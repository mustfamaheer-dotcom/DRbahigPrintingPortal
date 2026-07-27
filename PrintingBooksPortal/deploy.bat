@echo off
echo 🚀 PrintingBooks Portal - Quick Deploy
echo =====================================
echo Target: drbaheegbook.runasp.net
echo.

REM Build and deploy using the publish profile (MSDeploy via WMSVC).
REM NOTE: In .NET 10, passing bare /p:MSDeploy* flags to `dotnet publish`
REM silently falls back to a folder publish and never contacts the server.
REM Using the named PublishProfile (MonsterASP.pubxml) is what actually
REM loads the MSDeploy/WMSVC targets and pushes files to RunASP.NET.
echo 📦 Building and deploying application...
if "%DEPLOY_PASSWORD%"=="" (
    echo ❌ ERROR: DEPLOY_PASSWORD environment variable is not set!
    pause
    exit /b 1
)
dotnet publish -c Release -p:PublishProfile=MonsterASP -p:Password=%DEPLOY_PASSWORD%

if %errorlevel% equ 0 (
    echo.
    echo ✅ Deployment successful!
    echo 🌐 URL: http://drbaheegbook.runasp.net/
    echo.
) else (
    echo.
    echo ❌ Deployment failed!
    echo.
)

pause