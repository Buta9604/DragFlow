# Makes a release: builds, runs the tests, optionally signs, and writes bin\release\<version>\ with
#   DragFlow-<version>.exe, DragFlow-Setup-<version>.exe, latest.ini (what DragFlow's updater reads) and SHA256SUMS.txt,
#   plus bin\release\notes.md (this version's changelog, used for the GitHub release).
# Run:  powershell -ExecutionPolicy Bypass -File scripts\release.ps1 [-Notes "What changed"] [-Publish "C:\Users\me\OneDrive\DragFlow releases"]
#        [-CertThumbprint <thumbprint of a code-signing certificate in your certificate store>]
# -Publish copies the release into a folder; set that folder as the update source in Settings > Shortcuts & updates.
param([string]$Notes = '', [string]$Publish = '', [string]$CertThumbprint = $env:DRAGFLOW_CERT_THUMBPRINT)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& cmd /c "`"$root\scripts\build.cmd`""
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
& "$root\bin\DragFlow.Tests.exe" | Select-Object -Last 1
if ($LASTEXITCODE -ne 0) { throw 'Tests failed; no release made.' }

$exe = Join-Path $root 'bin\DragFlow.exe'
$version = ([Reflection.AssemblyName]::GetAssemblyName($exe)).Version.ToString(3)
$out = Join-Path $root "bin\release\$version"
New-Item -ItemType Directory $out -Force | Out-Null
$app = Join-Path $out "DragFlow-$version.exe"
$setup = Join-Path $out "DragFlow-Setup-$version.exe"
Copy-Item $exe $app -Force
Copy-Item (Join-Path $root 'bin\DragFlow-Setup.exe') $setup -Force

if ($CertThumbprint) {
    # Signing stops the "unknown publisher" warning. Needs a code-signing certificate (see docs\README.md).
    $cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My | Where-Object { $_.Thumbprint -eq $CertThumbprint } | Select-Object -First 1
    if (-not $cert) { throw "No certificate with thumbprint $CertThumbprint was found." }
    foreach ($f in $app, $setup) {
        $r = Set-AuthenticodeSignature -FilePath $f -Certificate $cert -TimestampServer 'http://timestamp.digicert.com' -HashAlgorithm SHA256
        if ($r.Status -ne 'Valid') { throw "Signing $f failed: $($r.StatusMessage)" }
    }
    Write-Host 'Signed.'
} else { Write-Host 'Not signed (no certificate given). Windows may warn people who download it.' }

$sha = (Get-FileHash $app -Algorithm SHA256).Hash.ToLower()
$ini = "version=$version`r`nfile=DragFlow-$version.exe`r`nsha256=$sha`r`nnotes=$Notes`r`n"
[IO.File]::WriteAllText((Join-Path $out 'latest.ini'), $ini, (New-Object Text.UTF8Encoding $false))   # no byte-order mark

# SHA256SUMS.txt (what people check a download against) and notes.md (this version's part of the changelog)
$sums = foreach ($f in $setup, $app) { (Get-FileHash $f -Algorithm SHA256).Hash.ToLower() + '  ' + (Split-Path $f -Leaf) }
[IO.File]::WriteAllText((Join-Path $out 'SHA256SUMS.txt'), (($sums -join "`n") + "`n"), (New-Object Text.UTF8Encoding $false))
$log = [IO.File]::ReadAllText((Join-Path $root 'docs\CHANGELOG.md'))
$m = [regex]::Match($log, "(?ms)^## $([regex]::Escape($version))\b[^\n]*\n(.*?)(?=^## |\z)")
$body = if ($m.Success) { $m.Groups[1].Value.Trim() } else { $Notes }
$body += "`n`n**Install:** download DragFlow-Setup-$version.exe and run it. It installs for you only and needs no administrator rights." +
         "`n`n**Check the download:** compare ``Get-FileHash DragFlow-Setup-$version.exe`` with SHA256SUMS.txt.`n"
[IO.File]::WriteAllText((Join-Path (Split-Path $out -Parent) 'notes.md'), $body, (New-Object Text.UTF8Encoding $false))
Write-Host "Release $version written to $out"

if ($Publish) {
    New-Item -ItemType Directory $Publish -Force | Out-Null
    Copy-Item $app, $setup -Destination $Publish -Force
    Copy-Item (Join-Path $out 'latest.ini') -Destination $Publish -Force   # copied last, so a half-copied release is never announced
    Write-Host "Published to $Publish"
}
