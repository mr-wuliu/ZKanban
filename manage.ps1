[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet("start", "stop", "restart", "status", "publish", "pack")]
    [string]$Action = "start"
)

# Hide the console window if running in a non-interactive context
if ($Host.Name -eq 'ConsoleHost') {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("kernel32.dll")]
    public static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
"@
    $consolePtr = [Win32]::GetConsoleWindow()
    if ($consolePtr -ne [IntPtr]::Zero) {
        [Win32]::ShowWindow($consolePtr, 0)  # SW_HIDE = 0
    }
}

$ProjectName = "ZKanban"
$ProjectDir = Join-Path $PSScriptRoot $ProjectName
$ProcessName = $ProjectName
$BuildDir = Join-Path $PSScriptRoot ".build"
$LogDir = Join-Path $BuildDir "logs"

function Get-AppProcess {
    return Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
}

function Get-AppExePath {
    $tf = (dotnet --info | Select-String '^\s*Version:' | Select-Object -First 1).Line.Trim() -replace 'Version:\s*', ''
    $rid = if ([Environment]::Is64BitOperatingSystem) { "win-x64" } else { "win-x86" }
    # Try Debug build output first, then find any matching exe
    $candidates = @(
        Join-Path $ProjectDir "bin\Debug\net10.0-windows\$ProjectName.exe"
        Join-Path $ProjectDir "bin\Debug\net10.0-windows\$rid\$ProjectName.exe"
    )
    foreach ($p in $candidates) {
        if (Test-Path $p) { return $p }
    }
    # Fallback: search for the exe
    $found = Get-ChildItem -Path (Join-Path $ProjectDir "bin") -Filter "$ProjectName.exe" -Recurse -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($found) { return $found.FullName }
    return $null
}

function Test-SourceChanged {
    $exePath = Get-AppExePath
    if (-not $exePath -or -not (Test-Path $exePath)) {
        return $true
    }

    $exeTime = (Get-Item $exePath).LastWriteTimeUtc
    $srcFiles = Get-ChildItem -Path $ProjectDir -Include *.cs, *.xaml, *.axaml, *.csproj, *.slnx -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }

    if (-not $srcFiles) { return $false }

    $newestSrc = ($srcFiles | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
    return $newestSrc -gt $exeTime
}

function Start-App {
    $proc = Get-AppProcess
    if ($proc) {
        Write-Host "[$ProjectName] is already running (PID: $($proc.Id))" -ForegroundColor Yellow
        return
    }

    # Rebuild if source has changed or exe is missing
    $exePath = Get-AppExePath
    if (Test-SourceChanged) {
        Write-Host "[$ProjectName] Source changed, rebuilding..." -ForegroundColor Cyan
        $logFile = Join-Path $LogDir "build-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
        New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
        dotnet build $ProjectDir --configuration Debug -v quiet 2>&1 | Out-File -FilePath $logFile -Encoding UTF8
        $exePath = Get-AppExePath
    }

    if (-not $exePath -or -not (Test-Path $exePath)) {
        Write-Host "[$ProjectName] Build failed. Cannot find executable." -ForegroundColor Red
        return
    }

    Write-Host "[$ProjectName] Starting..." -ForegroundColor Cyan
    Start-Process -FilePath $exePath -WindowStyle Hidden

    $proc = $null
    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Milliseconds 250
        $proc = Get-AppProcess
        if ($proc) { break }
    }

    if ($proc) {
        Write-Host "[$ProjectName] Started (PID: $($proc.Id))" -ForegroundColor Green
    } else {
        Write-Host "[$ProjectName] Failed to start after 5s." -ForegroundColor Red
    }
}

function Stop-App {
    $proc = Get-AppProcess
    if (-not $proc) {
        Write-Host "[$ProjectName] is not running." -ForegroundColor Yellow
        return
    }

    Write-Host "[$ProjectName] Stopping (PID: $($proc.Id))..." -ForegroundColor Cyan
    $proc | Stop-Process -Force

    $stillRunning = $false
    for ($i = 0; $i -lt 10; $i++) {
        Start-Sleep -Milliseconds 250
        if (-not (Get-AppProcess)) { break }
        $stillRunning = $true
    }

    if ($stillRunning -and (Get-AppProcess)) {
        Write-Host "[$ProjectName] Failed to stop." -ForegroundColor Red
    } else {
        Write-Host "[$ProjectName] Stopped." -ForegroundColor Green
    }
}

function Show-Status {
    $proc = Get-AppProcess
    if ($proc) {
        Write-Host "[$ProjectName] Running (PID: $($proc.Id), CPU: $($proc.CPU.ToString('F1'))s, Memory: $([math]::Round($proc.WorkingSet64 / 1MB, 1))MB)" -ForegroundColor Green
    } else {
        Write-Host "[$ProjectName] Not running." -ForegroundColor Red
    }
}

function Publish-App {
    $outDir = Join-Path $PSScriptRoot "publish"
    Write-Host "[$ProjectName] Publishing (optimized self-contained) to $outDir ..." -ForegroundColor Cyan
    dotnet publish $ProjectDir -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=none `
        -p:PublishTrimmed=true `
        -p:TrimMode=partial `
        -p:EnableCompressionInSingleFile=true `
        -p:StripSymbols=true `
        -o $outDir 2>&1 | ForEach-Object {
            if ($_ -match 'error\b') { Write-Host $_ -ForegroundColor Red }
            elseif ($_ -match 'warning\b') { Write-Host $_ -ForegroundColor DarkGray }
        }
    if ($LASTEXITCODE -eq 0) {
        # Remove NuGet package PDBs (not needed for distribution)
        Get-ChildItem $outDir -Filter *.pdb | Remove-Item -Force
        $exePath = Join-Path $outDir "$ProjectName.exe"
        $sizeMB = [math]::Round((Get-Item $exePath).Length / 1MB, 1)
        Write-Host "[$ProjectName] Published: $exePath ($sizeMB MB)" -ForegroundColor Green
    } else {
        Write-Host "[$ProjectName] Publish failed." -ForegroundColor Red
    }
}

switch ($Action) {
    "start"   { Start-App }
    "stop"    { Stop-App }
    "restart" { Stop-App; Start-App }
    "status"  { Show-Status }
    "publish" { Publish-App }
    "pack"    { Publish-App }
}
