#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$Project,

    [string[]]$Framework = @('net10.0'),

    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid = 'win-x64',

    [ValidateNotNullOrEmpty()]
    [string]$Configuration = 'Release',

    [ValidateRange(1, [int]::MaxValue)]
    [int]$MinimumExpectedTests = 1,

    [string[]]$TestArguments = @()
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$openUsdRoot = Join-Path $repoRoot "native/install/$Rid"
$shimRoot = Join-Path $repoRoot "native/install/shim/$Rid"
$physxShimRoot = Join-Path $repoRoot "native/install/shim/$Rid-physx"
# Every run stages into a directory of its own rather than a single shared one per RID. A shared
# root made each run begin by deleting a tree another run - or a probe sweep reading the same
# staged bin - was still using, which is the single cause behind a long tail of phantom results:
# mass skips that still report "Passed", DllNotFoundException bursts, and lone failures that never
# reproduce in isolation. Owning the directory removes the interference entirely instead of
# scheduling around it, so suites can run concurrently again.
$stageRoot = Join-Path $repoRoot "artifacts/native-managed-tests/$Rid-$PID"

function Assert-Directory
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container))
    {
        throw (
            "$Description was not found at '$Path'. Restore the verified native cache from " +
            '.github/workflows/native.yml or build the fast shim/native install before running native managed tests.')
    }
}

Assert-Directory -Path $openUsdRoot -Description 'OpenUSD native runtime'
Assert-Directory -Path $shimRoot -Description 'OpenUsd native shim'

# Staging still copies FROM the shared install prefixes, which a concurrent `cmake --install` can be
# rewriting, so the copy phase alone is serialised. The lock is released as soon as the stage is
# assembled rather than held across the run, so the long test execution stays parallel - the
# per-process stage root above is what keeps concurrent runs from touching each other's files.
$stageLock = New-Object System.Threading.Mutex($false, "Local\OpenUsdNativeManagedTests-$Rid")
$stageLockHeld = $false
try
{
    # An abandoned mutex means a previous run died holding it. Nothing is inherited except the
    # right to copy, so ownership is simply taken rather than treated as an error.
    $stageLockHeld = $stageLock.WaitOne([TimeSpan]::FromMinutes(60))
}
catch [System.Threading.AbandonedMutexException]
{
    $stageLockHeld = $true
}

if (-not $stageLockHeld)
{
    throw (
        "Timed out waiting for the native managed test stage lock for '$Rid'. Another suite has " +
        'held it for over an hour; stop that run before starting this one.')
}

