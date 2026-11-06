#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

function Assert-PrimitiveIdTransportReport
{
    param(
        [Parameter(Mandatory = $true)][object]$Report,
        [Parameter(Mandatory = $true)]
        [ValidateSet('D3D12', 'Vulkan')][string]$Backend,
        [Parameter(Mandatory = $true)][DateTime]$StartedUtc,
        [Parameter(Mandatory = $true)][DateTime]$FinishedUtc,
        [switch]$ExactReport
    )

    Set-StrictMode -Version Latest
    $assembly = 'OpenUsd.Rendering.ConformanceTests'
    $class = 'SilkPrimitiveIdTransportConformanceTests'
    $method = 'SingleIndexedDrawPreservesPrimitiveIdsWithoutProvingPtex'
    $generation = [Guid]::Empty
    if ($Report.schemaVersion -ne 1 -or $Report.assemblyName -cne $assembly -or
        -not [Guid]::TryParseExact([string]$Report.publicationGeneration, 'N', [ref]$generation) -or
        $generation -eq [Guid]::Empty)
    {
        throw 'The primitive-ID report assembly, schema, or generation is invalid.'
    }
    if ($FinishedUtc.ToUniversalTime() -lt $StartedUtc.ToUniversalTime())
    {
        throw 'The primitive-ID execution interval is invalid.'
    }

    $groups = @($Report.groups | Where-Object {
        $_.namespace -ceq $assembly -and $_.className -ceq $class
    })
    if ($groups.Count -ne 1 -or ($ExactReport -and @($Report.groups).Count -ne 1))
    {
        throw 'The primitive-ID report does not identify exactly one selected test class.'
    }
    $tests = @($groups[0].tests | Where-Object { $_.methodName -ceq $method })
    if ($tests.Count -ne 3 -or ($ExactReport -and @($groups[0].tests).Count -ne 3))
    {
        throw 'The primitive-ID report must contain all three selected cases.'
    }

    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($boundary in @(255, 32767, 65535))
    {
        $null = $expected.Add("$method($Backend, $boundary)")
    }
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($test in $tests)
    {
        $name = [string]$test.displayName
        $id = [string]$test.id
        if ($test.className -cne $class -or [string]::IsNullOrWhiteSpace($id) -or
            -not $id.StartsWith("$assembly.$class.", [StringComparison]::Ordinal) -or
            -not ($id.Contains(".$method(", [StringComparison]::Ordinal) -or
                $id.Contains(".$method.", [StringComparison]::Ordinal)) -or
            -not $expected.Contains($name))
        {
            throw "An unexpected primitive-ID test identity was reported: $name"
        }
        if (-not $ids.Add($id) -or -not $names.Add($name))
        {
            throw "A duplicate primitive-ID test identity was reported: $name"
        }
        if ($test.status -cne 'passed')
        {
            throw "The $Backend primitive-ID test did not pass: $name"
        }
        try
        {
            $caseStart = if ($test.startTime -is [DateTime])
            {
                $test.startTime.ToUniversalTime()
            }
            elseif ($test.startTime -is [DateTimeOffset])
            {
                $test.startTime.UtcDateTime
            }
            else
            {
                [DateTimeOffset]::Parse(
                    [string]$test.startTime, [Globalization.CultureInfo]::InvariantCulture,
                    [Globalization.DateTimeStyles]::AssumeUniversal).UtcDateTime
            }
        }
        catch [FormatException]
        {
            throw "The primitive-ID test has no valid execution start time: $name"
        }
        if ($caseStart -lt $StartedUtc.ToUniversalTime() -or
            $caseStart -gt $FinishedUtc.ToUniversalTime())
        {
            throw "The primitive-ID test is outside this execution: $name"
        }
    }

    $summaries = @($groups[0].summary)
    if ($ExactReport)
    {
        $summaries += $Report.summary
    }
    foreach ($summary in $summaries)
    {
        if ($summary.total -ne 3 -or $summary.passed -ne 3 -or
            $summary.failed -ne 0 -or $summary.skipped -ne 0 -or
            $summary.cancelled -ne 0 -or $summary.timedOut -ne 0 -or $summary.flaky -ne 0)
        {
            throw 'The primitive-ID report summary does not match the selected cases.'
        }
    }
}
