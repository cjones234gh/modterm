param(
    [Parameter(Mandatory = $true)]
    [string]$WorkspaceFolder
)

$ErrorActionPreference = 'Stop'

Get-Process modterm -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$outDir = Join-Path $WorkspaceFolder 'modterm\bin\x64\Debug\net8.0-windows10.0.19041.0'
$exe = Join-Path $outDir 'modterm.exe'

if (-not (Test-Path $exe)) {
    throw "Build output not found: $exe. Build the x64 Debug configuration first."
}

$proc = Start-Process -FilePath $exe -WorkingDirectory $outDir -PassThru
Start-Sleep -Seconds 2

if (-not (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) {
    throw "modterm.exe exited during startup (PID $($proc.Id))."
}

$pidFile = Join-Path $WorkspaceFolder '.vscode\modterm.pid'
Set-Content -Path $pidFile -Value $proc.Id -NoNewline -Encoding ascii
Write-Host "modterm started with PID $($proc.Id)"
