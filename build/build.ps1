param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\HermesInstaller\HermesInstaller.csproj'
$bin = Join-Path $root "src\HermesInstaller\bin\$Configuration\net48"
$outDir = Join-Path $root 'dist\HermesInstaller'
$zip = Join-Path $root 'dist\HermesInstaller-win-x64.zip'

Write-Host "Building $project ($Configuration)..." -ForegroundColor Cyan
& dotnet build $project -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

Copy-Item (Join-Path $bin 'HermesInstaller.exe') $outDir
$config = Join-Path $bin 'HermesInstaller.exe.config'
if (Test-Path $config) { Copy-Item $config $outDir }

foreach ($doc in @('README.md', 'LICENSE', 'NOTICE')) {
    $path = Join-Path $root $doc
    if (Test-Path $path) { Copy-Item $path $outDir }
}

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $outDir '*') -DestinationPath $zip

$size = [math]::Round((Get-Item $zip).Length / 1KB, 1)
Write-Host ""
Write-Host "Output : $outDir" -ForegroundColor Green
Write-Host "Zip    : $zip ($size KB)" -ForegroundColor Green
