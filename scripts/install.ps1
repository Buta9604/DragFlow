# Installs (or updates) DragFlow for the current user with the freshly built installer.
# Run:  powershell -ExecutionPolicy Bypass -File scripts\install.ps1            (no questions, default choices)
#       powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -Uninstall  (removes it, keeps your settings)
# Your settings, custom actions, rules, recipes and shelf (in %APPDATA%\DragFlow) are kept either way.
param([switch]$Uninstall)
$root = Split-Path $PSScriptRoot -Parent
if ($Uninstall) {
    $exe = Join-Path $env:LOCALAPPDATA 'DragFlow\DragFlow.exe'
    if (-not (Test-Path $exe)) { Write-Host 'DragFlow is not installed.'; return }
    Start-Process $exe -ArgumentList '--uninstall', '--quiet' -Wait
    Write-Host 'DragFlow removed. Your settings are still in %APPDATA%\DragFlow.'
    return
}
$setup = Join-Path $root 'bin\DragFlow-Setup.exe'
if (-not (Test-Path $setup)) { throw 'bin\DragFlow-Setup.exe not found - run scripts\build.cmd first.' }
$p = Start-Process $setup -ArgumentList '--quiet' -PassThru
$p.WaitForExit()   # only the installer; DragFlow, which it starts, keeps running
if ($p.ExitCode -ne 0) { throw "Setup failed (code $($p.ExitCode))." }
$version = (Get-Item (Join-Path $env:LOCALAPPDATA 'DragFlow\DragFlow.exe')).VersionInfo.FileVersion
Write-Host "DragFlow $version installed for $env:USERNAME and started."
