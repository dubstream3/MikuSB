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
    [switch]$NoZip,
    # Publish straight into this folder instead of build/MikuSB-win-x64. Needed when a server is
    # running from build/MikuSB-win-x64: the publish step deletes that tree and Windows refuses
    # to remove clrjit.dll while the process has it mapped.
    [string]$OutName = 'MikuSB-win-x64',
    # Public release: compile the dev-only instrumentation out (MikuDev=false). Internal/dev builds
    # keep it, so the login report in Game.log stays available while debugging.
    [switch]$Public
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$outDir = Join-Path $root 'build'
$publishDir = Join-Path $outDir $OutName
$zipPath = Join-Path $outDir "$OutName.zip"

# Refuse to ship a Lua payload the client cannot decode. SendClientPatch drops any patch that
# contains one of ForbiddenPatchChars ('+', '<', '>', '&', "'", '"') because System.Text.Json
# escapes them to \uXXXX and the client's Lua 5.1 JSON decoder rejects the escape - the patch then
# becomes a silent in-game no-op. Checked here so a broken payload cannot reach a release.
$checkPatches = Join-Path $root 'tools\check_client_patches.py'
if ((Test-Path $checkPatches) -and (Get-Command python -ErrorAction SilentlyContinue)) {
    Write-Host '=== checking client patch payloads' -ForegroundColor Cyan
    & python $checkPatches
    if ($LASTEXITCODE -ne 0) { throw 'A client patch payload contains a character the client cannot decode.' }
}

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

