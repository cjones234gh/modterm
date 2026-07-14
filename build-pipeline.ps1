# =================================================================================
# WinUI 3 Automated Build & Inno Setup Deployment Pipeline (modterm)
# =================================================================================
# Requires: .NET 8 SDK, Inno Setup 6 (ISCC.exe), network access on first run
#           to download redistributable runtimes.
# Also requires XtermSharp cloned next to this repo (see README).
#
# Publishes framework-dependent (not self-contained). The installer ships and
# silently installs:
#   - .NET 8 Desktop Runtime (needed by modterm + modtermTE)
#   - Windows App Runtime 1.8 (needed by unpackaged WinUI)
# =================================================================================

$ErrorActionPreference = "Stop"

# Resolve paths relative to this script so it works from any working directory.
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $RepoRoot

# 1. Configuration Constants
$SolutionPath   = Join-Path $RepoRoot "modterm.slnx"
$MainProjPath   = Join-Path $RepoRoot "modterm\modterm.csproj"
$TeProjPath     = Join-Path $RepoRoot "modtermTE\modtermTE.csproj"
$StagingFolder  = Join-Path $RepoRoot "deploy\staging"
$DepsFolder     = Join-Path $RepoRoot "deploy\dependencies"
$OutputFolder   = Join-Path $RepoRoot "deploy\output"
$InstallerIss   = Join-Path $RepoRoot "installer.iss"
$InnoCompiler   = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

# Must match Microsoft.WindowsAppSDK package version in modterm.csproj (1.8.x line).
$WindowsAppSdkRuntimeVersion = "1.8.260317003"
$WindowsAppRuntimeFileName   = "WindowsAppRuntimeInstall-x64.exe"
$WindowsAppRuntimeUrl        = "https://aka.ms/windowsappsdk/1.8/$WindowsAppSdkRuntimeVersion/windowsappruntimeinstall-x64.exe"

# .NET 8 Desktop Runtime (Microsoft.WindowsDesktop.App) — shared by modterm + modtermTE.
$DotNetDesktopRuntimeVersion  = "8.0.28"
$DotNetDesktopRuntimeFileName = "windowsdesktop-runtime-$DotNetDesktopRuntimeVersion-win-x64.exe"
$DotNetDesktopRuntimeUrl      = "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/$DotNetDesktopRuntimeVersion/$DotNetDesktopRuntimeFileName"

Write-Host "[1/5] Configuration" -ForegroundColor Cyan
Write-Host "  Repo:     $RepoRoot"
Write-Host "  Project:  $MainProjPath"
Write-Host "  Staging:  $StagingFolder"
Write-Host "  Mode:     framework-dependent (win-x64)"
Write-Host "  Runtimes: .NET Desktop $DotNetDesktopRuntimeVersion + Windows App SDK $WindowsAppSdkRuntimeVersion"

# 2. Clean and Prepare Target Folders
Write-Host "[2/5] Cleaning and preparing build paths..." -ForegroundColor Cyan
if (Test-Path $StagingFolder) {
    Get-ChildItem $StagingFolder -Force | Where-Object { $_.Name -ne "note.md" } | Remove-Item -Recurse -Force
}
New-Item -ItemType Directory -Path $StagingFolder -Force | Out-Null
New-Item -ItemType Directory -Path $DepsFolder -Force | Out-Null
New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null

function Get-CachedDependency {
    param(
        [Parameter(Mandatory)] [string] $FileName,
        [Parameter(Mandatory)] [string] $Url
    )

    $path = Join-Path $DepsFolder $FileName
    if (-not (Test-Path $path)) {
        Write-Host "--> $FileName not found. Downloading from Microsoft..." -ForegroundColor Yellow
        Write-Host "    $Url"
        Invoke-WebRequest -Uri $Url -OutFile $path
    } else {
        Write-Host "--> Using cached dependency: $FileName" -ForegroundColor DarkGray
    }

    return $path
}

# 3. Fetch redistributable runtimes if missing
$SdkInstallerPath = Get-CachedDependency -FileName $WindowsAppRuntimeFileName -Url $WindowsAppRuntimeUrl
$DotNetInstallerPath = Get-CachedDependency -FileName $DotNetDesktopRuntimeFileName -Url $DotNetDesktopRuntimeUrl

# 4. Build theme editor first, then publish the main unpackaged app framework-dependent.
# modterm copies modtermTE into the publish output via CopyThemeEditorToPublish;
# building TE with Platform=x64 keeps that path aligned with the csproj target.
Write-Host "[3/5] Publishing application binaries via Dotnet CLI..." -ForegroundColor Cyan

& dotnet build $TeProjPath -c Release -p:Platform=x64
if ($LASTEXITCODE -ne 0) {
    Write-Error "Building modtermTE failed."
    Exit 1
}

& dotnet publish $MainProjPath `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:Platform=x64 `
    -p:PublishReadyToRun=true `
    -p:PublishTrimmed=false `
    -o $StagingFolder

if ($LASTEXITCODE -ne 0) {
    Write-Error "Dotnet publish routine failed."
    Exit 1
}

# RID publish can leave a nested win-x64\ tree under TE's bin that gets partially
# mirrored into staging; strip that junk so the installer stays lean.
Get-ChildItem $StagingFolder -Directory -Filter "win-*" -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force
$NestedPublish = Join-Path $StagingFolder "publish"
if (Test-Path $NestedPublish) {
    Remove-Item $NestedPublish -Recurse -Force
}

$RequiredStagingFiles = @(
    "modterm.exe",
    "modtermTE.exe",
    "Assets\Fonts\BlexMonoNerdFontMono-Regular.ttf",
    "Assets\DefaultThemes\theme_Bluefang.json"
)
foreach ($relativePath in $RequiredStagingFiles) {
    $fullPath = Join-Path $StagingFolder $relativePath
    if (-not (Test-Path $fullPath)) {
        Write-Error "Expected publish output missing: $fullPath"
        Exit 1
    }
}

# 5. Execute Inno Setup Compiler
Write-Host "[4/5] Compiling final installer package with Inno Setup..." -ForegroundColor Cyan
if (-not (Test-Path $InnoCompiler)) {
    Write-Error "Inno Setup compiler could not be found at $InnoCompiler. Install Inno Setup 6 or update `$InnoCompiler."
    Exit 1
}

if (-not (Test-Path $SdkInstallerPath)) {
    Write-Error "Windows App Runtime installer missing at $SdkInstallerPath"
    Exit 1
}

if (-not (Test-Path $DotNetInstallerPath)) {
    Write-Error ".NET Desktop Runtime installer missing at $DotNetInstallerPath"
    Exit 1
}

& $InnoCompiler "/DDotNetDesktopRuntimeInstaller=$DotNetDesktopRuntimeFileName" $InstallerIss
if ($LASTEXITCODE -ne 0) {
    Write-Error "Inno Setup compilation step encountered a critical error."
    Exit 1
}

# 6. Verification
$SetupExe = Get-ChildItem $OutputFolder -Filter "modtermSetup*.exe" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

Write-Host "`n[5/5] Success! Installer built:" -ForegroundColor Green
if ($SetupExe) {
    Write-Host "  $($SetupExe.FullName) ($([math]::Round($SetupExe.Length / 1MB, 1)) MB)" -ForegroundColor Green
} else {
    Write-Host "  (See $OutputFolder)" -ForegroundColor Green
}
