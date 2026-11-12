# One-command test runner for the MikuSB dev tree.
#
# Runs the layers that do not need a human at the keyboard, in the order the contract requires,
# and reports a single exit code plus a machine-readable summary (build/test-results.json, which
# build.ps1 stamps into BUILD-INFO.txt so a package can never claim a test run that did not happen).
#
#   gate    tools/check_client_patches.py   every shipped payload must be decodable by the client
#   L1      tests/EraAudit                  378 policy/payload assertions against the real generators
#   L1.5    tests/EraAudit/lua/run.js       56 assertions that execute the emitted payloads
#   L2      tests/EraSmoke                  login-push assertions against a live server (optional)
#
# Usage:
#   pwsh -File tools/test.ps1                     # gate + L1 + L1.5; L2 when 127.0.0.1:21000 is up
#   pwsh -File tools/test.ps1 -SkipL2
#   pwsh -File tools/test.ps1 -Token <comboToken> -L2Seconds 15
#   pwsh -File tools/test.ps1 -OutJson build/test-results.json
#
# Exit: 0 when every executed step passed (skips are reported, not failed), 1 when any step failed.
# A skipped step is never silent: it prints the reason and is recorded in the JSON.

param(
    [string]$Configuration = 'Release',
    [switch]$SkipGate,
    [switch]$SkipLua,
    [switch]$SkipL2,
    # Also check the MIKU_CAP lines of the newest login against tools/caps.json. Off by default:
    # it reads Game.log, so it only means something right after a client session.
    [switch]$Caps,
    [int]$L2Seconds = 12,
    # ComboToken for L2. When empty it is read from Config/Database/Miku.db (Account.ComboToken).
    [string]$Token,
    [string]$OutJson
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutJson) { $OutJson = Join-Path $root 'build\test-results.json' }

class StepSkipped : System.Exception {
    StepSkipped([string]$message) : base($message) { }
}

$results = [System.Collections.Generic.List[object]]::new()
$overall = [System.Diagnostics.Stopwatch]::StartNew()

function Add-Result([string]$Name, [string]$Status, [string]$Detail, [double]$Seconds) {
    $results.Add([pscustomobject]@{ name = $Name; status = $Status; detail = $Detail; seconds = [math]::Round($Seconds, 1) })
    $color = switch ($Status) { 'pass' { 'Green' } 'fail' { 'Red' } 'skip' { 'Yellow' } default { 'Gray' } }
    Write-Host ('[{0,-4}] {1,-14} {2}' -f $Status.ToUpper(), $Name, $Detail) -ForegroundColor $color
}

function Invoke-Step([string]$Name, [scriptblock]$Body) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $detail = & $Body
        $sw.Stop()
        Add-Result $Name 'pass' ([string]$detail) $sw.Elapsed.TotalSeconds
    } catch [StepSkipped] {
        $sw.Stop()
        Add-Result $Name 'skip' $_.Exception.Message $sw.Elapsed.TotalSeconds
    } catch {
        $sw.Stop()
        Add-Result $Name 'fail' $_.Exception.Message $sw.Elapsed.TotalSeconds
    }
}

function Test-Port([int]$Port, [string]$Target = '127.0.0.1') {
    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $task = $client.ConnectAsync($Target, $Port)
        return $task.Wait(500) -and $client.Connected
    } catch {
        return $false
    } finally {
        $client.Dispose()
    }
}

function Find-Fengari {
    $candidates = @(
        (Join-Path $root 'node_modules\fengari'),
        (Join-Path $root 'tests\EraAudit\node_modules\fengari'),
        (Join-Path $root '..\..\.temp\luarun\node_modules\fengari'),
        (Join-Path $root '..\..\.temp\luacheck\node_modules\fengari')
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { return (Split-Path -Parent $c) }
    }
    return $null
}

