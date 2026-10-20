# Builds the Vacuo setup: publishes the app and the tray self-contained into one folder, compiles
# installer\vacuo.iss with Inno Setup 6, writes the setup's SHA-256 next to it and the release's file
# list (Vacuo-<version>-files.json: every published file's SHA-256 and size). With -PatchFrom and an
# earlier release's file list it also builds Vacuo-<version>-patch-<earlier>.exe, which writes only
# what changed since that release (K4, as in Odyssey).
# Every run uses a fresh folder under artifacts\<version>; nothing is deleted.
#
#   powershell -NoProfile -File tools\build-installer.ps1 [-Iscc <path to ISCC.exe>] [-PatchFrom <Vacuo-X.Y.Z-files.json>]

param(
    [string]$Iscc = '',
    [string]$PatchFrom = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$script = Join-Path $root 'installer\vacuo.iss'
$cli = Join-Path $root 'src\Vacuo.Cli'

function Write-Checksum([string]$file) {
    $hash = (Get-FileHash -Algorithm SHA256 $file).Hash.ToLowerInvariant()
    $name = Split-Path -Leaf $file
    Set-Content -Encoding ascii -NoNewline -Path "$file.sha256" -Value "$hash  $name"
    Write-Output "Setup: $file"
    Write-Output "SHA-256: $hash"
}

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

& $Iscc "/DAppVersion=$version" "/DSourceDir=$publish" "/DOutputDir=$output" $script
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }
Write-Checksum (Join-Path $output "Vacuo-$version-setup.exe")

# The next release's patch is built against this list, so it must describe these very files.
$files = Join-Path $output "Vacuo-$version-files.json"
dotnet run --project $cli -c Release -- dosya-kaydi $publish $version $files
if ($LASTEXITCODE -ne 0) { throw 'The file list could not be written.' }

if (-not $PatchFrom) { return }

$base = (Get-Content -Raw $PatchFrom | ConvertFrom-Json).Version
if ($base -eq $version) {
    Write-Output "The file list is for $version itself; no patch."
    return
}

$list = Join-Path $work "patch-from-$base.iss"
dotnet run --project $cli -c Release -- yama-listesi $PatchFrom $files $list
if ($LASTEXITCODE -ne 0) { throw 'The patch list could not be written.' }

& $Iscc "/DAppVersion=$version" "/DSourceDir=$publish" "/DOutputDir=$output" "/DPatchFrom=$base" "/DPatchList=$list" $script
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed for the patch.' }
Write-Checksum (Join-Path $output "Vacuo-$version-patch-$base.exe")
