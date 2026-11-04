#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'managed-workflow-reports.ps1')
. (Join-Path $PSScriptRoot 'silk-cubic-shadow-report.ps1')

function Add-FixtureCase
{
    param(
        [Collections.Generic.List[object]]$Rows,
        [string]$Class,
        [string]$Method,
        [string]$DisplayName,
        [string]$Start
    )
    $Rows.Add([pscustomobject]@{
        id = "OpenUsd.Rendering.ConformanceTests.$Class.fixture.$Method.$($Rows.Count)"
        displayName = $DisplayName
        methodName = $Method
        className = $Class
        status = 'passed'
        startTime = $Start
    })
}

function New-FixtureReport
{
    param([string]$Platform, [string]$Class)

    $rows = [Collections.Generic.List[object]]::new()
    $start = [DateTimeOffset]::UtcNow.ToString('O')
    $backends = switch ($Platform)
    {
        'windows' { @('D3D12', 'Vulkan') }
        'linux' { @('Vulkan') }
        'macos' { @('Metal') }
        default { throw "Unknown fixture platform: $Platform" }
    }
    if ($Class -ceq 'SilkCubicCurveNativeTests')
    {
        foreach ($kind in @(
            @{ name = 'CubicBezierSamplesAndWidthsMatchIndependentBasis'
                values = @('nonperiodic', 'periodic', 'pinned') },
            @{ name = 'PinnedBezierShortControlsMatchRepeatedLastControlReference'
                values = @('two', 'three', 'pair') }))
        {
            foreach ($value in $kind.values)
            {
                foreach ($width in @('constant', 'uniform', 'varying', 'vertex'))
                {
                    foreach ($bounded in @('False', 'True'))
                    {
                        Add-FixtureCase $rows $Class $kind.name "$($kind.name)($value, $width, $bounded)" $start
                    }
                }
            }
        }
        $method = 'ShortBezierFallbackRemainsScopedToPinnedWrap'
        foreach ($argument in @('nonperiodic, 2', 'nonperiodic, 3', 'periodic, 2'))
        {
            foreach ($bounded in @('False', 'True'))
            {
                Add-FixtureCase $rows $Class $method "${method}($argument, $bounded)" $start
            }
        }
        foreach ($method in @(
            'CubicBezierVertexWidthsAcceptUnusedTrailingPoints',
            'PinnedBezierShortControlEditsPreserveBudgetAndIdentity',
            'CubicBezierReservationCountsChordsAndPreservesOldPageOnRefusal',
            'CubicBezierMalformedDeclaredWidthsUseDefaultInsteadOfAnotherInterpolation'))
        {
            Add-FixtureCase $rows $Class $method $method $start
        }
        $method = 'CubicBezierPointTopologyAndUnsupportedEditsRestoreIdentity'
        foreach ($bounded in @('False', 'True'))
        {
            Add-FixtureCase $rows $Class $method "${method}($bounded)" $start
        }
        $method = 'CubicBezierInstancesShareOneReservationAndKeepInstanceIdentity'
        foreach ($argument in @('4, nonperiodic', '2, pinned', '3, pinned'))
        {
            Add-FixtureCase $rows $Class $method "${method}($argument)" $start
        }
        $method = 'CubicBezierUnsupportedInputsRefuseBoundedAndRemoveLegacyGeometry'
        foreach ($argument in @(
            'bspline, nonperiodic, ',
            'catmullRom, nonperiodic, ',
            'bezier, segmented, ',
            'bezier, nonperiodic, normal3f[] normals = [(0,0,1)] (interpolation = "constant")',
            'bezier, nonperiodic, float[] primvars:displayOpacity = [0·5] (interpolation = "constant")',
            'bezier, nonperiodic, texCoord2f[] primvars:st = [(0,0)] (interpolation = "constant")'))
        {
            Add-FixtureCase $rows $Class $method "${method}($argument)" $start
        }
        $method = 'CubicBezierRasterMatchesIndependentSegments'
        foreach ($backend in $backends)
        {
            foreach ($bounded in @('False', 'True'))
            {
                Add-FixtureCase $rows $Class $method "${method}($backend, $bounded)" $start
            }
        }
    }
    else
    {
        $method = 'AuthoredBoundedPointShadowMatchesAnalyticPixelsWhenSupported'
        foreach ($backend in $backends)
        {
            Add-FixtureCase $rows $Class $method "${method}($backend)" $start
        }
    }
    $summary = {
        [pscustomobject]@{
            total = $rows.Count; passed = $rows.Count
            failed = 0; skipped = 0; cancelled = 0; timedOut = 0; flaky = 0
        }
    }
    return [pscustomobject]@{
        schemaVersion = 1
        publicationGeneration = [Guid]::NewGuid().ToString('N')
        assemblyName = 'OpenUsd.Rendering.ConformanceTests'
        summary = (& $summary)
        groups = @([pscustomobject]@{
            namespace = 'OpenUsd.Rendering.ConformanceTests'
            className = $Class
            summary = (& $summary)
            tests = $rows.ToArray()
        })
    }
}

function Copy-Fixture
{
    param([object]$Report)
    $Report | ConvertTo-Json -Depth 12 | ConvertFrom-Json -Depth 12
}

function Write-Fixture
{
    param([object]$Report, [string]$Path)
    $Report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Path
}

function Assert-Rejected
{
    param([string]$Name, [string]$Reason, [scriptblock]$Action)
    $rejected = $false
    try { & $Action | Out-Null }
    catch
    {
        if (-not $_.Exception.Message.Contains($Reason, [StringComparison]::Ordinal))
        {
            throw "$Name failed for the wrong reason: $($_.Exception.Message)"
        }
        $rejected = $true
    }
    if (-not $rejected) { throw "Adversarial report was accepted: $Name" }
    Write-Host "[cubic-shadow-report-test] rejected: $Name"
}

function Invoke-FakeCubicShadowRunner
{
    param(
        [string]$Project, [string]$Framework, [string]$Configuration,
        [switch]$UseWindowsAppHost, [int]$MinimumExpectedTests, [string[]]$TestArguments
    )
    if ($script:fakeMode -ceq 'duplicate-current-reports')
    {
        foreach ($suffix in @('one', 'two'))
        {
            Set-Content -LiteralPath (Join-Path $script:fakeReportDirectory (
                "OpenUsd.Rendering.ConformanceTests-$suffix-net10.0.tunit-report.json")) -Value '{}'
        }
        Set-Content -LiteralPath (Join-Path $script:fakeReportDirectory (
            'OpenUsd.Rendering.ConformanceTests-one-net10.0-report.html')) -Value '<html/>'
    }
    $global:LASTEXITCODE = 0
    'succeeded: 2'
    'failed: 0'
    'skipped: 0'
}

