# PrintingBooks Portal Deployment Script
# Automated deployment to RunASP.NET hosting

Write-Host "Starting deployment of PrintingBooks Management Portal..." -ForegroundColor Green
Write-Host "Target: drbaheegbook.runasp.net" -ForegroundColor Cyan
Write-Host "=================================================================================" -ForegroundColor Yellow

# Step 1: Clean and restore packages
Write-Host "Step 1: Cleaning and restoring packages..." -ForegroundColor Blue
dotnet clean --configuration Release
dotnet restore

# Step 2: Build the application
Write-Host "Step 2: Building application in Release mode..." -ForegroundColor Blue
dotnet build --configuration Release --no-restore

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed! Stopping deployment." -ForegroundColor Red
    exit 1
}

# Step 3: Publish the application
Write-Host "Step 3: Publishing application..." -ForegroundColor Blue
Remove-Item -Recurse -Force "./publish" -ErrorAction SilentlyContinue
dotnet publish --configuration Release --output "./publish"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed! Stopping deployment." -ForegroundColor Red
    exit 1
}

# Step 4: Deploy using MSDeploy (Web Deploy)
Write-Host "Step 4: Deploying to RunASP.NET..." -ForegroundColor Blue
Write-Host "Server: site79455.siteasp.net:8172" -ForegroundColor Yellow
Write-Host "Site: site79455" -ForegroundColor Yellow
Write-Host "URL: http://drbaheegbook.runasp.net/" -ForegroundColor Yellow

# Create publish parameters
$publishUrl = "site79455.siteasp.net"
$siteName = "site79455"
$username = "site79455"
$password = $env:DEPLOY_PASSWORD
if ([string]::IsNullOrEmpty($password)) {
    Write-Host "ERROR: DEPLOY_PASSWORD environment variable is not set!" -ForegroundColor Red
    exit 1
}

# Deploy using dotnet publish with WebDeploy
Write-Host "Executing WebDeploy..." -ForegroundColor Blue

dotnet publish --configuration Release `
    /p:PublishMethod=MSDeploy `
    /p:MSDeployServiceURL="$publishUrl`:8172" `
    /p:DeployDefaultTarget=WebPublish `
    /p:MSDeployPublishMethod=WMSVC `
    /p:CreatePackageOnPublish=false `
    /p:MSDeploySite="$siteName" `
    /p:UserName="$username" `
    /p:Password="$password" `
    /p:AllowUntrustedCertificate=true `
    /p:SkipExtraFilesOnServer=true `
    /p:DeleteExistingFiles=false

if ($LASTEXITCODE -eq 0) {
    Write-Host "Deployment successful!" -ForegroundColor Green
    Write-Host "Application URL: http://drbaheegbook.runasp.net/" -ForegroundColor Cyan
    Write-Host "=================================================================================" -ForegroundColor Yellow
    Write-Host "Post-deployment checklist:" -ForegroundColor Yellow
    Write-Host "  1. Application deployed successfully" -ForegroundColor Green
    Write-Host "  2. Test the URL: http://drbaheegbook.runasp.net/" -ForegroundColor White
    Write-Host "  3. Login with admin credentials" -ForegroundColor White
    Write-Host "  4. Verify sidebar navigation is working" -ForegroundColor White
    Write-Host "  5. Test responsive design on mobile" -ForegroundColor White
    Write-Host "=================================================================================" -ForegroundColor Yellow
} else {
    Write-Host "Deployment failed!" -ForegroundColor Red
    Write-Host "Please check the connection settings and credentials." -ForegroundColor Yellow
    exit 1
}

Write-Host "Deployment process completed!" -ForegroundColor Green