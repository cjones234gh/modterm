# =================================================================================
# WinUI 3 Automated Build & Inno Setup Deployment Pipeline
# =================================================================================

# 1. Configuration Constants (Adjust paths as needed)
$SolutionPath   = ".\modterm.slnx"
$MainProjPath   = ".\modterm\modterm.csproj"  # Main app startup project
$StagingFolder  = ".\deploy\staging"
$DepsFolder     = ".\deploy\dependencies"
$InnoCompiler   = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" # Default path for v6.7.3

# 3. Clean and Prepare Target Folders
Write-Host "[2/5] Cleaning and preparing build paths..." -ForegroundColor Cyan
if (Test-Path $StagingFolder) { Remove-Item $StagingFolder -Recurse -Force }
New-Item -ItemType Directory -Path $StagingFolder -Force | Out-Null
if (-not (Test-Path $DepsFolder)) { New-Item -ItemType Directory -Path $DepsFolder -Force | Out-Null }

# 4. Fetch the Latest Windows App SDK Bootstrapper if missing
$SDKInstallerPath = Join-Path $DepsFolder "WindowsAppRuntimeInstall-x64.exe"
if (-not (Test-Path $SDKInstallerPath)) {
    Write-Host "--> WindowsAppRuntimeInstall.exe not found. Downloading from Microsoft..." -ForegroundColor Yellow
    # Downloads the official runtime bootstrapper client
    $DownloadUrl = "https://aka.ms/windowsappsdk/2.2/2.2.0/windowsappruntimeinstall-x64.exe" 
    Invoke-WebRequest -Uri $DownloadUrl -OutFile $SDKInstallerPath
}

# 5. Compile Solution using .NET SDK CLI
# Because you set <WindowsPackageType>None</WindowsPackageType>, publishing the primary project
# automatically evaluates and bundles your project dependencies (like your secondary .EXE and custom .DLL).
Write-Host "[3/5] Publishing application binaries via Dotnet CLI..." -ForegroundColor Cyan
& dotnet publish $MainProjPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishReadyToRun=true `
    -o $StagingFolder

if ($LASTEXITCODE -ne 0) {
    Write-Error "Dotnet publish routine failed."
    Exit 1
}

# 6. Execute Inno Setup Compiler 
Write-Host "[4/5] Compiling final installer package with Inno Setup..." -ForegroundColor Cyan
if (-not (Test-Path $InnoCompiler)) {
    Write-Error "Inno Setup compiler could not be found at $InnoCompiler. Verify your 6.7.3 installation path."
    Exit 1
}

& $InnoCompiler ".\installer.iss"

# 7. Verification Loop
if ($LASTEXITCODE -eq 0) {
    Write-Host "`n[5/5] Success! Single binary setup executable compiled to your Output directory." -ForegroundColor Green
} else {
    Write-Error "Inno Setup compilation step encountered a critical error."
}
