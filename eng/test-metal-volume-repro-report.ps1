#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'metal-volume-repro-report.ps1')

$started = [DateTime]::UtcNow.AddSeconds(-5)
$finished = $started.AddSeconds(10)
$method = 'OverlappingHighFiniteTintsMatchTheIndependentComposite'
$class = 'VolumeCameraJointPassConformanceTests'

function New-Fixture
{
    param([string]$CaseMethod = $method)

    $tests = [Collections.Generic.List[object]]::new()
    $thinField = $CaseMethod -ceq 'LargeFiniteDensityInThinFieldRemainsVisible'
    $densities = if ($thinField) { @('1E+20', '1E+30') } else { @('1E-37', '1E-20', '1E-08', '1', '1E+30') }
    foreach ($backend in @('D3D12', 'Vulkan', 'Metal'))
    {
        foreach ($sampled in @('False', 'True'))
        {
            foreach ($density in $densities)
            {
                $opacities = if ($thinField) { @('0.5', '1') } else { @('') }
                foreach ($opacity in $opacities)
                {
                    $name = if ($thinField)
                    {
                        "$CaseMethod($backend, $sampled, $density, $opacity)"
                    }
                    else
                    {
                        "$CaseMethod($backend, $sampled, $density)"
                    }
                    $tests.Add([pscustomobject]@{
                        id = "OpenUsd.Rendering.ConformanceTests.$class.fixture.$CaseMethod.$($tests.Count)"
                        className = $class
                        methodName = $CaseMethod
                        displayName = $name
                        status = if ($backend -ceq 'Metal') { 'passed' } else { 'skipped' }
                        startTime = $started.AddSeconds(1).ToString('O')
                        exception = $null
                    })
                }
            }
        }
    }
    $passed = if ($thinField) { 8 } else { 10 }
    $summary = {
        [pscustomobject]@{
            total = $tests.Count; passed = $passed; failed = 0; skipped = $tests.Count - $passed
            cancelled = 0; timedOut = 0; flaky = 0
        }
    }
    return [pscustomobject]@{
        schemaVersion = 1
        assemblyName = 'OpenUsd.Rendering.ConformanceTests'
        publicationGeneration = [Guid]::NewGuid().ToString('N')
        summary = & $summary
        groups = @([pscustomobject]@{
            namespace = 'OpenUsd.Rendering.ConformanceTests'
            className = $class
            summary = & $summary
            tests = $tests.ToArray()
        })
    }
}

