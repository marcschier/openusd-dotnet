#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$ProbeExecutable
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Host memory admission requires Windows.' }
$root = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $root) { throw 'Use a fresh evidence directory.' }
New-Item -ItemType Directory -Path $root | Out-Null
if (-not ('OpenUsd.Tooling.BoundedWindowsProcess' -as [type]))
{
    Add-Type -Path (Join-Path $PSScriptRoot 'BoundedWindowsProcess.cs')
}
$cases = @(
    @{ Name = 'both-exact'; Physical = 30ul; Commit = 40ul; PhysicalReserve = 20ul; CommitReserve = 30ul; Expected = $true },
    @{ Name = 'physical-one-short'; Physical = 29ul; Commit = 40ul; PhysicalReserve = 20ul; CommitReserve = 30ul; Expected = $false },
    @{ Name = 'commit-one-short'; Physical = 30ul; Commit = 39ul; PhysicalReserve = 20ul; CommitReserve = 30ul; Expected = $false },
    @{ Name = 'both-one-over'; Physical = 31ul; Commit = 41ul; PhysicalReserve = 20ul; CommitReserve = 30ul; Expected = $true },
    @{ Name = 'reserve-larger-than-available'; Physical = 0ul; Commit = 40ul; PhysicalReserve = 20ul; CommitReserve = 30ul; Expected = $false },
    @{ Name = 'commit-reserve-larger-than-available'; Physical = 30ul; Commit = 0ul; PhysicalReserve = 20ul; CommitReserve = 30ul; Expected = $false },
    @{ Name = 'unrequested-counters'; Physical = 0ul; Commit = 0ul; PhysicalReserve = $null; CommitReserve = $null; Expected = $true },
    @{ Name = 'physical-only'; Physical = 30ul; Commit = 0ul; PhysicalReserve = 20ul; CommitReserve = $null; Expected = $true },
    @{ Name = 'commit-only'; Physical = 0ul; Commit = 40ul; PhysicalReserve = $null; CommitReserve = 30ul; Expected = $true },
    @{ Name = 'zero-reserves'; Physical = 10ul; Commit = 10ul; PhysicalReserve = 0ul; CommitReserve = 0ul; Expected = $true },
    @{ Name = 'zero-reserve-short'; Physical = 9ul; Commit = 10ul; PhysicalReserve = 0ul; CommitReserve = 0ul; Expected = $false },
    @{ Name = 'overflow-refused'; Physical = [ulong]::MaxValue; Commit = [ulong]::MaxValue; PhysicalReserve = [ulong]::MaxValue; CommitReserve = 0ul; Expected = $false }
)
$results = [Collections.Generic.List[object]]::new()
foreach ($case in $cases)
{
    $snapshot = [OpenUsd.Tooling.BoundedWindowsProcess+HostMemorySnapshot]::new($case.Physical, $case.Commit)
    $actual = $snapshot.Admits(10ul, $case.PhysicalReserve, $case.CommitReserve)
    if ($actual -ne $case.Expected) { throw "Admission boundary failed: $($case.Name)" }
    $results.Add([ordered]@{ name = $case.Name; admitted = $actual; passed = $true })
}
$invalid = $false
try { [OpenUsd.Tooling.BoundedWindowsProcess+HostMemorySnapshot]::new(0, 0).Admits(0, $null, $null) }
catch
{
    if ($_.Exception.GetBaseException() -isnot [ArgumentOutOfRangeException]) { throw }
    $invalid = $true
}
if (-not $invalid) { throw 'A zero child ceiling was accepted.' }
$results.Add([ordered]@{ name = 'zero-child-ceiling'; passed = $true })
$observed = [OpenUsd.Tooling.BoundedWindowsProcess]::ReadHostMemory()
if ($observed.AvailablePhysicalBytes -eq 0 -or $observed.AvailableCommitBytes -eq 0)
{
    throw 'Live host counters are unexpectedly empty.'
}
$results.Add([ordered]@{ name = 'live-native-counter-query'; passed = $true })

