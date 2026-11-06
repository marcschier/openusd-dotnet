#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'ptex-atlas-admission-report.ps1')

$started = [DateTime]::UtcNow.AddSeconds(-5)
$finished = $started.AddSeconds(10)
$class = 'SilkMaterialCommandTests'
$method = 'MappedPtexAtlasAdmissionRollsBackAtCpuAndGpuBoundaries'

function New-Fixture
{
    $tests = [Collections.Generic.List[object]]::new()
    foreach ($arguments in @('252, 143, False', '251, 144, False', '252, 144, True'))
    {
        $tests.Add([pscustomobject]@{
            id = "OpenUsd.Rendering.Tests.$class.fixture.$method.$($tests.Count)"
            className = $class
            methodName = $method
            displayName = "$method($arguments)"
            status = 'passed'
            startTime = $started.AddSeconds(1).ToString('O')
        })
    }
    $summary = {
        [pscustomobject]@{
            total = 3; passed = 3; failed = 0; skipped = 0
            cancelled = 0; timedOut = 0; flaky = 0
        }
    }
    return [pscustomobject]@{
        schemaVersion = 1
        assemblyName = 'OpenUsd.Rendering.Tests'
        publicationGeneration = [Guid]::NewGuid().ToString('N')
        summary = & $summary
        groups = @([pscustomobject]@{
            namespace = 'OpenUsd.Rendering.Tests'
            className = $class
            summary = & $summary
            tests = $tests.ToArray()
        })
    }
}

function Assert-Rejected
{
    param([string]$Name, [object]$Report, [string]$Message)
    try
    {
        Assert-PtexAtlasAdmissionReport -Report $Report -StartedUtc $started -FinishedUtc $finished
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
Assert-PtexAtlasAdmissionReport -Report $valid -StartedUtc $started -FinishedUtc $finished
$parsed = $valid | ConvertTo-Json -Depth 10 | ConvertFrom-Json
Assert-PtexAtlasAdmissionReport -Report $parsed -StartedUtc $started -FinishedUtc $finished

$wrongAssembly = New-Fixture
$wrongAssembly.assemblyName = 'Other.Tests'
Assert-Rejected 'Wrong assembly' $wrongAssembly 'assembly'

$wrongGeneration = New-Fixture
$wrongGeneration.publicationGeneration = [Guid]::Empty.ToString('N')
Assert-Rejected 'Invalid generation' $wrongGeneration 'generation'

$wrongGroup = New-Fixture
$wrongGroup.groups[0].className = 'OtherTests'
Assert-Rejected 'Wrong group' $wrongGroup 'selected test class'

$missing = New-Fixture
$missing.groups[0].tests = @($missing.groups[0].tests | Select-Object -Skip 1)
Assert-Rejected 'Missing case' $missing 'three selected cases'

$duplicateName = New-Fixture
$duplicateName.groups[0].tests[1].displayName = $duplicateName.groups[0].tests[0].displayName
Assert-Rejected 'Duplicate case' $duplicateName 'duplicate'

$duplicateId = New-Fixture
$duplicateId.groups[0].tests[1].id = $duplicateId.groups[0].tests[0].id
Assert-Rejected 'Duplicate ID' $duplicateId 'duplicate'

$wrongMethod = New-Fixture
$wrongMethod.groups[0].tests[0].methodName = 'OtherMethod'
Assert-Rejected 'Wrong method' $wrongMethod 'identity'

$unexpected = New-Fixture
$unexpected.groups[0].tests[0].displayName = "$method(251, 143, False)"
Assert-Rejected 'Unexpected boundary' $unexpected 'identity'

$skipped = New-Fixture
$skipped.groups[0].tests[0].status = 'skipped'
Assert-Rejected 'Skipped case' $skipped 'did not pass'

$failed = New-Fixture
$failed.groups[0].tests[0].status = 'failed'
Assert-Rejected 'Failed case' $failed 'did not pass'

$stale = New-Fixture
$stale.groups[0].tests[0].startTime = $started.AddSeconds(-1).ToString('O')
Assert-Rejected 'Stale case' $stale 'outside this execution'

$future = New-Fixture
$future.groups[0].tests[0].startTime = $finished.AddSeconds(1).ToString('O')
Assert-Rejected 'Future case' $future 'outside this execution'

$badTime = New-Fixture
$badTime.groups[0].tests[0].startTime = 'not-a-time'
Assert-Rejected 'Invalid timestamp' $badTime 'valid execution start time'

$wrongSummary = New-Fixture
$wrongSummary.summary.passed = 2
Assert-Rejected 'Wrong overall summary' $wrongSummary 'summary'

$flaky = New-Fixture
$flaky.groups[0].summary.flaky = 1
Assert-Rejected 'Flaky group summary' $flaky 'summary'

Write-Host 'Ptex atlas admission report fixtures passed.'