$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixtureRoot = if ([string]::IsNullOrWhiteSpace($env:OPENUSD_TEST_WORK_ROOT))
{
    Join-Path (Join-Path $repo 'artifacts') 'silk-cubic-shadow-report-test'
}
else
{
    Join-Path ([IO.Path]::GetFullPath($env:OPENUSD_TEST_WORK_ROOT)) 'silk-cubic-shadow-report-test'
}
$root = Join-Path $fixtureRoot ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
$path = Join-Path $root 'report.json'
$started = [DateTime]::UtcNow.AddMinutes(-1)
try
{
    foreach ($platform in @('windows', 'linux', 'macos'))
    {
        foreach ($class in @('SilkCubicCurveNativeTests', 'SilkPointShadowNativeConformanceTests'))
        {
            $fixture = New-FixtureReport $platform $class
            $count = if ($class -ceq 'SilkCubicCurveNativeTests')
            {
                if ($platform -ceq 'windows') { 73 } else { 71 }
            }
            else { if ($platform -ceq 'windows') { 2 } else { 1 } }
            Write-Fixture $fixture $path
            $accepted = Assert-SilkCubicShadowReport -ReportPath $path -Platform $platform `
                -Class $class -StartedUtc $started -LogPassedCount $count
            if ($accepted.passed -ne $count) { throw "Incorrect accepted count for $platform $class." }
            Write-Host "[cubic-shadow-report-test] accepted synthetic fixture: $platform $class $count"

            $gpuRows = @($fixture.groups[0].tests | Where-Object {
                $_.methodName -ceq 'CubicBezierRasterMatchesIndependentSegments' -or
                $_.methodName -ceq 'AuthoredBoundedPointShadowMatchesAnalyticPixelsWhenSupported'
            })
            foreach ($gpuRow in $gpuRows)
            {
                $changed = Copy-Fixture $fixture
                $selected = @($changed.groups[0].tests | Where-Object {
                    $_.displayName -ceq $gpuRow.displayName
                })
                if ($selected.Count -ne 1) { throw 'Fixture GPU row is not unique.' }
                $selected[0].displayName = "$($gpuRow.displayName)-replacement"
                Write-Fixture $changed $path
                Assert-Rejected "$platform missing $($gpuRow.displayName)" 'unexpected, duplicate' {
                    Assert-SilkCubicShadowReport -ReportPath $path -Platform $platform -Class $class `
                        -StartedUtc $started -LogPassedCount $count
                }
                $changed = Copy-Fixture $fixture
                $selected = @($changed.groups[0].tests | Where-Object {
                    $_.displayName -ceq $gpuRow.displayName
                })
                $selected[0].displayName = $gpuRow.displayName.Replace(
                    'D3D12', 'd3d12').Replace('Vulkan', 'vulkan').Replace('Metal', 'metal')
                Write-Fixture $changed $path
                Assert-Rejected "$platform mis-cased $($gpuRow.displayName)" 'unexpected, duplicate' {
                    Assert-SilkCubicShadowReport -ReportPath $path -Platform $platform -Class $class `
                        -StartedUtc $started -LogPassedCount $count
                }
            }
        }
    }

    foreach ($platform in @('windows', 'linux'))
    {
        $class = 'SilkShadowTransmissionConformanceTests'
        $fixture = New-FixtureReport windows SilkPointShadowNativeConformanceTests
        $rows = [Collections.Generic.List[object]]::new()
        foreach ($backend in @('D3D12', 'Vulkan'))
        {
            foreach ($entry in @(
                @{ method = 'TextureBudgetRefusalPreservesTransmissionAndRetriesTheSamePage'; args = @('') },
                @{ method = 'SingleAndMultipleLayersRespectReceiverDepthAndLifecycle'
                    args = @(', 1, False', ', 2, False', ', 4, False', ', 1, True', ', 2, True') },
                @{ method = 'OverflowAndEqualDepthRefusalPreservePublishedPixelsAndAllowCorrection'
                    args = @(', False', ', True') },
                @{ method = 'OpacityTextureEditsAndShadowLinksMatchAnalyticPixels'
                    args = @(', False', ', True') },
                @{ method = 'ConstantAndTexturedLayersComposeInEitherDepthOrder'
                    args = @(', False, False', ', False, True', ', True, False', ', True, True') },
                @{ method = 'OpaqueAndCutoutCastersComposeWithTransmission'; args = @('') }))
            {
                foreach ($argument in $entry.args)
                {
                    Add-FixtureCase $rows $class $entry.method "$($entry.method)($backend$argument)" `
                        ([DateTimeOffset]::UtcNow.ToString('O'))
                    if ($platform -ceq 'linux' -and $backend -ceq 'D3D12')
                    {
                        $rows[$rows.Count - 1].status = 'skipped'
                    }
                }
            }
        }
        $count = if ($platform -ceq 'windows') { 30 } else { 15 }
        $fixture.groups[0].className = $class
        $fixture.groups[0].tests = $rows.ToArray()
        foreach ($summary in @($fixture.summary, $fixture.groups[0].summary))
        {
            $summary.total = 30
            $summary.passed = $count
            $summary.skipped = 30 - $count
        }
        Write-Fixture $fixture $path
        $accepted = Assert-SilkCubicShadowReport $path $platform $class $started $count
        if ($accepted.passed -ne $count -or $accepted.skipped -ne 30 - $count)
        {
            throw 'The transmission backend evidence count is incorrect.'
        }
        foreach ($row in $rows)
        {
            $changed = Copy-Fixture $fixture
            $selected = @($changed.groups[0].tests | Where-Object displayName -CEQ $row.displayName)[0]
            $selected.status = if ($row.status -ceq 'passed') { 'skipped' } else { 'passed' }
            Write-Fixture $changed $path
            Assert-Rejected "$platform wrong transmission backend verdict $($row.displayName)" 'unexpected, duplicate' {
                Assert-SilkCubicShadowReport $path $platform $class $started $count
            }
            $changed = Copy-Fixture $fixture
            $selected = @($changed.groups[0].tests | Where-Object displayName -CEQ $row.displayName)[0]
            $selected.displayName += '-replacement'
            Write-Fixture $changed $path
            Assert-Rejected "$platform missing transmission identity $($row.displayName)" 'unexpected, duplicate' {
                Assert-SilkCubicShadowReport $path $platform $class $started $count
            }
        }
        $changed = Copy-Fixture $fixture
        $changed.groups[0].tests[1].id = $changed.groups[0].tests[0].id
        Write-Fixture $changed $path
        Assert-Rejected "$platform duplicate transmission ID" 'unexpected, duplicate' {
            Assert-SilkCubicShadowReport $path $platform $class $started $count
        }
        Write-Fixture $fixture $path
        Assert-Rejected "$platform wrong transmission log count" 'execution log and' {
            Assert-SilkCubicShadowReport $path $platform $class $started ($count - 1)
        }
    }

    foreach ($platform in @('windows', 'linux'))
    {
        $class = 'SilkFiniteAreaShadowTests'
        $fixture = New-FixtureReport windows SilkPointShadowNativeConformanceTests
        $rows = [Collections.Generic.List[object]]::new()
        foreach ($method in @(
            'RectEmitterShadowsMatchFiveRayReferenceAcrossUpdateAndRetirement',
            'TransformedRectEmitterShadowsMatchFiveRayReference'))
        {
            foreach ($backend in @('D3D12', 'Vulkan'))
            {
                Add-FixtureCase $rows $class $method "${method}($backend)" ([DateTimeOffset]::UtcNow.ToString('O'))
                if ($platform -ceq 'linux' -and $backend -ceq 'D3D12')
                {
                    $rows[$rows.Count - 1].status = 'skipped'
                }
            }
        }
        $count = if ($platform -ceq 'windows') { 4 } else { 2 }
        $fixture.groups[0].className = $class
        $fixture.groups[0].tests = $rows.ToArray()
        foreach ($summary in @($fixture.summary, $fixture.groups[0].summary))
        {
            $summary.total = 4
            $summary.passed = $count
            $summary.skipped = 4 - $count
        }
        Write-Fixture $fixture $path
        $accepted = Assert-SilkCubicShadowReport $path $platform $class $started $count
        if ($accepted.passed -ne $count -or $accepted.skipped -ne 4 - $count)
        {
            throw 'The finite-area backend evidence count is incorrect.'
        }
        foreach ($row in $rows)
        {
            foreach ($mutation in @('verdict', 'identity', 'method', 'stale'))
            {
                $changed = Copy-Fixture $fixture
                $selected = @($changed.groups[0].tests | Where-Object displayName -CEQ $row.displayName)[0]
                switch ($mutation)
                {
                    'verdict' { $selected.status = if ($row.status -ceq 'passed') { 'skipped' } else { 'passed' } }
                    'identity' { $selected.displayName += '-replacement' }
                    'method' { $selected.methodName = 'UnrelatedMethod' }
                    'stale' { $selected.startTime = $started.AddMinutes(-1).ToString('O') }
                }
                if ($mutation -ceq 'stale' -and $row.status -ceq 'skipped') { continue }
                Write-Fixture $changed $path
                $reason = if ($mutation -ceq 'stale') { 'predates this execution' } else { 'unexpected, duplicate' }
                Assert-Rejected "$platform finite-area $mutation $($row.displayName)" $reason {
                    Assert-SilkCubicShadowReport $path $platform $class $started $count
                }
            }
        }
        $changed = Copy-Fixture $fixture
        $changed.groups[0].tests[1].id = $changed.groups[0].tests[0].id
        Write-Fixture $changed $path
        Assert-Rejected "$platform duplicate finite-area ID" 'unexpected, duplicate' {
            Assert-SilkCubicShadowReport $path $platform $class $started $count
        }
        Write-Fixture $fixture $path
        Assert-Rejected "$platform incorrect finite-area log count" 'execution log and' {
            Assert-SilkCubicShadowReport $path $platform $class $started ($count - 1)
        }
    }

    foreach ($platform in @('windows', 'linux', 'macos'))
    {
        $class = 'VolumeCameraConformanceTests'
        $fixture = New-FixtureReport $platform $class
        $rows = [Collections.Generic.List[object]]::new()
        $intended = @(switch ($platform) {
            'windows' { @('D3D12', 'Vulkan') }
            'linux' { @('Vulkan') }
            'macos' { @('Metal') }
        })
        foreach ($backend in @('D3D12', 'Vulkan', 'Metal'))
        {
            Add-FixtureCase $rows $class CameraIntegrationBackendGate "CameraIntegrationBackendGate($backend)" `
                ([DateTimeOffset]::UtcNow.ToString('O'))
            if ($backend -cnotin $intended) { $rows[$rows.Count - 1].status = 'skipped' }
        }
        $fixture.groups[0].tests = $rows.ToArray()
        foreach ($summary in @($fixture.summary, $fixture.groups[0].summary))
        {
            $summary.total = 3; $summary.passed = $intended.Count; $summary.skipped = 3 - $intended.Count
        }
        Write-Fixture $fixture $path
        $accepted = Assert-SilkCubicShadowReport $path $platform $class $started $intended.Count
        if ($accepted.passed -ne $intended.Count) { throw 'Incorrect camera pass count.' }
        foreach ($row in $rows)
        {
            foreach ($mutation in @('verdict', 'identity', 'method'))
            {
                $changed = Copy-Fixture $fixture
                $selected = @($changed.groups[0].tests | Where-Object displayName -CEQ $row.displayName)[0]
                switch ($mutation) {
                    'verdict' { $selected.status = if ($row.status -ceq 'passed') { 'skipped' } else { 'passed' } }
                    'identity' { $selected.displayName += '-replacement' }
                    'method' { $selected.methodName = 'UnrelatedCameraMethod' }
                }
                Write-Fixture $changed $path
                Assert-Rejected "$platform camera $mutation $($row.displayName)" 'unexpected, duplicate' {
                    Assert-SilkCubicShadowReport $path $platform $class $started $intended.Count
                }
            }
        }
    }

    $receiptRoot = Join-Path $root 'camera-receipts'
    $cameraCases = @(
        'analytic-slab-511-side', 'analytic-slab-512-side', 'analytic-slab-512-oblique',
        'large-scale-inside', 'large-scale-threshold', 'parallel-outside',
        'camera-inside', 'camera-perspective', 'camera-near', 'camera-far', 'camera-enclosing-clip',
        'scaled-field-local', 'same-tint-order', 'opaque-update-reuse', 'transform-refusal-retry',
        'material-disabled-single',
        'camera-far-origin', 'camera-nonrigid-view', 'camera-micro-outside', 'camera-micro-inside',
        'camera-micro-clipped', 'perspective-near-small', 'perspective-near-one',
        'perspective-near-two-asymmetric', 'oblique-off-center-clip',
        'perspective-finite-large-far', 'perspective-infinite-far',
        'opaque-one-ulp-before-clear',
        'scene-clip-entry', 'scene-clip-exit', 'scene-clip-oblique',
        'scene-clip-excluded', 'scene-clip-parallel',
        'joint-unequal-tint-reference', 'joint-uniform-mixed-clipping',
        'joint-replacement-CpuAdmission', 'joint-replacement-GpuAdmission',
        'joint-replacement-AtlasSubmission', 'joint-ownership-uniform', 'joint-ownership-atlas',
        'joint-zero-opacity-field-limit',
        'joint-missing-asset-refusal', 'joint-retained-atlas-source-removal',
        'joint-unsupported-uniform-map', 'joint-unsupported-zero-opacity-map', 'joint-unsupported-sampled-map',
        'joint-unsupported-density-duplicate', 'joint-unsupported-density-composite',
        'joint-dense-unequal-tint-uniform', 'joint-dense-unequal-tint-atlas', 'joint-workload-refusal-retry',
        'joint-nonfinite-density-scalar', 'joint-nonfinite-density-grid',
        'joint-finite-density-scalar', 'joint-finite-density-grid',
        'joint-thin-density-scalar-translucent', 'joint-thin-density-scalar-opaque',
        'joint-thin-density-grid-translucent', 'joint-thin-density-grid-opaque',
        'joint-tint-exposure-scalar-retained', 'joint-tint-exposure-scalar-staged',
        'joint-tint-exposure-grid-retained', 'joint-tint-exposure-grid-staged',
        'joint-tint-exposure-zero-opacity', 'joint-tint-exposure-material-disabled',
        'joint-high-tint-overlap-scalar', 'joint-high-tint-overlap-dense-scalar',
        'joint-high-tint-overlap-grid', 'joint-high-tint-overlap-dense-grid',
        'joint-high-tint-overlap-1e-37-scalar', 'joint-high-tint-overlap-1e-20-scalar',
        'joint-high-tint-overlap-1e-8-scalar', 'joint-high-tint-overlap-1e-37-grid',
        'joint-high-tint-overlap-1e-20-grid', 'joint-high-tint-overlap-1e-8-grid',
        'joint-signal-subnormal-scale-scalar', 'joint-signal-step-underflow-scalar',
        'joint-signal-opacity-product-scalar', 'joint-signal-subnormal-scale-grid',
        'joint-signal-step-underflow-grid', 'joint-signal-opacity-product-grid',
        'joint-signal-subnormal-sample-grid', 'joint-signal-zero-opacity', 'joint-signal-material-disabled',
        'joint-signal-retained-resize-scalar', 'joint-signal-retained-resize-grid',
        'joint-output-allocation-retained', 'joint-output-frame-submission-retained',
        'joint-output-publication-retained', 'joint-output-allocation-staged',
        'joint-output-frame-submission-staged', 'joint-output-publication-staged',
        'joint-direct-retirement-none', 'joint-retirement-none-retained', 'joint-retirement-none-staged',
        'joint-direct-retirement-depth', 'joint-retirement-depth-retained', 'joint-retirement-depth-staged',
        'joint-direct-retirement-resources', 'joint-retirement-resources-retained',
        'joint-retirement-resources-staged', 'joint-direct-retirement-both',
        'joint-retirement-both-retained', 'joint-retirement-both-staged',
        'joint-failed-frame-cleanup-retained', 'joint-snapshot-rollback-retained',
        'joint-snapshot-budget-refusal-retained', 'joint-snapshot-budget-exact-retained',
        'joint-failed-frame-cleanup-staged', 'joint-snapshot-rollback-staged',
        'joint-snapshot-budget-refusal-staged', 'joint-snapshot-budget-exact-staged',
        'joint-snapshot-budget-reuse', 'joint-renderer-disposal-retry')
    $prior92CameraCases = @($cameraCases | Where-Object {
        $_ -cnotin @(
            'joint-direct-retirement-none', 'joint-retirement-none-retained', 'joint-retirement-none-staged',
            'joint-direct-retirement-depth', 'joint-retirement-depth-retained', 'joint-retirement-depth-staged',
            'joint-direct-retirement-resources', 'joint-retirement-resources-retained',
            'joint-retirement-resources-staged', 'joint-direct-retirement-both',
            'joint-retirement-both-retained', 'joint-retirement-both-staged',
            'joint-failed-frame-cleanup-retained', 'joint-snapshot-rollback-retained',
            'joint-snapshot-budget-refusal-retained', 'joint-snapshot-budget-exact-retained',
            'joint-failed-frame-cleanup-staged', 'joint-snapshot-rollback-staged',
            'joint-snapshot-budget-refusal-staged', 'joint-snapshot-budget-exact-staged',
            'joint-snapshot-budget-reuse', 'joint-renderer-disposal-retry')
    })
    $prior84CameraCases = @($prior92CameraCases | Where-Object {
        $_ -cnotin @(
            'joint-signal-retained-resize-scalar', 'joint-signal-retained-resize-grid',
            'joint-output-allocation-retained', 'joint-output-frame-submission-retained',
            'joint-output-publication-retained', 'joint-output-allocation-staged',
            'joint-output-frame-submission-staged', 'joint-output-publication-staged')
    })
    $prior69CameraCases = @($prior84CameraCases | Where-Object {
        $_ -cnotin @(
            'joint-high-tint-overlap-1e-37-scalar', 'joint-high-tint-overlap-1e-20-scalar',
            'joint-high-tint-overlap-1e-8-scalar', 'joint-high-tint-overlap-1e-37-grid',
            'joint-high-tint-overlap-1e-20-grid', 'joint-high-tint-overlap-1e-8-grid',
            'joint-signal-subnormal-scale-scalar', 'joint-signal-step-underflow-scalar',
            'joint-signal-opacity-product-scalar', 'joint-signal-subnormal-scale-grid',
            'joint-signal-step-underflow-grid', 'joint-signal-opacity-product-grid',
            'joint-signal-subnormal-sample-grid', 'joint-signal-zero-opacity', 'joint-signal-material-disabled')
    })
    $prior65CameraCases = @($prior69CameraCases | Where-Object {
        $_ -cnotin @(
            'joint-high-tint-overlap-scalar', 'joint-high-tint-overlap-dense-scalar',
            'joint-high-tint-overlap-grid', 'joint-high-tint-overlap-dense-grid')
    })
    $priorCameraCases = @($prior65CameraCases | Where-Object {
        $_ -cnotin @(
            'joint-tint-exposure-scalar-retained', 'joint-tint-exposure-scalar-staged',
            'joint-tint-exposure-grid-retained', 'joint-tint-exposure-grid-staged',
            'joint-tint-exposure-zero-opacity', 'joint-tint-exposure-material-disabled')
    })
    $cameraAssert = Join-Path $PSScriptRoot 'assert-volume-camera-evidence.ps1'
    foreach ($platform in @('windows', 'linux', 'macos'))
    {
        $backends = switch ($platform) {
            'windows' { @('D3D12', 'Vulkan') }
            'linux' { @('Vulkan') }
            'macos' { @('Metal') }
        }
        foreach ($backend in $backends)
        {
            $directory = Join-Path $receiptRoot "camera-$platform-$backend"
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
            $receipt = [pscustomobject]@{
                schemaVersion = 1; platform = $platform; backend = $backend; status = 'passed'
                deviceName = 'SwiftShader fixture'; software = $true; cases = $cameraCases
            }
            Write-Fixture $receipt (Join-Path $directory 'receipt.json')
        }
        & $cameraAssert -Root $receiptRoot -Platform $platform -NotBeforeUtc $started
        foreach ($backend in $backends)
        {
            $receiptPath = Join-Path (Join-Path $receiptRoot "camera-$platform-$backend") 'receipt.json'
            $valid = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
            if ($platform -cne 'windows')
            {
                $hardware = Copy-Fixture $valid
                $hardware.deviceName = 'Hardware fixture'
                $hardware.software = $false
                Write-Fixture $hardware $receiptPath
                & $cameraAssert -Root $receiptRoot -Platform $platform -NotBeforeUtc $started
                Write-Fixture $valid $receiptPath
            }
            else
            {
                $hardware = Copy-Fixture $valid
                $hardware.software = $false
                Write-Fixture $hardware $receiptPath
                $reason = if ($backend -ceq 'Vulkan') { 'did not use SwiftShader' }
                    else { 'did not use a software device' }
                Assert-Rejected "$platform $backend hardware camera receipt" $reason {
                    & $cameraAssert -Root $receiptRoot -Platform $platform -NotBeforeUtc $started
                }
                Write-Fixture $valid $receiptPath
                if ($backend -ceq 'Vulkan')
                {
                    $otherSoftware = Copy-Fixture $valid
                    $otherSoftware.deviceName = 'Other software renderer'
                    Write-Fixture $otherSoftware $receiptPath
                    Assert-Rejected "$platform $backend non-SwiftShader camera receipt" 'did not use SwiftShader' {
                        & $cameraAssert -Root $receiptRoot -Platform $platform -NotBeforeUtc $started
                    }
                    Write-Fixture $valid $receiptPath
                }
            }
            foreach ($mutation in @('absent', 'stale', 'incomplete', 'backend',
                'string-schema-version', 'fractional-schema-version', 'boolean-schema-version',
                'array-platform', 'array-backend',
                'array-status', 'numeric-device-name', 'array-device-name', 'string-software',
                'numeric-software', 'null-software', 'missing-software', 'non-string-case',
                'duplicate-case', 'missing-case',
                'old-manifest', 'fifteen-case-manifest', 'sixteen-case-manifest',
                'twenty-six-case-manifest', 'thirty-two-case-manifest', 'thirty-three-case-manifest',
                'thirty-eight-case-manifest', 'forty-two-case-manifest',
                'forty-six-case-manifest', 'forty-eight-case-manifest', 'fifty-one-case-manifest',
                'fifty-three-case-manifest', 'fifty-five-case-manifest', 'fifty-nine-case-manifest',
                'sixty-five-case-manifest', 'sixty-nine-case-manifest', 'eighty-four-case-manifest',
                'ninety-two-case-manifest', 'missing-snapshot-budget', 'missing-retirement', 'missing-disposal',
                'missing-finite-scalar', 'missing-finite-grid',
                'substituted-case'))
            {
                $changed = Copy-Fixture $valid
                $reason = 'Incomplete or mismatched'
                switch ($mutation) {
                    'absent' { $changed = $null; $reason = 'Cannot find path' }
                    'stale' { $reason = 'Stale camera receipt' }
                    'incomplete' { $changed.status = 'started' }
                    'backend' { $changed.backend = 'SubstitutedBackend' }
                    'string-schema-version' { $changed.schemaVersion = '1' }
                    'fractional-schema-version' { $changed.schemaVersion = 1.0 }
                    'boolean-schema-version' { $changed.schemaVersion = $true }
                    'array-platform' { $changed.platform = @($changed.platform) }
                    'array-backend' { $changed.backend = @($changed.backend) }
                    'array-status' { $changed.status = @($changed.status) }
                    'numeric-device-name' { $changed.deviceName = 1 }
                    'array-device-name' { $changed.deviceName = @($changed.deviceName) }
                    'string-software' { $changed.software = 'true' }
                    'numeric-software' { $changed.software = 1 }
                    'null-software' { $changed.software = $null }
                    'missing-software' { $changed.PSObject.Properties.Remove('software') }
                    'non-string-case' { $changed.cases[0] = 1; $reason = 'Missing, duplicate' }
                    'duplicate-case' { $changed.cases[1] = $changed.cases[0]; $reason = 'Missing, duplicate' }
                    'missing-case' { $changed.cases = @($changed.cases | Select-Object -Skip 1); $reason = 'Missing, duplicate' }
                    'old-manifest' {
                        $changed.cases = @($changed.cases | Select-Object -First 16 | Where-Object {
                            $_ -cnotin @('large-scale-inside', 'large-scale-threshold', 'parallel-outside',
                                'material-disabled-single')
                        })
                        $reason = 'Missing, duplicate'
                    }
                    'fifteen-case-manifest' {
                        $changed.cases = @($changed.cases | Select-Object -First 15)
                        $reason = 'Missing, duplicate'
                    }
                    'sixteen-case-manifest' {
                        $changed.cases = @($changed.cases | Select-Object -First 16)
                        $reason = 'Missing, duplicate'
                    }
                    'twenty-six-case-manifest' {
                        $changed.cases = @($changed.cases | Select-Object -First 33 | Where-Object {
                            $_ -cnotin @('perspective-near-small', 'perspective-near-one',
                                'perspective-near-two-asymmetric', 'oblique-off-center-clip',
                                'perspective-finite-large-far', 'perspective-infinite-far',
                                'opaque-one-ulp-before-clear')
                        })
                        $reason = 'Missing, duplicate'
                    }
                    'thirty-two-case-manifest' {
                        $changed.cases = @($changed.cases | Select-Object -First 33 | Where-Object {
                            $_ -cne 'opaque-one-ulp-before-clear'
                        })
                        $reason = 'Missing, duplicate'
                    }
                    'thirty-three-case-manifest' {
                        $changed.cases = @($changed.cases | Select-Object -First 33)
                        $reason = 'Missing, duplicate'
                    }
                    'thirty-eight-case-manifest' {
                        $changed.cases = @($changed.cases | Select-Object -First 38)
                        $reason = 'Missing, duplicate'
                    }
                    'forty-two-case-manifest' {
                        $changed.cases = @($changed.cases | Where-Object {
                            $_ -cnotin @('joint-zero-opacity-field-limit', 'joint-unsupported-zero-opacity-map',
                                'joint-dense-unequal-tint-uniform', 'joint-dense-unequal-tint-atlas',
                                'joint-workload-refusal-retry', 'joint-nonfinite-density-scalar',
                                'joint-nonfinite-density-grid', 'joint-finite-density-scalar',
                                'joint-finite-density-grid')
                        } | Select-Object -First 42)
                        $reason = 'Missing, duplicate'
                    }
                    'forty-six-case-manifest' {
                        $changed.cases = @($priorCameraCases | Where-Object {
                            $_ -cnotin @('joint-zero-opacity-field-limit', 'joint-unsupported-zero-opacity-map',
                                'joint-dense-unequal-tint-uniform', 'joint-dense-unequal-tint-atlas',
                                'joint-workload-refusal-retry', 'joint-nonfinite-density-scalar',
                                'joint-nonfinite-density-grid', 'joint-finite-density-scalar',
                                'joint-finite-density-grid',
                                'joint-thin-density-scalar-translucent', 'joint-thin-density-scalar-opaque',
                                'joint-thin-density-grid-translucent', 'joint-thin-density-grid-opaque')
                        })
                        if ($changed.cases.Count -ne 46) { throw 'Incorrect 46-case camera fixture.' }
                        $reason = 'Missing, duplicate'
                    }
                    'forty-eight-case-manifest' {
                        $changed.cases = @($priorCameraCases | Where-Object {
                            $_ -cnotin @('joint-dense-unequal-tint-uniform', 'joint-dense-unequal-tint-atlas',
                                'joint-workload-refusal-retry', 'joint-nonfinite-density-scalar',
                                'joint-nonfinite-density-grid', 'joint-finite-density-scalar',
                                'joint-finite-density-grid',
                                'joint-thin-density-scalar-translucent', 'joint-thin-density-scalar-opaque',
                                'joint-thin-density-grid-translucent', 'joint-thin-density-grid-opaque')
                        })
                        if ($changed.cases.Count -ne 48) { throw 'Incorrect 48-case camera fixture.' }
                        $reason = 'Missing, duplicate'
                    }
                    'fifty-one-case-manifest' {
                        $changed.cases = @($priorCameraCases | Where-Object {
                            $_ -cnotin @('joint-nonfinite-density-scalar', 'joint-nonfinite-density-grid',
                                'joint-finite-density-scalar', 'joint-finite-density-grid',
                                'joint-thin-density-scalar-translucent', 'joint-thin-density-scalar-opaque',
                                'joint-thin-density-grid-translucent', 'joint-thin-density-grid-opaque')
                        })
                        if ($changed.cases.Count -ne 51) { throw 'Incorrect 51-case camera fixture.' }
                        $reason = 'Missing, duplicate'
                    }
                    'fifty-three-case-manifest' {
                        $changed.cases = @($priorCameraCases | Where-Object {
                            $_ -cnotin @('joint-finite-density-scalar', 'joint-finite-density-grid',
                                'joint-thin-density-scalar-translucent', 'joint-thin-density-scalar-opaque',
                                'joint-thin-density-grid-translucent', 'joint-thin-density-grid-opaque')
                        })
                        if ($changed.cases.Count -ne 53) { throw 'Incorrect 53-case camera fixture.' }
                        $reason = 'Missing, duplicate'
                    }
                    'fifty-five-case-manifest' {
                        $changed.cases = @($priorCameraCases | Where-Object {
                            $_ -cnotin @('joint-thin-density-scalar-translucent', 'joint-thin-density-scalar-opaque',
                                'joint-thin-density-grid-translucent', 'joint-thin-density-grid-opaque')
                        })
                        if ($changed.cases.Count -ne 55) { throw 'Incorrect 55-case camera fixture.' }
                        $reason = 'Missing, duplicate'
                    }
                    'fifty-nine-case-manifest' {
                        $changed.cases = @($priorCameraCases)
                        if ($changed.cases.Count -ne 59) { throw 'Incorrect 59-case camera fixture.' }
                        $reason = 'Missing, duplicate'
                    }
                    'sixty-five-case-manifest' {
                        $changed.cases = @($prior65CameraCases)
                        if ($changed.cases.Count -ne 65) { throw 'Incorrect 65-case camera fixture.' }
                        $reason = 'Missing, duplicate'
                    }
                    'sixty-nine-case-manifest' {
                        $changed.cases = @($prior69CameraCases)
                        if ($changed.cases.Count -ne 69) { throw 'Incorrect 69-case camera fixture.' }
                        $reason = 'Missing, duplicate'
                    }
                    'eighty-four-case-manifest' {
                        $changed.cases = @($prior84CameraCases)
                        if ($changed.cases.Count -ne 84) { throw 'Incorrect 84-case camera fixture.' }
                        $reason = 'Missing, duplicate'
                    }
                    'ninety-two-case-manifest' {
                        $changed.cases = @($prior92CameraCases)
                        if ($changed.cases.Count -ne 92) { throw 'Incorrect 92-case camera fixture.' }
                        $reason = 'Missing, duplicate'
                    }
                    'missing-snapshot-budget' {
                        $changed.cases = @($changed.cases | Where-Object {
                            $_ -cne 'joint-snapshot-budget-exact-staged'
                        })
                        $reason = 'Missing, duplicate'
                    }
                    'missing-retirement' {
                        $changed.cases = @($changed.cases | Where-Object {
                            $_ -cne 'joint-retirement-both-retained'
                        })
                        $reason = 'Missing, duplicate'
                    }
                    'missing-disposal' {
                        $changed.cases = @($changed.cases | Where-Object {
                            $_ -cne 'joint-renderer-disposal-retry'
                        })
                        $reason = 'Missing, duplicate'
                    }
                    'missing-finite-scalar' {
                        $changed.cases = @($changed.cases | Where-Object { $_ -cne 'joint-finite-density-scalar' })
                        $reason = 'Missing, duplicate'
                    }
                    'missing-finite-grid' {
                        $changed.cases = @($changed.cases | Where-Object { $_ -cne 'joint-finite-density-grid' })
                        $reason = 'Missing, duplicate'
                    }
                    'substituted-case' { $changed.cases[3] = 'unrelated-large-scale-case'; $reason = 'Missing, duplicate' }
                }
                Write-Fixture $changed $receiptPath
                if ($mutation -ceq 'absent')
                {
                    Move-Item -LiteralPath $receiptPath -Destination "$receiptPath.absent"
                }
                if ($mutation -ceq 'stale')
                {
                    (Get-Item -LiteralPath $receiptPath).LastWriteTimeUtc = $started.AddMinutes(-1)
                }
                Assert-Rejected "$platform $backend camera receipt $mutation" $reason {
                    & $cameraAssert -Root $receiptRoot -Platform $platform -NotBeforeUtc $started
                }
                Write-Fixture $valid $receiptPath
            }
            foreach ($case in $cameraCases)
            {
                $changed = Copy-Fixture $valid
                $changed.cases = @($changed.cases | Where-Object { $_ -cne $case })
                Write-Fixture $changed $receiptPath
                Assert-Rejected "$platform $backend missing camera case $case" 'Missing, duplicate' {
                    & $cameraAssert -Root $receiptRoot -Platform $platform -NotBeforeUtc $started
                }
                Write-Fixture $valid $receiptPath
            }
        }
    }

    function Assert-CameraFailureRetention
    {
        $volumeRoot = Join-Path $root 'failed-camera-volumes'
        $emptyEvidence = Join-Path $root 'camera-failure-no-receipts'
        Save-SilkCameraFailureEvidence -VolumeRoot $volumeRoot -EvidenceRoot $emptyEvidence `
            -Platform windows -PrimaryFailure 'runner failed before receipt creation'
        $empty = Get-Content -LiteralPath (Join-Path $emptyEvidence 'camera.failure.json') -Raw |
            ConvertFrom-Json
        if ($empty.status -cne 'failed' -or
            $empty.primaryFailure -cne 'runner failed before receipt creation' -or
            @($empty.receipts).Count -ne 0 -or @($empty.retentionErrors).Count -ne 0)
        {
            throw 'Missing diagnostic receipts changed the original failure.'
        }
        $sourceDirectory = Join-Path $volumeRoot 'camera-windows-D3D12'
        New-Item -ItemType Directory -Path $sourceDirectory -Force | Out-Null
        $source = Join-Path $sourceDirectory 'receipt.json'
        foreach ($kind in @('started', 'stale', 'malformed'))
        {
            $diagnostics = Join-Path $root "camera-failure-$kind"
            New-Item -ItemType Directory -Path $diagnostics | Out-Null
            if ($kind -ceq 'malformed') { Set-Content -LiteralPath $source -Value '{partial' }
            else { Write-Fixture ([pscustomobject]@{ status = 'started'; cases = @() }) $source }
            if ($kind -ceq 'stale') { (Get-Item $source).LastWriteTimeUtc = $started.AddMinutes(-1) }
            $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
            Save-SilkCameraFailureEvidence -VolumeRoot $volumeRoot -EvidenceRoot $diagnostics `
                -Platform windows -PrimaryFailure 'runner or exact report validation failed'
            $manifest = Get-Content -LiteralPath (Join-Path $diagnostics 'camera.failure.json') -Raw |
                ConvertFrom-Json
            $copy = Join-Path (Join-Path (Join-Path $diagnostics 'camera-failure-receipts') `
                'camera-windows-D3D12') 'receipt.json'
            if ($manifest.status -cne 'failed' -or -not $manifest.diagnosticOnly -or
                $manifest.primaryFailure -cne 'runner or exact report validation failed' -or
                @($manifest.receipts).Count -ne 1 -or @($manifest.retentionErrors).Count -ne 0 -or
                $manifest.receipts[0].sha256 -cne $hash.ToLowerInvariant() -or
                (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -cne $hash -or
                (Test-Path -LiteralPath (Join-Path $diagnostics 'camera.execution.json')) -or
                (Test-Path -LiteralPath (Join-Path $diagnostics 'camera-receipts')))
            {
                throw "Camera $kind failure diagnostics were lost or promoted to success evidence."
            }
        }

        $vulkanDirectory = Join-Path $volumeRoot 'camera-windows-Vulkan'
        New-Item -ItemType Directory -Path $vulkanDirectory | Out-Null
        Write-Fixture ([pscustomobject]@{ status = 'started' }) (Join-Path $vulkanDirectory 'receipt.json')
        $diagnostics = Join-Path $root 'camera-failure-copy-error'
        $retained = Join-Path $diagnostics 'camera-failure-receipts'
        New-Item -ItemType Directory -Path $retained -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $retained 'camera-windows-D3D12') -Value 'blocks directory creation'
        $warnings = @()
        Save-SilkCameraFailureEvidence -VolumeRoot $volumeRoot -EvidenceRoot $diagnostics `
            -Platform windows -PrimaryFailure 'original runner failure' -WarningVariable warnings
        $manifest = Get-Content -LiteralPath (Join-Path $diagnostics 'camera.failure.json') -Raw |
            ConvertFrom-Json
        if ($manifest.primaryFailure -cne 'original runner failure' -or $manifest.status -cne 'failed' -or
            @($manifest.retentionErrors).Count -ne 1 -or
            -not $manifest.retentionErrors[0].StartsWith('D3D12:', [StringComparison]::Ordinal) -or
            @($warnings).Count -ne 1 -or @($manifest.receipts).Count -ne 1 -or
            $manifest.receipts[0].backend -cne 'Vulkan' -or
            -not (Test-Path -LiteralPath (Join-Path (Join-Path $retained 'camera-windows-Vulkan') 'receipt.json')))
        {
            throw 'Copy failure replaced the primary failure or prevented other backend diagnostic retention.'
        }
    }
    Assert-CameraFailureRetention

    $class = 'SilkCubicCurveNativeTests'
    $fixture = New-FixtureReport 'windows' $class
    $count = 73
    $changed = Copy-Fixture $fixture
    $changed.groups[0].tests[1].id = $changed.groups[0].tests[0].id
    Write-Fixture $changed $path
    Assert-Rejected 'duplicate case ID' 'unexpected, duplicate' {
        Assert-SilkCubicShadowReport $path windows $class $started $count
    }
    $changed = Copy-Fixture $fixture
    $changed.groups[0].tests[1].displayName = $changed.groups[0].tests[0].displayName
    Write-Fixture $changed $path
    Assert-Rejected 'duplicate case name' 'unexpected, duplicate' {
        Assert-SilkCubicShadowReport $path windows $class $started $count
    }
    $changed = Copy-Fixture $fixture
    $changed.groups[0].tests[1].id = $changed.groups[0].tests[0].id.Replace('.fixture.', '.Fixture.')
    Write-Fixture $changed $path
    Assert-Rejected 'case-colliding ID' 'unexpected, duplicate' {
        Assert-SilkCubicShadowReport $path windows $class $started $count
    }
    $changed = Copy-Fixture $fixture
    $changed.groups[0].tests[0].displayName = $changed.groups[0].tests[0].displayName.Replace(
        'nonperiodic', 'Nonperiodic')
    Write-Fixture $changed $path
    Assert-Rejected 'mis-cased non-raster argument' 'unexpected, duplicate' {
        Assert-SilkCubicShadowReport $path windows $class $started $count
    }
    $changed = Copy-Fixture $fixture
    $opacity = @($changed.groups[0].tests | Where-Object { $_.displayName.Contains('0·5') })[0]
    $opacity.displayName = $opacity.displayName.Replace('0·5', '0.5')
    Write-Fixture $changed $path
    Assert-Rejected 'unsanitized argument display name' 'unexpected, duplicate' {
        Assert-SilkCubicShadowReport $path windows $class $started $count
    }
    $changed = Copy-Fixture $fixture
    $changed.groups[0].tests[0].methodName = $changed.groups[0].tests[0].methodName.ToLowerInvariant()
    Write-Fixture $changed $path
    Assert-Rejected 'mis-cased method' 'unexpected, duplicate' {
        Assert-SilkCubicShadowReport $path windows $class $started $count
    }
    $changed = Copy-Fixture $fixture
    $changed.groups[0].tests[0].id = $changed.groups[0].tests[0].id.Replace(
        '.CubicBezierSamplesAndWidthsMatchIndependentBasis.',
        '.cubicBezierSamplesAndWidthsMatchIndependentBasis.')
    Write-Fixture $changed $path
    Assert-Rejected 'mis-cased ID method' 'unexpected, duplicate' {
        Assert-SilkCubicShadowReport $path windows $class $started $count
    }
    $changed = Copy-Fixture $fixture
    $changed.groups[0].tests[0].className = $class.ToLowerInvariant()
    Write-Fixture $changed $path
    Assert-Rejected 'mis-cased row class' 'unexpected, duplicate' {
        Assert-SilkCubicShadowReport $path windows $class $started $count
    }
    foreach ($status in @('failed', 'skipped', 'cancelled', 'timedOut', 'Passed'))
    {
        $changed = Copy-Fixture $fixture
        $changed.groups[0].tests[0].status = $status
        Write-Fixture $changed $path
        Assert-Rejected "row status $status" 'unexpected, duplicate' {
            Assert-SilkCubicShadowReport $path windows $class $started $count
        }
    }
    foreach ($property in @('failed', 'skipped', 'cancelled', 'timedOut', 'flaky'))
    {
        $changed = Copy-Fixture $fixture
        $changed.groups[0].summary.$property = 1
        Write-Fixture $changed $path
        Assert-Rejected "group summary $property" 'no skips or failures' {
            Assert-SilkCubicShadowReport $path windows $class $started $count
        }
    }
    $changed = Copy-Fixture $fixture
    $changed.summary.passed--
    Write-Fixture $changed $path
    Assert-Rejected 'incorrect top-level summary' 'no skips or failures' {
        Assert-SilkCubicShadowReport $path windows $class $started $count
    }
    Write-Fixture $fixture $path
    Assert-Rejected 'incorrect log count' 'execution log and' {
        Assert-SilkCubicShadowReport $path windows $class $started 72
    }
    foreach ($field in @('assemblyName', 'publicationGeneration'))
    {
        $changed = Copy-Fixture $fixture
        $changed.$field = if ($field -ceq 'assemblyName') { 'openusd.Rendering.ConformanceTests' } else { '' }
        Write-Fixture $changed $path
        Assert-Rejected "invalid $field" 'does not identify' {
            Assert-SilkCubicShadowReport $path windows $class $started $count
        }
    }
    $changed = Copy-Fixture $fixture
    $changed.groups[0].namespace = 'openusd.Rendering.ConformanceTests'
    Write-Fixture $changed $path
    Assert-Rejected 'mis-cased namespace' 'does not identify' {
        Assert-SilkCubicShadowReport $path windows $class $started $count
    }
    $changed = Copy-Fixture $fixture
    $changed.groups[0].className = $class.ToLowerInvariant()
    Write-Fixture $changed $path
    Assert-Rejected 'mis-cased group class' 'does not identify' {
        Assert-SilkCubicShadowReport $path windows $class $started $count
    }
    $changed = Copy-Fixture $fixture
    $changed.groups += $changed.groups[0]
    Write-Fixture $changed $path
    Assert-Rejected 'duplicate group' 'does not identify' {
        Assert-SilkCubicShadowReport $path windows $class $started $count
    }
    $changed = Copy-Fixture $fixture
    $changed.groups[0].tests[0].startTime = $started.AddMinutes(-1).ToString('O')
    Write-Fixture $changed $path
    Assert-Rejected 'fresh file containing stale case' 'predates this execution' {
        Assert-SilkCubicShadowReport $path windows $class $started $count
    }
    Write-Fixture $fixture $path
    [IO.File]::SetLastWriteTimeUtc($path, $started.AddMinutes(-1))
    Assert-Rejected 'stale report file' 'fresh' {
        Assert-SilkCubicShadowReport $path windows $class $started $count
    }
    Assert-Rejected 'missing report file' 'fresh' {
        Assert-SilkCubicShadowReport (Join-Path $root 'missing.json') windows $class $started $count
    }
    Write-Fixture $fixture $path
    Assert-Rejected 'changed retained report hash' 'does not match' {
        Assert-SilkCubicShadowReport $path windows $class $started $count -ExpectedSha256 ('0' * 64)
    }
    $priorPath = Join-Path $root 'prior.json'
    Write-Fixture $fixture $priorPath
    Assert-Rejected 'unchanged prior report' 'was not replaced' {
        Assert-SilkCubicShadowReport $path windows $class $started $count -PriorReportPaths @($priorPath)
    }
    $prior = Copy-Fixture $fixture
    $prior.groups[0].tests[0].status = 'failed'
    Write-Fixture $prior $priorPath
    Assert-Rejected 'reused publication generation' 'reused a prior publication generation' {
        Assert-SilkCubicShadowReport $path windows $class $started $count -PriorReportPaths @($priorPath)
    }
    Set-Content -LiteralPath $priorPath -Value '{invalid json'
    Assert-Rejected 'invalid prior report' 'report evidence is invalid' {
        Assert-SilkCubicShadowReport $path windows $class $started $count -PriorReportPaths @($priorPath)
    }
    $prior = Copy-Fixture $fixture
    $prior.publicationGeneration = ''
    Write-Fixture $prior $priorPath
    Assert-Rejected 'prior report without publication generation' 'has no publication generation' {
        Assert-SilkCubicShadowReport $path windows $class $started $count -PriorReportPaths @($priorPath)
    }

    $assembly = Join-Path $root 'assembly'
    $script:fakeReportDirectory = Join-Path $assembly 'TestResults'
    $evidence = Join-Path $root 'evidence'
    New-Item -ItemType Directory -Path $script:fakeReportDirectory, $evidence -Force | Out-Null
    $oldRaw = 'OpenUsd.Rendering.ConformanceTests-windows-net10.0.tunit-report.json'
    $oldHtml = 'OpenUsd.Rendering.ConformanceTests-windows-net10.0-report.html'
    Set-Content -LiteralPath (Join-Path $script:fakeReportDirectory $oldRaw) -Value '{}'
    Set-Content -LiteralPath (Join-Path $script:fakeReportDirectory $oldHtml) -Value '<html/>'
    $script:fakeMode = 'no-new-reports'
    Assert-Rejected 'stale reports without a new execution' 'report retention failed' {
        Invoke-ManagedWorkflowScenario -Runner 'Invoke-FakeCubicShadowRunner' -Project 'fixture' `
            -AssemblyDirectory $assembly -EvidenceRoot $evidence `
            -Entry @{ Id = 'stale'; Class = $class } -ReportAssembly 'OpenUsd.Rendering.ConformanceTests'
    }
    foreach ($name in @("stale.prior-$oldRaw", "stale.prior-$oldHtml", 'stale.log'))
    {
        if (-not (Test-Path -LiteralPath (Join-Path $evidence $name) -PathType Leaf))
        {
            throw "Separated stale evidence was not retained: $name"
        }
    }
    $script:fakeMode = 'duplicate-current-reports'
    Assert-Rejected 'duplicate current report files' 'report retention failed' {
        Invoke-ManagedWorkflowScenario -Runner 'Invoke-FakeCubicShadowRunner' -Project 'fixture' `
            -AssemblyDirectory $assembly -EvidenceRoot $evidence `
            -Entry @{ Id = 'duplicate'; Class = $class } -ReportAssembly 'OpenUsd.Rendering.ConformanceTests'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $evidence 'duplicate.report.html') -PathType Leaf))
    {
        throw 'The available HTML report was not retained after JSON duplication.'
    }
    Write-Host '[cubic-shadow-report-test] all report controls passed'
}
finally
{
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