function Assert-Rejected
{
    param([string]$Name, [object]$Report, [string]$Message, [string]$CaseMethod = $method)
    try
    {
        $null = Assert-MetalVolumeReproReport -Report $Report -StartedUtc $started `
            -FinishedUtc $finished -Method $CaseMethod
    }
    catch
    {
        if ($_.Exception.Message -notlike "*$Message*")
        {
            throw "$Name failed for the wrong reason: $($_.Exception.Message)"
        }
        return
    }
    throw "$Name should have been rejected."
}

$valid = New-Fixture
$result = Assert-MetalVolumeReproReport -Report $valid -StartedUtc $started -FinishedUtc $finished
if ($result.passed -ne 10 -or $result.skipped -ne 20)
{
    throw 'The valid fixture did not report the expected Metal and platform counts.'
}

$nan = New-Fixture
$row = @($nan.groups[0].tests | Where-Object {
    $_.displayName -ceq "$method(Metal, True, 1E+30)"
})[0]
$row.status = 'error'
$row.exception = [pscustomobject]@{ message = 'Component 4160 (pixel 1040, channel 0) was NaN.' }
foreach ($summary in @($nan.summary, $nan.groups[0].summary))
{
    $summary.passed = 9
    $summary.failed = 1
}
Assert-Rejected 'NaN reproduction' $nan 'REPRODUCED_METAL_VOLUME_NAN'

$otherFailure = New-Fixture
$row = @($otherFailure.groups[0].tests | Where-Object {
    $_.displayName -ceq "$method(Metal, True, 1E+30)"
})[0]
$row.status = 'error'
$row.exception = [pscustomobject]@{ message = 'The device could not be created.' }
foreach ($summary in @($otherFailure.summary, $otherFailure.groups[0].summary))
{
    $summary.passed = 9
    $summary.failed = 1
}
Assert-Rejected 'Unrelated failure' $otherFailure 'without the expected NaN'

$missing = New-Fixture
$missing.groups[0].tests = @($missing.groups[0].tests | Select-Object -Skip 1)
Assert-Rejected 'Missing case' $missing '30'

$duplicate = New-Fixture
$duplicate.groups[0].tests[1].displayName = $duplicate.groups[0].tests[0].displayName
Assert-Rejected 'Duplicate case' $duplicate 'duplicate'

$metalSkip = New-Fixture
@($metalSkip.groups[0].tests | Where-Object { $_.displayName -like '*(Metal, *' })[0].status = 'skipped'
Assert-Rejected 'Metal skip' $metalSkip 'Metal'

$otherPass = New-Fixture
$otherPass.groups[0].tests[0].status = 'passed'
Assert-Rejected 'Unexpected platform pass' $otherPass 'skipped'

$stale = New-Fixture
@($stale.groups[0].tests | Where-Object { $_.displayName -like '*(Metal, *' })[0].startTime =
    $started.AddSeconds(-1).ToString('O')
Assert-Rejected 'Stale Metal case' $stale 'execution'

$future = New-Fixture
@($future.groups[0].tests | Where-Object { $_.displayName -like '*(Metal, *' })[0].startTime =
    $finished.AddSeconds(1).ToString('O')
Assert-Rejected 'Future Metal case' $future 'execution'

$wrongMethod = New-Fixture
$wrongMethod.groups[0].tests[0].methodName = 'UnrelatedMethod'
Assert-Rejected 'Wrong method' $wrongMethod 'identity'

$wrongGeneration = New-Fixture
$wrongGeneration.publicationGeneration = 'not-a-guid'
Assert-Rejected 'Invalid generation' $wrongGeneration 'generation'

$wrongSummary = New-Fixture
$wrongSummary.summary.passed = 9
Assert-Rejected 'Wrong summary' $wrongSummary 'summary'

$thinMethod = 'LargeFiniteDensityInThinFieldRemainsVisible'
$thin = New-Fixture -CaseMethod $thinMethod
$thinResult = Assert-MetalVolumeReproReport -Report $thin -StartedUtc $started `
    -FinishedUtc $finished -Method $thinMethod
if ($thinResult.passed -ne 8 -or $thinResult.skipped -ne 16)
{
    throw 'The thin-field fixture did not report the expected Metal and platform counts.'
}

$thinNan = New-Fixture -CaseMethod $thinMethod
$row = @($thinNan.groups[0].tests | Where-Object {
    $_.displayName -ceq "$thinMethod(Metal, False, 1E+20, 1)"
})[0]
$row.status = 'error'
$row.exception = [pscustomobject]@{ message = 'Component 4160 (pixel 1040, channel 0) was NaN.' }
foreach ($summary in @($thinNan.summary, $thinNan.groups[0].summary))
{
    $summary.passed = 7
    $summary.failed = 1
}
Assert-Rejected 'Thin-field NaN reproduction' $thinNan 'REPRODUCED_METAL_VOLUME_NAN' $thinMethod

$thinMissing = New-Fixture -CaseMethod $thinMethod
$thinMissing.groups[0].tests = @($thinMissing.groups[0].tests | Select-Object -Skip 1)
Assert-Rejected 'Missing thin-field case' $thinMissing '24' $thinMethod

$thinUnexpected = New-Fixture -CaseMethod $thinMethod
$thinUnexpected.groups[0].tests[0].displayName = "$thinMethod(D3D12, False, 1E+20, 0.75)"
Assert-Rejected 'Unexpected thin-field opacity' $thinUnexpected 'identity' $thinMethod

$prefix = Join-Path ([IO.Path]::GetTempPath()) (
    'openusd-metal-volume-report-' + [Guid]::NewGuid().ToString('N'))
$source = "$prefix-source.json"
$destination = "$prefix-retained.json"
try
{
    [IO.File]::WriteAllText($source, 'source evidence')
    [IO.File]::WriteAllText($destination, 'existing evidence')
    $collisionRejected = $false
    try
    {
        Move-MetalVolumeReproReport -Source $source -Destination $destination
    }
    catch
    {
        if ($_.Exception.InnerException -isnot [IO.IOException])
        {
            throw
        }
        $collisionRejected = $true
    }
    if (-not $collisionRejected -or
        [IO.File]::ReadAllText($source) -cne 'source evidence' -or
        [IO.File]::ReadAllText($destination) -cne 'existing evidence')
    {
        throw 'A report retention collision must preserve both files.'
    }

    [IO.File]::Delete($destination)
    Move-MetalVolumeReproReport -Source $source -Destination $destination
    if ([IO.File]::Exists($source) -or
        [IO.File]::ReadAllText($destination) -cne 'source evidence')
    {
        throw 'A report retention retry must move the original evidence.'
    }
}
finally
{
    [IO.File]::Delete($source)
    [IO.File]::Delete($destination)
}

Write-Host 'Passed: 18, Failed: 0, Skipped: 0'
