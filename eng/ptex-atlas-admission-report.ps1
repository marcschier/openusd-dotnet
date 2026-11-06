#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

function Assert-PtexAtlasAdmissionReport
{
    param(
        [Parameter(Mandatory = $true)][object]$Report,
        [Parameter(Mandatory = $true)][DateTime]$StartedUtc,
        [Parameter(Mandatory = $true)][DateTime]$FinishedUtc
    )

    Set-StrictMode -Version Latest
    $class = 'SilkMaterialCommandTests'
    $method = 'MappedPtexAtlasAdmissionRollsBackAtCpuAndGpuBoundaries'
    $generation = [Guid]::Empty
    if ($Report.schemaVersion -ne 1 -or
        $Report.assemblyName -cne 'OpenUsd.Rendering.Tests' -or
        -not [Guid]::TryParseExact([string]$Report.publicationGeneration, 'N', [ref]$generation) -or
        $generation -eq [Guid]::Empty)
    {
        throw 'The Ptex admission report assembly, schema, or generation is invalid.'
    }

    $groups = @($Report.groups)
    if ($groups.Count -ne 1 -or
        $groups[0].namespace -cne 'OpenUsd.Rendering.Tests' -or
        $groups[0].className -cne $class)
    {
        throw 'The Ptex admission report does not identify the selected test class.'
    }

    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in @(
        "$method(252, 143, False)",
        "$method(251, 144, False)",
        "$method(252, 144, True)"))
    {
        $null = $expected.Add($name)
    }
    $tests = @($groups[0].tests)
    if ($tests.Count -ne $expected.Count)
    {
        throw 'The Ptex admission report must contain all three selected cases.'
    }

    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($test in $tests)
    {
        $name = [string]$test.displayName
        $id = [string]$test.id
        if ($test.className -cne $class -or $test.methodName -cne $method -or
            [string]::IsNullOrWhiteSpace($id) -or
            -not $id.StartsWith("OpenUsd.Rendering.Tests.$class.", [StringComparison]::Ordinal) -or
            -not ($id.Contains(".$method(", [StringComparison]::Ordinal) -or
                $id.Contains(".$method.", [StringComparison]::Ordinal)) -or
            -not $expected.Contains($name))
        {
            throw "An unexpected Ptex admission test identity was reported: $name"
        }
        if (-not $ids.Add($id) -or -not $names.Add($name))
        {
            throw "A duplicate Ptex admission test identity was reported: $name"
        }
        if ($test.status -cne 'passed')
        {
            throw "The Ptex admission test did not pass: $name"
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
            throw "The Ptex admission test has no valid execution start time: $name"
        }
        if ($caseStart -lt $StartedUtc.ToUniversalTime() -or
            $caseStart -gt $FinishedUtc.ToUniversalTime())
        {
            throw "The Ptex admission test is outside this execution: $name"
        }
    }

    foreach ($summary in @($Report.summary, $groups[0].summary))
    {
        if ($summary.total -ne 3 -or $summary.passed -ne 3 -or
            $summary.failed -ne 0 -or $summary.skipped -ne 0 -or
            $summary.cancelled -ne 0 -or $summary.timedOut -ne 0 -or $summary.flaky -ne 0)
        {
            throw 'The Ptex admission report summary does not match the selected cases.'
        }
    }
}