function Get-ComboToken {
    $db = Join-Path $root 'Config\Database\Miku.db'
    if (-not (Test-Path $db)) { return $null }
    if (-not (Get-Command python -ErrorAction SilentlyContinue)) { return $null }
    try {
        $code = "import sqlite3,sys;c=sqlite3.connect(sys.argv[1]);r=c.execute('select ComboToken from Account where ComboToken is not null order by Uid desc limit 1').fetchone();print(r[0] if r else '')"
        $value = & python -c $code $db 2>$null
        if ($LASTEXITCODE -eq 0 -and $value) { return ([string]$value).Trim() }
    } catch { }
    return $null
}

# Prefer the portable SDK; fall back to whatever `dotnet` resolves to. The portable SDK here is
# missing the workload locator SDKs, so every project evaluation fails with MSB4276 and the MSBuild
# worker nodes die while the parent only reports MSB4186; single-node with the resolver disabled
# avoids it (same workaround build.ps1 uses).
$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
$dotnetArgs = @()
if (Test-Path $dotnet) {
    $dotnetArgs = @('-m:1', '-nodeReuse:false', '-p:MSBuildEnableWorkloadResolver=false')
} else {
    $dotnet = 'dotnet'
}

$emitDir = Join-Path $root 'build\eraaudit-out'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutJson) | Out-Null

Write-Host "=== MikuSB tests ($Configuration) - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Cyan

# --- gate -------------------------------------------------------------------------------------
if ($SkipGate) {
    Add-Result 'gate' 'skip' 'disabled by -SkipGate' 0
} else {
    Invoke-Step 'gate' {
        if (-not (Get-Command python -ErrorAction SilentlyContinue)) {
            throw 'python not found; the payload gate cannot run (install Python or pass -SkipGate)'
        }
        $out = & python (Join-Path $root 'tools\check_client_patches.py') 2>&1
        $code = $LASTEXITCODE
        $tail = [string]($out | Select-Object -Last 1)
        if ($code -ne 0) { throw "exit $code`: $tail" }
        $tail
    }
}

# --- L1: policy/payload assertions on the real generators --------------------------------------
Invoke-Step 'L1 EraAudit' {
    $proj = Join-Path $root 'tests\EraAudit\EraAudit.csproj'
    $buildOut = & $dotnet build $proj -c $Configuration @dotnetArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "build failed: $((@($buildOut) | Select-Object -Last 3) -join ' | ')"
    }
    $dll = Join-Path $root "tests\EraAudit\bin\$Configuration\net10.0\EraAudit.dll"
    if (-not (Test-Path $dll)) { throw "missing $dll" }
    $out = & $dotnet $dll --emit $emitDir 2>&1
    $code = $LASTEXITCODE
    $line = [string](@($out) | Where-Object { $_ -match 'check\(s\) passed' } | Select-Object -Last 1)
    if ($code -ne 0) { if ($line) { throw $line } else { throw "exit $code" } }
    $line
}

# --- L1.5: execute the emitted payloads in a Lua VM --------------------------------------------
if ($SkipLua) {
    Add-Result 'L1.5 payload Lua' 'skip' 'disabled by -SkipLua' 0
} else {
    Invoke-Step 'L1.5 payload Lua' {
        if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw 'node not found' }
        $fengari = Find-Fengari
        if (-not $fengari) {
            throw 'fengari not found (looked in node_modules, tests/EraAudit/node_modules, .temp/luarun/node_modules)'
        }
        $runJs = Join-Path $root 'tests\EraAudit\lua\run.js'
        # Absolute paths only: run.js does require(path.join(fengari, 'fengari')), and a relative
        # path is resolved as a module name, which fails with MODULE_NOT_FOUND.
        $out = & node $runJs --out $emitDir --fengari $fengari 2>&1
        $code = $LASTEXITCODE
        $line = [string](@($out) | Where-Object { $_ -match 'check\(s\) passed' } | Select-Object -Last 1)
        if ($code -ne 0) { if ($line) { throw $line } else { throw "exit $code" } }
        $line
    }
}