# A run that was killed cannot clean up after itself, so its stage is reclaimed here. Only
# directories whose owning process is gone are removed, which leaves concurrent runs untouched.
Get-ChildItem -LiteralPath (Split-Path -Parent $stageRoot) -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match "^$([regex]::Escape($Rid))-(\d+)$" } |
    ForEach-Object {
        $ownerId = [int]$Matches[1]
        if ($ownerId -ne $PID -and -not (Get-Process -Id $ownerId -ErrorAction SilentlyContinue))
        {
            Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

if (Test-Path -LiteralPath $stageRoot)
{
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}

$binTarget = Join-Path $stageRoot 'bin'
$libTarget = Join-Path $stageRoot 'lib'
$pluginPath = Join-Path $stageRoot 'plugin/usd'
$workRoot = Join-Path $stageRoot 'work'
New-Item -ItemType Directory -Force -Path $binTarget, $libTarget, $pluginPath, $workRoot | Out-Null

# A stale copy of a project shim can also sit in the OpenUSD install prefix, so the OpenUSD
# runtime is staged first and the project shims are staged last. The last copy wins, which
# keeps the freshly built shim authoritative no matter what an older install left behind.
foreach ($layout in @(
    @{ Source = (Join-Path $openUsdRoot 'bin'); Target = $binTarget },
    @{ Source = (Join-Path $openUsdRoot 'lib'); Target = $libTarget },
    @{ Source = (Join-Path $shimRoot 'bin'); Target = $binTarget },
    @{ Source = (Join-Path $shimRoot 'lib'); Target = $libTarget },
    @{ Source = (Join-Path $physxShimRoot 'bin'); Target = $binTarget },
    @{ Source = (Join-Path $physxShimRoot 'lib'); Target = $libTarget }))
{
    if (Test-Path -LiteralPath $layout.Source -PathType Container)
    {
        Get-ChildItem -LiteralPath $layout.Source -File |
            Where-Object { $_.Name -match '\.dll$|\.dylib$|\.so($|\.)' } |
            Copy-Item -Destination $layout.Target -Force
    }
}

foreach ($source in @(
    (Join-Path (Join-Path $openUsdRoot 'lib') 'usd'),
    (Join-Path (Join-Path $openUsdRoot 'plugin') 'usd'),
    (Join-Path (Join-Path $shimRoot 'plugin') 'usd')))
{
    if (Test-Path -LiteralPath $source -PathType Container)
    {
        Copy-Item -Path (Join-Path $source '*') -Destination $pluginPath -Recurse -Force
    }
}

if (-not (Get-ChildItem -LiteralPath $pluginPath -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1))
{
    throw "No OpenUSD plugins were staged at '$pluginPath'. Native managed tests cannot open schema-backed stages."
}

$oldPath = $env:PATH
$oldLdLibraryPath = $env:LD_LIBRARY_PATH
$oldDyldLibraryPath = $env:DYLD_LIBRARY_PATH
$oldPluginPath = $env:OPENUSD_TEST_PLUGIN_PATH
$oldWorkRoot = $env:OPENUSD_TEST_WORK_ROOT
$oldRequirePhysics = $env:OPENUSD_REQUIRE_NATIVE_PHYSICS

# A staged physics runtime is a contract, not a hint: when the shim was copied into the stage the
# native-backed physics tests must execute rather than skip, so they fail loudly if the runtime
# they were told to exercise cannot be loaded.
#
# Both stage directories are scanned because the install layout is platform dependent: a Windows
# build installs the shim as a runtime artifact into 'bin', while a Linux or macOS build installs
# the same shim as a library artifact into 'lib'. Scanning only 'bin' silently reported "no physics
# runtime" on every non-Windows host, which turned the whole native-backed physics suite into
# skips instead of failures.
$physicsModulePattern = '^(lib)?openusd_physx\.(dll|dylib|so)'
$stagedPhysicsFiles = @(
    Get-ChildItem -LiteralPath $binTarget, $libTarget -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match $physicsModulePattern })
$stagedPhysics = $stagedPhysicsFiles.Count -gt 0

# The optional CUDA modules are loaded late, by name, from the directory the physics runtime lives
# in, so they are placed beside whichever stage directory received the shim rather than left in the
# one the install layout happened to use.
if ($stagedPhysics)
{
    $physicsHomes = @($stagedPhysicsFiles | ForEach-Object { $_.DirectoryName } | Sort-Object -Unique)
    foreach ($physicsHome in $physicsHomes)
    {
        foreach ($candidate in @($binTarget, $libTarget))
        {
            if ($candidate -eq $physicsHome)
            {
                continue
            }
            Get-ChildItem -LiteralPath $candidate -File -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match '^(lib)?PhysX(Gpu|Device)' } |
                Copy-Item -Destination $physicsHome -Force
        }
    }
}

