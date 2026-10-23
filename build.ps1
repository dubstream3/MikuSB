# Builds the Windows release package the same way .github/workflows/release.yml does.
#
# Output (git-ignored):
#   build/MikuSB-win-x64/            publish output (multi-file, framework-dependent)
#   build/MikuSB-win-x64.zip         release archive
#   build/MikuSB-win-x64.zip.sha256  sha256 of the archive
#
# Usage:
#   pwsh -File build.ps1              # publish + zip
#   pwsh -File build.ps1 -NoZip       # publish only
#   pwsh -File build.ps1 -WithSymbols # keep pdb/debug info (leaks local source paths)
#   pwsh -File build.ps1 -Source "$env:USERPROFILE\.nuget\packages" -NoReadyToRun  # offline
#
# Debug info is dropped by default: a build made on a workstation stamps the absolute
# source path into every assembly's CodeView record, which would ship the build
# machine's layout to whoever gets the package.

param(
    [string]$Configuration = 'Release',
    # Restore only from this folder (e.g. the local NuGet cache) instead of nuget.org.
    # Useful on offline machines: -Source "$env:USERPROFILE\.nuget\packages"
    [string[]]$Source,
    # Skip ReadyToRun crossgen (needs the Crossgen2 pack, which an offline cache may lack).
    [switch]$NoReadyToRun,
    # Ship debug symbols too. Off by default: a local build stamps absolute source
    # paths (C:\Users\<you>\...) into the assemblies' CodeView records and the PDBs.
    [switch]$WithSymbols,
    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$outDir = Join-Path $root 'build'
$publishDir = Join-Path $outDir 'MikuSB-win-x64'
$zipPath = Join-Path $outDir 'MikuSB-win-x64.zip'

# Prefer the portable SDK; fall back to whatever `dotnet` resolves to.
$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
$extraArgs = @()
if (Test-Path $dotnet) {
    # The portable SDK here is missing the workload locator SDKs, so every project
    # evaluation fails with MSB4276 and MSBuild worker nodes die while the parent
    # only reports MSB4186. Running single-node with the resolver disabled avoids it.
    $extraArgs = @('-m:1', '-nodeReuse:false', '-p:MSBuildEnableWorkloadResolver=false')
} else {
    $dotnet = 'dotnet'
}

if (Test-Path $publishDir) { Remove-Item -Path $publishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

Write-Host "=== publish $Configuration -> $publishDir" -ForegroundColor Cyan
$publishArgs = @(
    'publish', (Join-Path $root 'MikuSB\MikuSB.csproj'),
    '-c', $Configuration,
    '-p:PublishProfile=MikuSB-Win64-MultiFile',
    '-o', $publishDir
)
foreach ($s in $Source) { if ($s) { $publishArgs += @('--source', $s) } }
if ($NoReadyToRun) { $publishArgs += '-p:PublishReadyToRun=false' }
if (-not $WithSymbols) { $publishArgs += @('-p:DebugType=none', '-p:DebugSymbols=false') }
$publishArgs += $extraArgs
& $dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

Copy-Item (Join-Path $root 'version.txt') $publishDir -Force
if (-not (Test-Path (Join-Path $publishDir 'MikuSB.Updater.exe'))) {
    throw 'MikuSB.Updater.exe was not bundled into the publish output.'
}

$files = Get-ChildItem $publishDir -Recurse -File
$bytes = ($files | Measure-Object -Property Length -Sum).Sum
Write-Host ("=== publish OK: {0} files, {1:N1} MB" -f $files.Count, ($bytes / 1MB)) -ForegroundColor Green

if (-not $WithSymbols) {
    # Safety net in case a project overrides DebugType.
    $pdbs = Get-ChildItem $publishDir -Recurse -Filter *.pdb -File
    foreach ($p in $pdbs) { Remove-Item $p.FullName -Force }
    if ($pdbs) { Write-Host ("=== dropped {0} stray pdb(s)" -f $pdbs.Count) }
}

if (-not $NoZip) {
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -Force
    $hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  MikuSB-win-x64.zip" | Set-Content "$zipPath.sha256"
    Write-Host ("=== zip: {0} ({1:N1} MB)" -f $zipPath, ((Get-Item $zipPath).Length / 1MB)) -ForegroundColor Green
    Write-Host "=== sha256: $hash"
}

exit 0