# An impossible reserve must fail before an executable is inspected or a child is created.
foreach ($mode in @('both', 'physical', 'commit'))
{
    $refused = $false
    $deniedRoot = Join-Path $root "refused-$mode"
    $arguments = @{
        Executable = Join-Path $root 'must-not-exist.exe'
        MaximumCommitBytes = 1048576
        OutputDirectory = $deniedRoot
    }
    if ($mode -ne 'commit') { $arguments.PhysicalReserveBytes = [ulong]::MaxValue }
    if ($mode -ne 'physical') { $arguments.CommitReserveBytes = [ulong]::MaxValue }
    try
    {
        & (Join-Path $PSScriptRoot 'run-bounded-process.ps1') @arguments | Out-Null
    }
    catch
    {
        if ($_.Exception.GetBaseException() -isnot [InvalidOperationException] -or
            $_.Exception.Message -notlike '*Host memory admission refused before child creation*') { throw }
        $refused = $true
    }
    $denied = Get-Content -LiteralPath (Join-Path $deniedRoot 'host-admission.json') -Raw | ConvertFrom-Json
    if (-not $refused -or $denied.admitted -or
        (Test-Path -LiteralPath (Join-Path $deniedRoot 'stdout.log')) -or
        (Test-Path -LiteralPath (Join-Path $deniedRoot 'stderr.log')) -or
        (Test-Path -LiteralPath (Join-Path $deniedRoot 'receipt.json')))
    {
        throw 'Low-headroom refusal lost evidence or reached child creation.'
    }
    if ((($null -ne $denied.physicalReserveBytes) -ne ($mode -ne 'commit')) -or
        (($null -ne $denied.commitReserveBytes) -ne ($mode -ne 'physical')) -or
        $denied.maximumCommitBytes -ne 1048576)
    {
        throw 'The refusal receipt dropped or invented a requested admission limit.'
    }
    $results.Add([ordered]@{ name = "runner-$mode-refuses-before-child-creation"; passed = $true })
}
if ($ProbeExecutable)
{
    foreach ($mode in @('requested', 'legacy'))
    {
        $arguments = @{
            Executable = $ProbeExecutable
            Arguments = @('--version')
            MaximumCommitBytes = 134217728
            OutputDirectory = Join-Path $root $mode
            TimeoutSeconds = 20
        }
        if ($mode -eq 'requested')
        {
            $arguments.PhysicalReserveBytes = 0ul
            $arguments.CommitReserveBytes = 0ul
        }
        $result = & (Join-Path $PSScriptRoot 'run-bounded-process.ps1') @arguments
        if (-not $result.completed -or $result.exitCode -ne 0 -or
            (($null -ne $result.hostAdmission) -ne ($mode -eq 'requested')))
        {
            throw "The $mode runner admission mode changed child outcome or dropped its receipt."
        }
        $recordPath = Join-Path $arguments.OutputDirectory 'host-admission.json'
        if (($mode -eq 'requested') -ne (Test-Path -LiteralPath $recordPath))
        {
            throw "The $mode runner wrote incorrect host-admission evidence."
        }
        if ($mode -eq 'requested' -and (-not $result.hostAdmission.admitted -or
            $result.hostAdmission.physicalReserveBytes -ne 0 -or $result.hostAdmission.commitReserveBytes -ne 0))
        {
            throw 'Explicit zero reserves were not recorded as requested admission.'
        }
        $results.Add([ordered]@{ name = "$mode-runner-success"; passed = $true })
    }
}
[ordered]@{
    status = 'passed'
    cases = $results.ToArray()
    observed = $observed
    sourceSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'BoundedWindowsProcess.cs')).Hash
    runnerSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'run-bounded-process.ps1')).Hash
    probeExecutable = $ProbeExecutable
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $root 'summary.json')
Write-Output "Host memory admission: $($results.Count) checks passed."
