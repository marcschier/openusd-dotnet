#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'primitive-id-transport-report.ps1')

$started = [DateTime]::UtcNow.AddSeconds(-1)
$finished = $started.AddSeconds(2)
$fixtureStart = $started.AddSeconds(1)
$assembly = 'OpenUsd.Rendering.ConformanceTests'
$class = 'SilkPrimitiveIdTransportConformanceTests'
$method = 'SingleIndexedDrawPreservesPrimitiveIdsWithoutProvingPtex'

function New-PrimitiveReport
{
    param([string]$Backend)

    $tests = @(
        foreach ($boundary in @(255, 32767, 65535))
        {
            [pscustomobject]@{
                id = "$assembly.$class.1.1.$method.$boundary"
                className = $class
                methodName = $method
                displayName = "$method($Backend, $boundary)"
                status = 'passed'
                startTime = $fixtureStart.ToString('o')
            }
        }
    )
    $summary = [pscustomobject]@{
        total = 3; passed = 3; failed = 0; skipped = 0
        cancelled = 0; timedOut = 0; flaky = 0
    }
    return [pscustomobject]@{
        schemaVersion = 1
        publicationGeneration = [Guid]::NewGuid().ToString('N')
        assemblyName = $assembly
        summary = $summary
        groups = @([pscustomobject]@{
            namespace = $assembly
            className = $class
            summary = $summary
            tests = $tests
        })
    }
}

function Assert-Refused
{
    param([object]$Report, [string]$Backend, [string]$Expected, [switch]$ExactReport)

    $failure = $null
    try
    {
        Assert-PrimitiveIdTransportReport -Report $Report -Backend $Backend `
            -StartedUtc $started -FinishedUtc $finished -ExactReport:$ExactReport
    }
    catch
    {
        $failure = $_
    }
    if ($null -eq $failure -or $failure.Exception.Message -notlike $Expected)
    {
        throw "Expected primitive-ID report rejection '$Expected'; got '$failure'."
    }
}

$valid = New-PrimitiveReport 'Vulkan'
Assert-PrimitiveIdTransportReport -Report $valid -Backend Vulkan `
    -StartedUtc $started -FinishedUtc $finished -ExactReport

$mixed = New-PrimitiveReport 'D3D12'
$mixed.groups += [pscustomobject]@{ namespace = $assembly; className = 'OtherTests' }
$mixed.summary = [pscustomobject]@{
    total = 4; passed = 4; failed = 0; skipped = 0
    cancelled = 0; timedOut = 0; flaky = 0
}
Assert-PrimitiveIdTransportReport -Report $mixed -Backend D3D12 `
    -StartedUtc $started -FinishedUtc $finished

$invalid = New-PrimitiveReport 'Vulkan'
$invalid.publicationGeneration = 'stale'
Assert-Refused $invalid Vulkan '*schema, or generation is invalid*' -ExactReport

$invalid = New-PrimitiveReport 'Vulkan'
$invalid.groups[0].tests[1].displayName = $invalid.groups[0].tests[0].displayName
Assert-Refused $invalid Vulkan '*duplicate primitive-ID test identity*' -ExactReport

$invalid = New-PrimitiveReport 'Vulkan'
$invalid.groups[0].tests[1].id = $invalid.groups[0].tests[0].id
Assert-Refused $invalid Vulkan '*duplicate primitive-ID test identity*' -ExactReport

$invalid = New-PrimitiveReport 'Vulkan'
$invalid.groups[0].tests[0].status = 'skipped'
Assert-Refused $invalid Vulkan '*did not pass*' -ExactReport

$invalid = New-PrimitiveReport 'D3D12'
$invalid.groups[0].tests[0].startTime = $started.AddSeconds(-1).ToString('o')
Assert-Refused $invalid D3D12 '*outside this execution*'

$invalid = New-PrimitiveReport 'D3D12'
$invalid.groups[0].tests[0].displayName = "$method(Vulkan, 255)"
Assert-Refused $invalid D3D12 '*unexpected primitive-ID test identity*'

$invalid = New-PrimitiveReport 'Vulkan'
$invalid.summary = [pscustomobject]@{
    total = 3; passed = 3; failed = 0; skipped = 0
    cancelled = 0; timedOut = 0; flaky = 1
}
Assert-Refused $invalid Vulkan '*summary does not match*' -ExactReport

$invalid = New-PrimitiveReport 'D3D12'
$invalid.summary = [pscustomobject]@{
    total = 3; passed = 3; failed = 0; skipped = 0
    cancelled = 0; timedOut = 0; flaky = 0
}
$invalid.groups[0].summary.flaky = 1
Assert-Refused $invalid D3D12 '*summary does not match*'

$invalid = New-PrimitiveReport 'Vulkan'
$invalid.groups[0].tests = @($invalid.groups[0].tests | Select-Object -First 2)
Assert-Refused $invalid Vulkan '*all three selected cases*' -ExactReport

Write-Host '[primitive-id-report] Passed report identity, freshness, and refusal fixtures.'
