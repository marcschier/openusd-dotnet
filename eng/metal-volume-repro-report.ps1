#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

function Move-MetalVolumeReproReport
{
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $sourcePath = (Resolve-Path -LiteralPath $Source).ProviderPath
    $destinationPath = [IO.Path]::GetFullPath($Destination, (Get-Location).ProviderPath)
    [IO.File]::Move($sourcePath, $destinationPath)
}

function Assert-MetalVolumeReproReport
{
    param(
        [Parameter(Mandatory = $true)][object]$Report,
        [Parameter(Mandatory = $true)][DateTime]$StartedUtc,
        [Parameter(Mandatory = $true)][DateTime]$FinishedUtc,
        [ValidateSet(
            'OverlappingHighFiniteTintsMatchTheIndependentComposite',
            'LargeFiniteDensityInThinFieldRemainsVisible')]
        [string]$Method = 'OverlappingHighFiniteTintsMatchTheIndependentComposite'
    )

    $class = 'VolumeCameraJointPassConformanceTests'
    $method = $Method
    $generation = [Guid]::Empty
    if ($Report.schemaVersion -ne 1 -or
        $Report.assemblyName -cne 'OpenUsd.Rendering.ConformanceTests' -or
        -not [Guid]::TryParseExact([string]$Report.publicationGeneration, 'N', [ref]$generation) -or
        $generation -eq [Guid]::Empty)
    {
        throw 'The report assembly, schema, or publication generation is invalid.'
    }

    $groups = @($Report.groups)
    if ($groups.Count -ne 1 -or
        $groups[0].namespace -cne 'OpenUsd.Rendering.ConformanceTests' -or
        $groups[0].className -cne $class)
    {
        throw 'The report does not identify the selected test class.'
    }
    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $thinField = $method -ceq 'LargeFiniteDensityInThinFieldRemainsVisible'
    $densities = if ($thinField) { @('1E+20', '1E+30') } else { @('1E-37', '1E-20', '1E-08', '1', '1E+30') }
    foreach ($backend in @('D3D12', 'Vulkan', 'Metal'))
    {
        foreach ($sampled in @('False', 'True'))
        {
            foreach ($density in $densities)
            {
                if ($thinField)
                {
                    foreach ($opacity in @('0.5', '1'))
                    {
                        $null = $expected.Add("$method($backend, $sampled, $density, $opacity)")
                    }
                }
                else
                {
                    $null = $expected.Add("$method($backend, $sampled, $density)")
                }
            }
        }
    }
    $expectedPassed = if ($thinField) { 8 } else { 10 }
    $expectedSkipped = $expected.Count - $expectedPassed
    $tests = @($groups[0].tests)
    if ($tests.Count -ne $expected.Count)
    {
        throw "The report must contain all $($expected.Count) selected case identities."
    }

    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $failures = [Collections.Generic.List[string]]::new()
    $nanFailures = [Collections.Generic.List[string]]::new()
    $passed = 0
    $skipped = 0
    foreach ($test in $tests)
    {
        $name = [string]$test.displayName
        $id = [string]$test.id
        if ($test.className -cne $class -or $test.methodName -cne $method -or
            [string]::IsNullOrWhiteSpace($id) -or
            -not $id.StartsWith("OpenUsd.Rendering.ConformanceTests.$class.", [StringComparison]::Ordinal) -or
            -not ($id.Contains(".$method(", [StringComparison]::Ordinal) -or
                $id.Contains(".$method.", [StringComparison]::Ordinal)) -or
            -not $expected.Contains($name))
        {
            throw "An unexpected test identity was reported: $name"
        }
        if (-not $ids.Add($id) -or -not $names.Add($name))
        {
            throw "A duplicate test identity was reported: $name"
        }
        if (-not $name.StartsWith("$method(Metal, ", [StringComparison]::Ordinal))
        {
            if ($test.status -cne 'skipped')
            {
                throw "A non-Metal case must be skipped on macOS: $name"
            }
            $skipped++
            continue
        }
        if ($test.status -cne 'passed' -and $test.status -cne 'error' -and $test.status -cne 'failed')
        {
            throw "A Metal case must pass or report an error: $name"
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
        catch
        {
            throw "The Metal case has no valid execution start time: $name"
        }
        if ($caseStart -lt $StartedUtc.ToUniversalTime() -or $caseStart -gt $FinishedUtc.ToUniversalTime())
        {
            throw "The Metal case is outside this execution: $name"
        }
        if ($test.status -ceq 'passed')
        {
            $passed++
            continue
        }
        $failures.Add($name)
        if ($null -ne $test.exception -and
            [string]$test.exception.message -match 'Component \d+ \(pixel \d+, channel \d+\) was NaN\.')
        {
            $nanFailures.Add($name)
        }
    }

    foreach ($summary in @($Report.summary, $groups[0].summary))
    {
        if ($summary.total -ne $expected.Count -or $summary.passed -ne $passed -or
            $summary.failed -ne $failures.Count -or $summary.skipped -ne $skipped -or
            $summary.cancelled -ne 0 -or $summary.timedOut -ne 0 -or $summary.flaky -ne 0)
        {
            throw 'The report summary does not match the selected cases.'
        }
    }
    if ($nanFailures.Count -gt 0)
    {
        throw "REPRODUCED_METAL_VOLUME_NAN: $($nanFailures -join '; ')"
    }
    if ($failures.Count -gt 0)
    {
        throw "Metal cases failed without the expected NaN: $($failures -join '; ')"
    }
    if ($passed -ne $expectedPassed -or $skipped -ne $expectedSkipped)
    {
        throw "Exactly $expectedPassed Metal passes and $expectedSkipped platform skips are required."
    }
    return [pscustomobject]@{
        passed = $passed
        skipped = $skipped
        publicationGeneration = $Report.publicationGeneration
    }
}
