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
$root = Join-Path $repo ("artifacts/silk-cubic-shadow-report-test/$([Guid]::NewGuid().ToString('N'))")
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
