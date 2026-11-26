[CmdletBinding()]
param(
    [switch]$NoStartup,
    [switch]$NoLaunch,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

$RootDir = $PSScriptRoot
$SrcDir = Join-Path $RootDir 'src'
$ExePath = Join-Path $RootDir 'DisplayMosaic.exe'
$LegacyExePath = Join-Path $RootDir 'MonitorWallpaper.exe'
$IconPath = Join-Path $SrcDir 'DisplayMosaic.ico'
$ManifestPath = Join-Path $SrcDir 'app.manifest'
$StartupDir = [Environment]::GetFolderPath('Startup')
$StartupLink = Join-Path $StartupDir 'DisplayMosaic.lnk'
$LegacyStartupLink = Join-Path $StartupDir 'MonitorWallpaper.lnk'

function Stop-ExistingInstances {
    foreach ($procName in @('DisplayMosaic', 'MonitorWallpaper')) {
        Get-Process -Name $procName -ErrorAction SilentlyContinue | ForEach-Object {
            Write-Host "Stopping running $procName.exe (PID $($_.Id))..."
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        }
    }

    try {
        Get-CimInstance Win32_Process -Filter "Name='powershell.exe' OR Name='pwsh.exe'" -ErrorAction SilentlyContinue |
            Where-Object { $_.CommandLine -like '*MonitorWallpaper.ps1*' -or $_.CommandLine -like '*DisplayMosaic*' } |
            ForEach-Object {
                Write-Host "Stopping legacy script process (PID $($_.ProcessId))..."
                Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
            }
    }
    catch {
    }

    Start-Sleep -Milliseconds 300
}

if ($Uninstall) {
    Stop-ExistingInstances
    foreach ($lnkPath in @($StartupLink, $LegacyStartupLink)) {
        if (Test-Path -LiteralPath $lnkPath) {
            Remove-Item -LiteralPath $lnkPath -Force
            Write-Host "Removed startup shortcut: $lnkPath"
        }
    }
    Write-Host 'DisplayMosaic stopped and removed from startup.'
    exit 0
}

$FrameworkDir = @(
    (Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319'),
    (Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v4.0.30319')
) | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'csc.exe') } | Select-Object -First 1

if (-not $FrameworkDir) {
    throw 'Could not locate .NET Framework 4.x csc.exe in Windows\Microsoft.NET.'
}

$CscPath = Join-Path $FrameworkDir 'csc.exe'
$Sources = @(Get-ChildItem -LiteralPath $SrcDir -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName })
if ($Sources.Count -eq 0) {
    throw "No C# source files found in $SrcDir"
}

Stop-ExistingInstances

if (Test-Path -LiteralPath $LegacyExePath) {
    Remove-Item -LiteralPath $LegacyExePath -Force -ErrorAction SilentlyContinue
}
if (Test-Path -LiteralPath $LegacyStartupLink) {
    Remove-Item -LiteralPath $LegacyStartupLink -Force -ErrorAction SilentlyContinue
}

Write-Host "Compiling DisplayMosaic.exe using $CscPath..."
$cscArgs = @(
    '/nologo',
    '/target:winexe',
    '/optimize+',
    '/platform:anycpu',
    "/out:$ExePath",
    "/win32icon:$IconPath",
    "/win32manifest:$ManifestPath",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Management.dll',
    '/reference:System.Web.Extensions.dll',
    '/reference:System.Windows.Forms.dll'
) + $Sources

& $CscPath @cscArgs
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $ExePath)) {
    throw "Compilation failed with exit code $LASTEXITCODE."
}
Write-Host "Built: $ExePath"

if (-not $NoStartup) {
    $sh = New-Object -ComObject WScript.Shell
    $lnk = $sh.CreateShortcut($StartupLink)
    $lnk.TargetPath = $ExePath
    $lnk.Arguments = '--minimized --startup-delay'
    $lnk.WorkingDirectory = $RootDir
    $lnk.IconLocation = "$ExePath,0"
    $lnk.Description = 'DisplayMosaic (starts minimised in taskbar hidden icons)'
    $lnk.Save()
    Write-Host "Installed startup shortcut: $StartupLink"
}

if (-not $NoLaunch) {
    Start-Process -FilePath $ExePath -ArgumentList '--minimized' -WorkingDirectory $RootDir
    Write-Host 'Started DisplayMosaic.exe minimised in the taskbar hidden icons area.'
}
