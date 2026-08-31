#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
#
# Classifies a backend's sampled volume evidence as executed, capability-skipped, or
# broken, and fails accordingly.
#
# The volume conformance class reports a capability skip whenever the native runtime or
# OpenUSD's hioOpenVDB field reader is absent, and a skip exits zero. Without this check an
# uploaded evidence artifact containing nothing but skip notes is indistinguishable from
# one containing measured deltas, and a backend could be promoted to "supported" from a run
# that never rendered a single volume pixel. This script is what makes "the workflow truly
# runs it" mechanically checkable instead of assumed.
#
# Two failure classes are deliberately kept apart. Missing or malformed evidence is a
# wiring fault -- the staging, the build, or the test filter is wrong -- and always fails.
# A capability skip is a documented native-profile outcome; -AllowCapabilitySkip downgrades
# it to a warning so a newly wired job can land without gambling on an unverified runner,
# while the written status file still records that the backend did not render. Dropping
# that switch is the promotion gate.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$Root,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$Backend,

    [switch]$AllowCapabilitySkip,

    # Present so the same script can assert the exact subset a job ran, rather than
    # demanding evidence for a gate that job never filtered in.
    [switch]$SkipUniformGate,

    [switch]$SkipSampledGate,

    [switch]$SkipDepthGate
)

$ErrorActionPreference = 'Stop'
$rootPath = [System.IO.Path]::GetFullPath($Root)
if (-not (Test-Path -LiteralPath $rootPath -PathType Container))
{
    throw "The volume evidence directory does not exist: $rootPath"
}

# Each entry is one evidence file and the metric keys its gate writes when it renders.
# The keys are exactly the ones VolumeRenderingConformanceTests asserts on, so a file that
# is present but truncated, or written by a skip path, cannot satisfy this.
$expected = @()
if (-not $SkipUniformGate)
{
    $expected += @{
        File = "volume-density-$Backend-gates.txt"
        Metrics = @(
            'volume-zero-vs-empty',
            'volume-unit-vs-empty',
            'volume-double-vs-unit')
        Positive = @('unitMeanRgb', 'doubleMeanRgb')
    }
}
if (-not $SkipSampledGate)
{
    $expected += @{
        File = "volume-vdb-$Backend-gates.txt"
        Metrics = @(
            'volume-vdb-nonuniform',
            'volume-vdb-sampled-vs-uniform',
            'volume-vdb-shifted-interior')
        Positive = @('varianceRgb', 'deltaVarianceRgb')
    }
}
if (-not $SkipDepthGate)
{
    $expected += @{
        File = "volume-depth-$Backend-gates.txt"
        Metrics = @(
            'volume-depth-slab-vs-uniform',
            'volume-depth-slab-vs-empty',
            'volume-depth-uniform-vs-empty')
        Positive = @('columnMean')
    }
}
if ($expected.Count -eq 0)
{
    throw 'Nothing to assert: the uniform, sampled, and depth gates were excluded.'
}

$wiringFailures = @()
$capabilitySkips = @()
foreach ($entry in $expected)
{
    $path = Join-Path $rootPath $entry.File
    if (-not (Test-Path -LiteralPath $path -PathType Leaf))
    {
        # No file at all means the gate never reached the point of reporting anything,
        # which is a wiring fault even when the underlying cause is a missing runtime:
        # the staging step ahead of it already verified and named the native input.
        $wiringFailures += "missing evidence file: $($entry.File)"
        continue
    }

    $lines = @(Get-Content -LiteralPath $path | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -eq 0)
    {
        $wiringFailures += "empty evidence file: $($entry.File)"
        continue
    }

    Write-Host "[volume-evidence] $($entry.File)"
    foreach ($line in $lines)
    {
        Write-Host "[volume-evidence]   $line"
    }

    $skipped = $false
    foreach ($line in $lines)
    {
        if ($line -match 'skipped|unavailable')
        {
            $capabilitySkips += "$($entry.File): $line"
            $skipped = $true
        }
    }
    if ($skipped)
    {
        continue
    }

    foreach ($metric in $entry.Metrics)
    {
        if (-not ($lines -match ('^' + [regex]::Escape($metric) + '\s')))
        {
            $wiringFailures += "$($entry.File) is missing the '$metric' measurement"
        }
    }

    # Every reported key must carry a finite number, and the keys that describe how much
    # the volume varied must be strictly positive. A gate that emitted the metric names
    # while rendering nothing fails here rather than passing on shape alone.
    $text = $lines -join "`n"
    foreach ($pair in [regex]::Matches($text, '(?<key>[A-Za-z][A-Za-z0-9]*)=(?<value>[^;\s]+)'))
    {
        $key = $pair.Groups['key'].Value
        $raw = $pair.Groups['value'].Value
        $value = 0.0
        $parsed = [double]::TryParse(
            $raw,
            [Globalization.NumberStyles]::Float,
            [Globalization.CultureInfo]::InvariantCulture,
            [ref]$value)
        if (-not $parsed)
        {
            $wiringFailures += "$($entry.File) has a non-numeric $key=$raw"
            continue
        }
        if ([double]::IsNaN($value) -or [double]::IsInfinity($value))
        {
            $wiringFailures += "$($entry.File) has a non-finite $key=$value"
            continue
        }
        if ($entry.Positive -contains $key -and $value -le 0)
        {
            $wiringFailures += "$($entry.File) has $key=$value; a rendered volume cannot be flat here"
        }
    }
}

$status = if ($wiringFailures.Count -ne 0)
{
    'broken'
}
elseif ($capabilitySkips.Count -ne 0)
{
    'capability-skip'
}
else
{
    'executed'
}

[ordered]@{
    schemaVersion = 1
    backend = $Backend
    status = $status
    wiringFailures = @($wiringFailures)
    capabilitySkips = @($capabilitySkips)
} | ConvertTo-Json -Depth 4 |
    Set-Content (Join-Path $rootPath "volume-evidence-$Backend-status.json")

foreach ($failure in $wiringFailures)
{
    Write-Host "::error::volume evidence: $failure"
}
if ($wiringFailures.Count -ne 0)
{
    throw ("The '$Backend' volume evidence is broken: " + ($wiringFailures -join '; '))
}

if ($capabilitySkips.Count -ne 0)
{
    $summary = "The '$Backend' volume gates did not render: " + ($capabilitySkips -join '; ')
    if (-not $AllowCapabilitySkip)
    {
        Write-Host "::error::volume evidence: $summary"
        throw $summary
    }
    Write-Host "::warning::volume evidence: $summary"
    Write-Host (
        "[volume-evidence] status=capability-skip. The '$Backend' backend must not be " +
        'promoted to a rendering support claim from this run.')
    exit 0
}

Write-Host (
    "[volume-evidence] status=executed. The '$Backend' volume gates rendered and " +
    'reported measured deltas.')
