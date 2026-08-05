#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

[CmdletBinding()]
param(
    [ValidateRange(60, 300)]
    [int]$SmokeSeconds = 180,
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows)
{
    throw 'The short Viewer picking smoke requires Windows.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$root = if ([string]::IsNullOrWhiteSpace($OutputRoot))
{
    Join-Path $repoRoot 'artifacts/viewer-picking-smoke'
}
else
{
    [IO.Path]::GetFullPath($OutputRoot)
}
$runtimeRoot = Join-Path $root 'runtime'
$evidencePath = Join-Path $root 'picking-smoke.json'
$summaryPath = Join-Path $root 'picking-smoke-summary.json'
$stagePath = Join-Path $repoRoot 'test-assets/minimal.usda'
$runViewer = Join-Path $PSScriptRoot 'run-viewer.ps1'
$nativeRuntime = Join-Path $repoRoot (
    'native/build/shim/win-x64/openusd_storm_child/tests/' +
    'storm-child-runtime/bin')
foreach ($requiredNative in @('openusd_storm_child.dll', 'openusd_hydra.dll'))
{
    if (-not (Test-Path (Join-Path $nativeRuntime $requiredNative)))
    {
        throw "Build the finalized native Storm probes before the short smoke: $requiredNative"
    }
}

Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $root | Out-Null
$oldRenderer = $env:OPENUSD_RENDERER
try
{
    $env:OPENUSD_RENDERER = 'Storm'
    & $runViewer `
        -Rid win-x64 `
        -StagePath $stagePath `
        -SmokeSeconds $SmokeSeconds `
        -OutputPath $runtimeRoot `
        -PickSmokeEvidencePath $evidencePath `
        -NativeRuntimeOverridePath $nativeRuntime
    if ($LASTEXITCODE -ne 0)
    {
        throw "Viewer picking smoke exited with $LASTEXITCODE."
    }
}
finally
{
    $env:OPENUSD_RENDERER = $oldRenderer
}

$artifact = Get-Content $evidencePath -Raw | ConvertFrom-Json -Depth 12
if ([int]$artifact.schemaVersion -ne 3 -or
    [string]$artifact.scenario -cne 'viewer-picking-short-smoke' -or
    [string]::IsNullOrWhiteSpace([string]$artifact.commonHitPath) -or
    -not [bool]$artifact.stormClickHit -or
    -not [bool]$artifact.stormClickMiss -or
    [long]$artifact.staleRetries -ne 1 -or
    -not [bool]$artifact.selectionPreservedAcrossSwitches -or
    -not [bool]$artifact.hostPickHitObserved -or
    -not [bool]$artifact.hostPickMissObserved -or
    -not [bool]$artifact.hostSelectionHitObserved -or
    -not [bool]$artifact.hostSelectionClearObserved -or
    -not [bool]$artifact.stormHighlightChanged -or
    -not [bool]$artifact.stormHighlightCleared)
{
    throw 'Viewer picking smoke summary fields are incomplete or invalid.'
}

$silkOutlines = @($artifact.silkOutlines)
$expectedSilk = @('D3D12', 'Vulkan')
if ($silkOutlines.Count -ne $expectedSilk.Count)
{
    throw 'Viewer picking smoke did not record both Silk selection outlines.'
}
foreach ($backend in $expectedSilk)
{
    $outline = @($silkOutlines | Where-Object {
        [string]$_.backend -ceq $backend
    })
    if ($outline.Count -ne 1 -or
        [string]$outline[0].selectedStatus -cne 'Rendered' -or
        [ulong]$outline[0].maskPasses -eq 0 -or
        [ulong]$outline[0].outlinePasses -eq 0 -or
        [ulong]$outline[0].selectedDraws -eq 0 -or
        [string]$outline[0].clearedStatus -cne 'EmptySelection' -or
        -not [bool]$outline[0].clearedWithoutAdditionalPass)
    {
        throw "Viewer picking smoke backend '$backend' did not prove outline and clear."
    }
}

$backends = @($artifact.backends)
$expected = @('Storm', 'D3D12', 'Vulkan')
if ($backends.Count -ne $expected.Count)
{
    throw 'Viewer picking smoke did not record all three required backends.'
}
foreach ($backend in $expected)
{
    $record = @($backends | Where-Object { [string]$_.backend -ceq $backend })
    if ($record.Count -ne 1 -or
        [string]$record[0].hitPath -cne [string]$artifact.commonHitPath -or
        -not [bool]$record[0].clickHit -or
        -not [bool]$record[0].clickMiss)
    {
        throw "Viewer picking smoke backend '$backend' did not resolve the common path."
    }
}
if ([string]$artifact.stormUnselectedHash -ceq
        [string]$artifact.stormSelectedHash -or
    [string]$artifact.stormClearedHash -cne
        [string]$artifact.stormUnselectedHash)
{
    throw 'Viewer picking smoke Storm hashes do not prove highlight change and exact clear.'
}

$checkedSelectionPaths = @(
    Get-ChildItem (Join-Path $repoRoot 'eng/shaders/checked') `
        -File `
        -Filter 'selection.*' |
        Sort-Object Name |
        ForEach-Object { "eng/shaders/checked/$($_.Name)" }
)
$sourcePaths = @(
    'src/OpenUsd.Rendering.Silk/ISilkGraphicsDevice.cs',
    'src/OpenUsd.Rendering.Silk/SilkMeshRenderer.cs',
    'src/OpenUsd.Rendering.Silk/SilkSelectionOutline.cs',
    'src/OpenUsd.Rendering.Silk.D3D12/D3D12SilkGraphicsDevice.SelectionOutline.cs',
    'src/OpenUsd.Rendering.Silk.D3D12/D3D12SilkGraphicsDevice.Offscreen.cs',
    'src/OpenUsd.Rendering.Silk.D3D12/D3D12CompositionViewportPresenter.cs',
    'src/OpenUsd.Rendering.Silk.Vulkan/VulkanSilkGraphicsDevice.SelectionOutline.cs',
    'src/OpenUsd.Rendering.Silk.Vulkan/VulkanSilkGraphicsDevice.Offscreen.cs',
    'src/OpenUsd.Rendering.Silk.Vulkan/VulkanCompositionViewportPresenter.cs',
    'src/OpenUsd.Rendering.Silk.Metal/MetalSilkGraphicsDevice.SelectionOutline.cs',
    'src/OpenUsd.Rendering.Silk.Metal/MetalSilkGraphicsDevice.Offscreen.cs',
    'src/OpenUsd.Rendering.Silk.Metal/MetalCompositionViewportPresenter.cs',
    'src/OpenUsd.Viewer/ViewerPicking.cs',
    'src/OpenUsd.Viewer/ViewerPickingSmoke.cs',
    'src/OpenUsd.Viewer/ViewerRenderCoordinator.cs',
    'src/OpenUsd.Viewer/ViewerRenderBackendAdapters.cs',
    'src/OpenUsd.Viewer/ViewerFrameAdapters.cs',
    'src/OpenUsd.Viewer/AvaloniaViewerRenderBackendHost.cs',
    'src/OpenUsd.Viewer/StormNativeControlHost.cs',
    'src/OpenUsd.Viewer/StormViewportControl.cs',
    'src/OpenUsd.Viewer/RendererSwitchingViewport.cs',
    'src/OpenUsd.Viewer/ViewerTimelineModels.cs',
    'src/OpenUsd.Viewer/ViewerSessionModels.cs',
    'src/OpenUsd.Viewer/ViewerHostEventArgs.cs',
    'src/OpenUsd.Viewer/ViewerHostInteraction.cs',
    'src/OpenUsd.Viewer/ViewerHostOptions.cs',
    'src/OpenUsd.Viewer/ViewerStageSession.cs',
    'src/OpenUsd.Viewer/ViewerStartupOptions.cs',
    'src/OpenUsd.Viewer/MainWindow.axaml.cs',
    'tests/OpenUsd.Viewer.Tests/ViewerPickingTests.cs',
    'tests/OpenUsd.Viewer.Tests/ViewerRenderBackendTests.cs',
    'tests/OpenUsd.Viewer.Tests/ViewerSourceContractTests.cs',
    'tests/OpenUsd.Viewer.Tests/StormNativeChildHostTests.cs',
    'tests/OpenUsd.Rendering.ConformanceTests/D3D12SelectionOutlineTests.cs',
    'tests/OpenUsd.Rendering.ConformanceTests/VulkanSelectionOutlineTests.cs',
    'tests/OpenUsd.Rendering.ConformanceTests/MetalSelectionOutlineConformanceTests.cs',
    'eng/shaders/sources/selection.mask.vertex.slang',
    'eng/shaders/sources/selection.mask.fragment.slang',
    'eng/shaders/sources/selection.outline.vertex.slang',
    'eng/shaders/sources/selection.outline.fragment.slang',
    'eng/shaders/shader-manifest.json',
    'docs/rendering.md',
    'docs/architecture.md',
    'eng/run-viewer.ps1',
    'test-assets/minimal.usda',
    'eng/run-viewer-picking-smoke.ps1'
) + $checkedSelectionPaths
$sources = @($sourcePaths | ForEach-Object {
    $path = Join-Path $repoRoot $_
    [ordered]@{
        path = $_
        sha256 = (Get-FileHash $path -Algorithm SHA256).Hash
        length = (Get-Item $path).Length
    }
})
$summary = [ordered]@{
    schemaVersion = 3
    scenario = 'viewer-picking-short-smoke'
    status = 'passed'
    completedAt = [DateTimeOffset]::UtcNow.ToString('O')
    evidenceSha256 = (Get-FileHash $evidencePath -Algorithm SHA256).Hash
    stageSha256 = (Get-FileHash $stagePath -Algorithm SHA256).Hash
    commonHitPath = [string]$artifact.commonHitPath
    staleRetries = [long]$artifact.staleRetries
    hostCallbacks = [ordered]@{
        pickHit = [bool]$artifact.hostPickHitObserved
        pickMiss = [bool]$artifact.hostPickMissObserved
        selectionHit = [bool]$artifact.hostSelectionHitObserved
        selectionClear = [bool]$artifact.hostSelectionClearObserved
    }
    stormHashes = [ordered]@{
        unselected = [string]$artifact.stormUnselectedHash
        selected = [string]$artifact.stormSelectedHash
        cleared = [string]$artifact.stormClearedHash
    }
    silkOutlines = $silkOutlines
    sources = $sources
    nativeRuntime = @(
        @('openusd_storm_child.dll', 'openusd_hydra.dll') | ForEach-Object {
            $path = Join-Path $nativeRuntime $_
            [ordered]@{
                name = $_
                sha256 = (Get-FileHash $path -Algorithm SHA256).Hash
            }
        })
}
$summary | ConvertTo-Json -Depth 8 | Set-Content $summaryPath
Write-Output (
    'VIEWER_PICKING_SHORT_SMOKE passed ' +
    "path=$($artifact.commonHitPath) evidence=$evidencePath summary=$summaryPath")