# The stage is fully assembled, so the copy lock is handed on before the long part of the run.
if ($stageLockHeld)
{
    $stageLock.ReleaseMutex()
    $stageLockHeld = $false
}
try
{
    $env:PATH = $binTarget + [System.IO.Path]::PathSeparator +
        $libTarget + [System.IO.Path]::PathSeparator +
        $stageRoot + [System.IO.Path]::PathSeparator + $oldPath
    $env:LD_LIBRARY_PATH = $libTarget + [System.IO.Path]::PathSeparator +
        $binTarget + [System.IO.Path]::PathSeparator + $oldLdLibraryPath
    $env:DYLD_LIBRARY_PATH = $libTarget + [System.IO.Path]::PathSeparator +
        $binTarget + [System.IO.Path]::PathSeparator + $oldDyldLibraryPath
    $env:OPENUSD_TEST_PLUGIN_PATH = $pluginPath
    $env:OPENUSD_TEST_WORK_ROOT = $workRoot
    $env:OPENUSD_REQUIRE_NATIVE_PHYSICS = if ($stagedPhysics) { '1' } else { '0' }

    & (Join-Path $PSScriptRoot 'run-managed-tests.ps1') `
        -Project $Project `
        -Framework $Framework `
        -Configuration $Configuration `
        -MinimumExpectedTests $MinimumExpectedTests `
        -TestArguments $TestArguments 2>&1 | Tee-Object -Variable stageRunOutput
    $stageExitCode = $LASTEXITCODE

    # A staged runtime that mass-skips is the most dangerous failure this script can produce: the
    # run reports "Passed" with zero failures while silently exercising almost nothing, and a green
    # summary is exactly what nobody looks at twice. -MinimumExpectedTests cannot catch it because
    # a skipped test still counts toward the total. The proportion is used rather than a fixed
    # ceiling so the rule scales with the suite: a healthy run skips a handful of cases by design
    # (4 of 590 here), while a stage that lost its native runtime skips a fifth of everything.
    #
    # The threshold has headroom over every legitimate skip population, and the numbers are
    # recorded here so a later change cannot invalidate it silently. The largest such population is
    # the 41 bake-capability tests gated on BakeFixture.SkipIfUnavailable, spread across
    # UsdPhysicsBakerTests (22), UsdPhysicsPreviewApplierTests (11) and UsdPhysicsBakeRegressionTests
    # (8): even with that capability entirely absent it is 45 of 590, or 7.6%, which does not fire.
    #
    # Read the headroom as a COUNT, not as that percentage. A newly added skippable test raises both
    # sides of the ratio, so the margin closes far faster than 7.6% suggests: solving
    # (45 + n) * 10 > (590 + n) gives n = 16. Sixteen more skippable tests - one ordinary batch of
    # bake or preview-applier coverage - and the worst legitimate case starts failing. The failure
    # would land on whoever ran the suite, not whoever added the tests, and would blame a corrupted
    # stage, so re-check this count when growing any capability-gated suite. Platform-conditional
    # skips are a distant second today (the macOS-only Metal lifecycle tests), but a large
    # Windows-only Storm or D3D12 block would make gating this on platform necessary too.
    if ($stageExitCode -eq 0 -and $stagedPhysics)
    {
        $totalTests = 0
        $skippedTests = 0
        foreach ($line in @($stageRunOutput))
        {
            $text = [string]$line
            if ($text -match '^\s*total:\s*(\d+)\s*$')
            {
                $totalTests += [int]$Matches[1]
            }
            elseif ($text -match '^\s*skipped:\s*(\d+)\s*$')
            {
                $skippedTests += [int]$Matches[1]
            }
        }

        if ($skippedTests -ge 10 -and $totalTests -gt 0 -and ($skippedTests * 10) -gt $totalTests)
        {
            throw (
                "The native runtime was staged for '$Rid', but $skippedTests of $totalTests tests " +
                'skipped instead of running. That is the signature of a stage this run could not ' +
                'load rather than a real result, so it is reported as a failure instead of a ' +
                'green summary. Re-run once nothing else is using the stage.')
        }
    }

    exit $stageExitCode
}
finally
{
    $env:PATH = $oldPath
    $env:LD_LIBRARY_PATH = $oldLdLibraryPath
    $env:DYLD_LIBRARY_PATH = $oldDyldLibraryPath
    $env:OPENUSD_TEST_PLUGIN_PATH = $oldPluginPath
    $env:OPENUSD_TEST_WORK_ROOT = $oldWorkRoot
    $env:OPENUSD_REQUIRE_NATIVE_PHYSICS = $oldRequirePhysics

    if ($stageLockHeld)
    {
        $stageLock.ReleaseMutex()
        $stageLockHeld = $false
    }
    $stageLock.Dispose()

    # This run owns its stage outright, so removing it cannot disturb anyone else. Best effort:
    # a file still held by a crashed test host must not turn a real result into a failure.
    Remove-Item -LiteralPath $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
}
