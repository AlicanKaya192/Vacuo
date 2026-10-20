# Builds the Vacuo setup: publishes the app and the tray self-contained into one folder, compiles
# installer\vacuo.iss with Inno Setup 6 and writes the setup's SHA-256 next to it.
# Every run uses a fresh folder under artifacts\<version>; nothing is deleted.
#
#   powershell -NoProfile -File tools\build-installer.ps1 [-Iscc <path to ISCC.exe>]

param(
    [string]$Iscc = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

[xml]$project = Get-Content (Join-Path $root 'src\Vacuo.App\Vacuo.App.csproj')
$version = ($project.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'No <Version> in src\Vacuo.App\Vacuo.App.csproj.' }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$work = Join-Path $root "artifacts\$version-$stamp"
$publish = Join-Path $work 'publish'
$output = Join-Path $work 'installer'

foreach ($app in 'src\Vacuo.App', 'src\Vacuo.Tray') {
    dotnet publish (Join-Path $root $app) -c Release -r win-x64 --self-contained true -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $app." }
}

if (-not $Iscc) {
    $Iscc = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Iscc) { throw 'Inno Setup 6 (ISCC.exe) was not found.' }

& $Iscc "/DAppVersion=$version" "/DSourceDir=$publish" "/DOutputDir=$output" (Join-Path $root 'installer\vacuo.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }

$setup = Join-Path $output "Vacuo-$version-setup.exe"
$hash = (Get-FileHash -Algorithm SHA256 $setup).Hash.ToLowerInvariant()
Set-Content -Encoding ascii -NoNewline -Path "$setup.sha256" -Value "$hash  Vacuo-$version-setup.exe"
Write-Output "Setup: $setup"
Write-Output "SHA-256: $hash"