# --- L2: login-push assertions against a running server ----------------------------------------
if ($SkipL2) {
    Add-Result 'L2 EraSmoke' 'skip' 'disabled by -SkipL2' 0
} else {
    Invoke-Step 'L2 EraSmoke' {
        if (-not (Test-Port 21000)) {
            throw [StepSkipped]::new('no server on 127.0.0.1:21000 (start it, or pass -SkipL2)')
        }
        if (-not $Token) { $Token = Get-ComboToken }
        if (-not $Token) {
            throw [StepSkipped]::new('no ComboToken (pass -Token, or seed Config/Database/Miku.db)')
        }

        # EraSmoke links the generated protobuf types from the launcher build, so that assembly has
        # to exist before the smoke project can compile.
        $proto = Join-Path $root "MikuSB\bin\$Configuration\net10.0\MikuProto.dll"
        if (-not (Test-Path $proto)) {
            Write-Host '    (building MikuSB once so EraSmoke can reference MikuProto.dll)' -ForegroundColor DarkGray
            $sb = & $dotnet build (Join-Path $root 'MikuSB\MikuSB.csproj') -c $Configuration @dotnetArgs 2>&1
            if ($LASTEXITCODE -ne 0) { throw "MikuSB build failed: $((@($sb) | Select-Object -Last 3) -join ' | ')" }
        }

        $proj = Join-Path $root 'tests\EraSmoke\EraSmoke.csproj'
        $buildOut = & $dotnet build $proj -c $Configuration @dotnetArgs 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "build failed: $((@($buildOut) | Select-Object -Last 3) -join ' | ')"
        }
        $dll = Join-Path $root "tests\EraSmoke\bin\$Configuration\net10.0\era-smoke.dll"
        if (-not (Test-Path $dll)) { throw "missing $dll" }

        $out = & $dotnet $dll $Token $L2Seconds 2>&1
        $code = $LASTEXITCODE
        $passLine = [string](@($out) | Where-Object { $_ -match 'all checks passed' } | Select-Object -Last 1)
        $failLine = [string](@($out) | Where-Object { $_ -match 'FAIL' } | Select-Object -First 1)
        if ($code -ne 0) { if ($failLine) { throw $failLine } else { throw "exit $code" } }
        if (-not $passLine) { throw 'no summary line from era-smoke' }
        $passLine
    }
}

# --- optional: the client's own view of the session (needs a fresh Game.log) --------------------
if ($Caps) {
    Invoke-Step 'caps contract' {
        if (-not (Get-Command python -ErrorAction SilentlyContinue)) { throw 'python not found' }
        $out = & python (Join-Path $root 'tools\devlog.py') cap --expect (Join-Path $root 'tools\caps.json') 2>&1
        $code = $LASTEXITCODE
        $tail = [string](@($out) | Select-Object -Last 1)
        if ($code -ne 0) {
            $bad = (@($out) | Where-Object { $_ -match '^FAIL|^WARN' }) -join ' | '
            if ($bad) { throw $bad } else { throw "exit $code" }
        }
        $tail
    }
}

# --- summary -----------------------------------------------------------------------------------
$overall.Stop()
$failed = @($results | Where-Object { $_.status -eq 'fail' })
$skipped = @($results | Where-Object { $_.status -eq 'skip' })
$passed = @($results | Where-Object { $_.status -eq 'pass' })

$commit = 'unknown'
try { $commit = (git -C $root rev-parse --short HEAD 2>$null) } catch { }

$summary = [pscustomobject]@{
    runAt         = (Get-Date).ToString('o')
    commit        = $commit
    configuration = $Configuration
    passed        = $passed.Count
    failed        = $failed.Count
    skipped       = $skipped.Count
    seconds       = [math]::Round($overall.Elapsed.TotalSeconds, 1)
    steps         = $results
}
$summary | ConvertTo-Json -Depth 6 | Set-Content $OutJson -Encoding utf8

Write-Host ''
Write-Host ('=== {0} passed, {1} failed, {2} skipped in {3:N1}s' -f $passed.Count, $failed.Count, $skipped.Count, $overall.Elapsed.TotalSeconds) -ForegroundColor $(if ($failed.Count) { 'Red' } else { 'Green' })
Write-Host "=== results: $OutJson"

if ($failed.Count -gt 0) { exit 1 }
exit 0