if (Test-Path $publishDir) {
    # A dev build links Resources instead of copying it. Drop the link itself first: a recursive
    # delete that walks a junction can reach the resource repository and wipe it.
    $staleLink = Join-Path $publishDir 'Resources'
    if (Test-Path $staleLink) {
        $li = Get-Item $staleLink -Force
        if ($li.LinkType) { [System.IO.Directory]::Delete($staleLink) }
    }
    Remove-Item -Path $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

Write-Host "=== publish $Configuration -> $publishDir" -ForegroundColor Cyan
$publishArgs = @(
    'publish', (Join-Path $root 'MikuSB\MikuSB.csproj'),
    '-c', $Configuration,
    '-p:PublishProfile=MikuSB-Win64-MultiFile',
    '-o', $publishDir
)
if ($Public) { $publishArgs += '-p:MikuDev=false' } else { $publishArgs += '-p:MikuDev=true' }
foreach ($s in $Source) { if ($s) { $publishArgs += @('--source', $s) } }
if ($NoReadyToRun) { $publishArgs += '-p:PublishReadyToRun=false' }
if (-not $WithSymbols) { $publishArgs += @('-p:DebugType=none', '-p:DebugSymbols=false') }
$publishArgs += $extraArgs
& $dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

# A public package must not carry the dev diagnostics: they exist only to be read by whoever is
# debugging, and the payload set is a fingerprint of our internals. Assert it instead of trusting
# the flag: the marker is a UTF-16 string literal in the assembly's metadata.
$serverDll = Join-Path $publishDir 'MikuGameServer.dll'
if (Test-Path $serverDll) {
    $bytes = [System.IO.File]::ReadAllBytes($serverDll)
    $utf16 = [System.Text.Encoding]::Unicode.GetString($bytes)
    $hasDevMarker = $utf16.Contains('MIKU_CAP')
    if ($Public -and $hasDevMarker) { throw 'Dev diagnostics are still present in a public build.' }
    if (-not $Public -and -not $hasDevMarker) { throw 'Dev diagnostics are missing from a dev build (MIKU_DEV not defined?).' }
    Write-Host ("=== dev diagnostics: {0}" -f $(if ($hasDevMarker) { 'present (dev build)' } else { 'absent (public build)' })) -ForegroundColor Cyan
}

Copy-Item (Join-Path $root 'version.txt') $publishDir -Force
if (-not (Test-Path (Join-Path $publishDir 'MikuSB.Updater.exe'))) {
    throw 'MikuSB.Updater.exe was not bundled into the publish output.'
}

# The injected patch DLL is not produced by this repository, but the launcher refuses to launch
# without Patch/MikuSB-Patch.dll and its public download URL is not reachable from every network.
# An internal build therefore ships the locally built DLLs; public builds instead let
# PatchDownloadService fetch the release asset from the public repository.
# The injected DLL is built from the sibling MikuSB-Patch checkout (main repo root, gitignored), not
# from this tree, so probe both locations.
$patchRootCandidates = @(
    (Join-Path $root 'MikuSB-Patch'),
    (Join-Path $root '..\..\MikuSB-Patch')
) | Where-Object { (Test-Path (Join-Path $_ 'build\MikuSB-Patch.dll')) -or (Test-Path (Join-Path $_ 'build-cn\MikuSB-Patch.dll')) }
$patchRoot = $patchRootCandidates | Select-Object -First 1
if (-not $patchRoot) { $patchRoot = Join-Path $root '..\..\MikuSB-Patch' }
Write-Host ("=== patch source: {0}" -f $patchRoot)

$patchSources = @(
    @{ From = (Join-Path $patchRoot 'build\MikuSB-Patch.dll');    To = 'MikuSB-Patch.dll' },
    @{ From = (Join-Path $patchRoot 'build-cn\MikuSB-Patch.dll'); To = 'MikuSB-Patch-cn.dll' }
)
# Internal builds are handed to testers without a public release note, so stamp what is inside:
# commit, build time and the two DLL hashes are what we ask for when a report comes back.
$gitCommit = 'unknown'
try { $gitCommit = (git -C $root rev-parse --short HEAD 2>$null) } catch { }
$buildStamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'
$serverDll = Join-Path $publishDir 'MikuGameServer.dll'
$serverHash = if (Test-Path $serverDll) { (Get-FileHash $serverDll -Algorithm SHA256).Hash.ToLowerInvariant() } else { 'missing' }
# Report what tools/test.ps1 actually observed instead of a hard-coded sentence: a package that
# claims a green run it never had is worse than one that says "not run". Missing or failed results
# only warn - packaging must stay possible without a server or a client - but the file never lies.
$testLine = 'tests at build time: not run (run tools/test.ps1)'
$testResults = Join-Path $outDir 'test-results.json'
if (Test-Path $testResults) {
    try {
        $tr = Get-Content $testResults -Raw | ConvertFrom-Json
        $age = (Get-Date) - [datetime]::Parse($tr.runAt)
        $stale = if ($age.TotalHours -gt 24) { ' STALE' } else { '' }
        $ran = ($tr.steps | Where-Object { $_.status -ne 'skip' } | ForEach-Object { "$($_.name)=$($_.status)" }) -join ', '
        # test.ps1 writes a culture-dependent runAt; BUILD-INFO is read by people in other locales.
        $ranAt = try { ([datetime]::Parse($tr.runAt)).ToString('yyyy-MM-dd HH:mm:ss') } catch { $tr.runAt }
        $testLine = "tests at build time: $($tr.passed) passed, $($tr.failed) failed, $($tr.skipped) skipped [$ran]$stale, run $ranAt @ $($tr.commit)"
        if ($tr.failed -gt 0) { Write-Warning "The last test run had $($tr.failed) failure(s): $testLine" }
    } catch {
        $testLine = 'tests at build time: unreadable test-results.json'
    }
} else {
    Write-Warning 'No build/test-results.json: BUILD-INFO.txt will say the tests were not run.'
}
@(
    "MikuSB internal build",
    "built:      $buildStamp",
    "commit:     $gitCommit",
    "version:    $((Get-Content (Join-Path $root 'version.txt') -Raw).Trim())",
    "server dll: $serverHash",
    "patch dlls: see Patch/ (bundled from the local MikuSB-Patch build)",
    $testLine
) | Set-Content (Join-Path $publishDir 'BUILD-INFO.txt') -Encoding utf8

$patchDir = Join-Path $publishDir 'Patch'
New-Item -ItemType Directory -Force -Path $patchDir | Out-Null
$bundledPatch = 0
foreach ($p in $patchSources) {
    if (Test-Path $p.From) {
        Copy-Item $p.From (Join-Path $patchDir $p.To) -Force
        $hashLine = (Get-FileHash $p.From -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-Host ("=== bundled Patch/{0} ({1})" -f $p.To, $hashLine) -ForegroundColor Green
        $bundledPatch++
    } else {
        Write-Warning ("Patch source not found, package will depend on the download: {0}" -f $p.From)
    }
}
if ($bundledPatch -eq 0) {
    Write-Host '=== WARNING: no patch DLL bundled; PatchDownloadService must succeed at runtime.' -ForegroundColor Yellow
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
    "$hash  $OutName.zip" | Set-Content "$zipPath.sha256"
    Write-Host ("=== zip: {0} ({1:N1} MB)" -f $zipPath, ((Get-Item $zipPath).Length / 1MB)) -ForegroundColor Green
    Write-Host "=== sha256: $hash"
}

# Local dev builds read the resource tables from the checkout instead of carrying a second copy:
# a directory junction (no admin rights needed, unlike a symlink) keeps one 323 MB tree on disk and
# the server resolves Resources/ relative to its working directory either way. Created after
# packaging so the zip never ships the payload, and skipped for release names.
if ($OutName -like '*dev*') {
    $resSrc = Join-Path $root 'Resources'
    $resDst = Join-Path $publishDir 'Resources'
    if (-not (Test-Path $resSrc)) {
        Write-Warning "no Resources link: $resSrc does not exist"
    } elseif (Test-Path $resDst) {
        $existing = Get-Item $resDst -Force
        if ($existing.LinkType) {
            Write-Host ("=== Resources already linked -> {0}" -f $existing.Target) -ForegroundColor Green
        } else {
            Write-Warning "no Resources link: $resDst exists as a real directory"
        }
    } else {
        New-Item -ItemType Junction -Path $resDst -Target $resSrc | Out-Null
        Add-Content (Join-Path $publishDir 'BUILD-INFO.txt') 'resources:  linked to the checkout Resources/ (dev build, not copied)'
        Write-Host ("=== linked Resources -> {0}" -f $resSrc) -ForegroundColor Green
    }
}

exit 0
