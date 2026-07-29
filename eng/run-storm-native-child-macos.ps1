#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

[CmdletBinding()]
param(
    [ValidateRange(2, 10000)]
    [int]$SwitchCount = 100,
    [ValidateRange(1, 86400)]
    [int]$SurvivalSeconds = 90,
    [ValidateSet('build', 'archive')]
    [string]$NativeSource = 'build'
)

$ErrorActionPreference = 'Stop'
if (-not $IsMacOS -or
    [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne
        [System.Runtime.InteropServices.Architecture]::Arm64)
{
    throw 'The macOS Storm native-child smoke requires an Apple Silicon host.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$outputRoot = Join-Path $repoRoot 'artifacts/storm-native-child-macos'
$viewerRoot = Join-Path $outputRoot 'viewer'
Remove-Item $outputRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$nativeProbeLog = Join-Path $outputRoot 'native-storm-child.log'
if ($NativeSource -eq 'build')
{
    & ctest `
        --test-dir (Join-Path $repoRoot 'native/build/shim/osx-arm64') `
        --verbose `
        -R openusd_storm_child_probe 2>&1 |
        Tee-Object $nativeProbeLog
}
else
{
    $openUsdRoot = Join-Path $repoRoot 'native/install/osx-arm64'
    $shimRoot = Join-Path $repoRoot 'native/install/shim/osx-arm64'
    $probe = Join-Path $shimRoot 'bin/openusd_storm_child_probe'
    if (-not (Test-Path $probe -PathType Leaf))
    {
        throw "The osx-arm64 native archive omitted the installed Storm child probe: $probe"
    }
    $probeRoot = Join-Path $outputRoot 'native-probe'
    $pluginRoot = Join-Path $probeRoot 'plugin/usd'
    New-Item -ItemType Directory -Force -Path $pluginRoot | Out-Null
    foreach ($source in @(
        (Join-Path $openUsdRoot 'lib/usd'),
        (Join-Path $openUsdRoot 'plugin/usd'),
        (Join-Path $shimRoot 'plugin/usd')))
    {
        if (Test-Path $source)
        {
            Copy-Item (Join-Path $source '*') $pluginRoot -Recurse -Force
        }
    }
    $oldDyldLibraryPath = $env:DYLD_LIBRARY_PATH
    try
    {
        $env:DYLD_LIBRARY_PATH = @(
            (Join-Path $shimRoot 'lib'),
            (Join-Path $openUsdRoot 'lib'),
            $oldDyldLibraryPath
        ) -join ':'
        & $probe `
            $pluginRoot `
            (Join-Path $repoRoot 'test-assets/minimal.usda') 2>&1 |
            Tee-Object $nativeProbeLog
    }
    finally
    {
        $env:DYLD_LIBRARY_PATH = $oldDyldLibraryPath
    }
}
if ($LASTEXITCODE -ne 0)
{
    throw "The macOS native Storm child probe failed with $LASTEXITCODE."
}
$nativeProbe = Get-Content $nativeProbeLog -Raw
# The probe reports an unusable graphics environment by skipping, and CTest records
# that as a skip rather than a failure, so the exit code above is zero. Every gate
# below needs the same accelerated OpenGL 4.1 core context the probe just said is
# unavailable, so demanding its pass evidence here would contradict the probe's own
# capability report. A hosted macOS runner has no such context.
if ($nativeProbe -match 'Skipping Storm child probe: ')
{
    Write-Host (
        'Skipping the macOS Storm native-child evidence: the probe reported that an ' +
        'accelerated OpenGL 4.1 core context is unavailable on this host.')
    New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
    $skipped = [ordered]@{
        schemaVersion = 1
        status = 'skipped'
        completedAt = [DateTimeOffset]::UtcNow.ToString('O')
        platform = 'macos-15'
        reason = 'An accelerated OpenGL 4.1 core context is unavailable on this host.'
        nativeProbe = $nativeProbeLog
    }
    $skipped |
        ConvertTo-Json -Depth 4 |
        Set-Content (Join-Path $outputRoot 'storm-metal-switching.json')
    return
}

foreach ($pattern in @(
    'Storm macOS native child probe passed',
    'initialHash=\d+',
    'editedHash=\d+',
    'rendererName=Storm / Metal \+ OpenGL 4\.1 core presentation',
    'resizeGeneration=[1-9]\d*',
    'contextUpdateGeneration=[1-9]\d*',
    'renderedResizeGeneration=[1-9]\d*',
    'recoveryGeneration=[1-9]\d*',
    'rendererContextGeneration=[1-9]\d*',
    'firstRecoveryFrameContextGeneration=[1-9]\d*',
    'navigationSequence=[1-9]\d*',
    'navigationCommands=2/2/4',
    'scrollPointsPerStep=40',
    'contextGeneration=2'))
{
    if ($nativeProbe -notmatch $pattern)
    {
        throw "The macOS native probe omitted required evidence: $pattern"
    }
}

& (Join-Path $PSScriptRoot 'run-viewer.ps1') `
    -Rid osx-arm64 `
    -OutputPath $viewerRoot `
    -RendererSwitchSoak `
    -SwitchCount $SwitchCount `
    -SwitchSoakSeconds $SurvivalSeconds `
    -SimulateStormContextLoss 2>&1 |
    Tee-Object (Join-Path $outputRoot 'switching.log')
if ($LASTEXITCODE -ne 0)
{
    throw "The macOS Storm/Metal switching smoke exited with $LASTEXITCODE."
}

$statusPath = Join-Path $viewerRoot 'viewer-status.txt'
if (-not (Test-Path $statusPath))
{
    throw "The Viewer did not write $statusPath."
}
$status = Get-Content $statusPath -Raw
foreach ($pattern in @(
    "Viewer switch soak passed: switches=$SwitchCount;",
    'compositionFrames=\d+;',
    'compositionDraws=[1-9]\d*;',
    'VIEWER_METAL_HDSILK_READY',
    'Metal hdSilk frame: revision=\d+; draws=[1-9]\d*; triangles=[1-9]\d*;',
    'Storm native child initialized on Storm / Metal \+ OpenGL 4\.1 core presentation\.',
    'Storm native child frame:',
    'gl=4\.1-core;',
    'Viewer final resources: child=0;.*managedSilk=0; nativeSilk=0; ' +
        'managedPages=0; nativePages=0; gpuScenes=0; gpuMeshes=0;.*abandonedStorm=1'))
{
    if ($status -notmatch $pattern)
    {
        throw "The macOS switching status omitted required evidence: $pattern"
    }
}
if ($status -match 'STAGE_DRAW_BLOCKED')
{
    throw 'The macOS switching run reported blocked stage drawing.'
}

$summary = [ordered]@{
    schemaVersion = 1
    status = 'passed'
    completedAt = [DateTimeOffset]::UtcNow.ToString('O')
    platform = 'macos-15'
    shell = 'Avalonia Metal'
    stormChild = 'NSView + application-owned NSOpenGLContext 4.1 core'
    stormRenderer = 'Storm / Metal + OpenGL 4.1 core presentation'
    metalComposition = 'IOSurfaceRef + MetalSharedEvent'
    switchCount = $SwitchCount
    survivalSeconds = $SurvivalSeconds
    nativeProbe = $nativeProbeLog
    gates = @(
        'native first/edit/preserved capture',
        'actual openusd_hydra renderer is Storm / Metal',
        'Cocoa input and main-thread ownership',
        'ABI-7 native navigation snapshots, picking, and command counters',
        'concurrent resize/context-update ordering and context recreation',
        'serialized staged recovery and first recovered frame',
        'scheduler-bound hdSilk Metal stage draws and triangles',
        'no blocked diagnostics',
        'zero final native and Silk resources')
    viewerStatus = $statusPath
}
$summaryPath = Join-Path $outputRoot 'storm-metal-switching.json'
$summary | ConvertTo-Json -Depth 4 | Set-Content $summaryPath
Write-Output "macOS Storm/Metal switching smoke passed: $summaryPath"
