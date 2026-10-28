# Fetches Microsoft's current C# compiler (Roslyn) into tools\roslyn so build.cmd can use modern C#.
# Nothing is installed on the PC: the files stay in this project folder. Delete tools\roslyn to go back
# to the compiler that ships with Windows.
# Run:  powershell -ExecutionPolicy Bypass -File scripts\get-compiler.ps1
param([string]$Version = '4.11.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $root 'tools\roslyn'
$tmp = Join-Path $env:TEMP ('dragflow-roslyn-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $tmp | Out-Null
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $pkg = Join-Path $tmp 'roslyn.zip'
    Invoke-WebRequest "https://www.nuget.org/api/v2/package/Microsoft.Net.Compilers.Toolset/$Version" -OutFile $pkg -UseBasicParsing
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $unz = Join-Path $tmp 'pkg'
    [IO.Compression.ZipFile]::ExtractToDirectory($pkg, $unz)
    $src = Join-Path $unz 'tasks\net472'
    $csc = Join-Path $src 'csc.exe'
    if (-not (Test-Path $csc)) { throw 'The package does not contain csc.exe.' }
    $sig = Get-AuthenticodeSignature $csc
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notlike '*Microsoft Corporation*') { throw "csc.exe is not signed by Microsoft ($($sig.Status)); not used." }
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    New-Item -ItemType Directory $dest -Force | Out-Null
    Copy-Item (Join-Path $src '*') $dest -Recurse -Force
    Write-Host "Roslyn $Version is in $dest (signed by Microsoft). build.cmd will use it from now on."
}
finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
