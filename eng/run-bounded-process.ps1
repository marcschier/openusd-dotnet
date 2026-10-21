#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [string[]]$Arguments = @(),
    [Parameter(Mandatory)][ulong]$MaximumCommitBytes,
    [Nullable[ulong]]$MaximumWorkingSetBytes,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$WorkingDirectory = (Get-Location).Path,
    [ValidateRange(1, 86400)][int]$TimeoutSeconds = 90
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'This process guard requires Windows.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a new output directory; prior evidence is not overwritten.' }
if (-not ('OpenUsd.Tooling.BoundedWindowsProcess' -as [type]))
{
    Add-Type -Path (Join-Path $PSScriptRoot 'BoundedWindowsProcess.cs')
}
New-Item -ItemType Directory -Path $output | Out-Null
$child = $null
$watch = [Diagnostics.Stopwatch]::StartNew()
try
{
    $child = [OpenUsd.Tooling.BoundedWindowsProcess]::Start(
        [IO.Path]::GetFullPath($Executable), $Arguments, [IO.Path]::GetFullPath($WorkingDirectory),
        (Join-Path $output 'stdout.log'), (Join-Path $output 'stderr.log'),
        $MaximumCommitBytes, $MaximumWorkingSetBytes)
    $completed = $child.WaitForExit($TimeoutSeconds * 1000)
    $result = [ordered]@{
        processId = $child.ProcessId
        completed = $completed
        exitCode = if ($completed) { $child.ExitCode } else { $null }
        elapsedSeconds = $watch.Elapsed.TotalSeconds
        maximumProcessCommitBytes = $MaximumCommitBytes
        maximumJobCommitBytes = $MaximumCommitBytes
        maximumProcessWorkingSetBytes = $MaximumWorkingSetBytes
        reportedPeakProcessMemoryUsedBytes = $child.PeakProcessMemoryUsedBytes
        reportedPeakJobMemoryUsedBytes = $child.PeakJobMemoryUsedBytes
        observedJobProcesses = $child.ObservedProcessCount
        reportedPeakExceedsConfiguredCommit = $child.PeakJobMemoryUsedBytes -gt $MaximumCommitBytes
        admission = 'Windows kernel job limits installed before resuming the suspended child; no breakaway.'
        exclusions = 'Not total host RAM: system cache, shared mappings and GPU/driver memory remain separate.'
        accounting = 'Raw Windows peak-memory counters are not asserted to be a strict historical commit bound.'
        executableSha256 = (Get-FileHash -LiteralPath $Executable).Hash
    }
    $result | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath (Join-Path $output 'receipt.json') -Encoding utf8
    [pscustomobject]$result
    if (-not $completed) { throw 'The bounded child exceeded its timeout; its owned job will be terminated.' }
    if ($child.ExitCode -ne 0) { throw "The bounded child exited with code $($child.ExitCode)." }
}
finally
{
    if ($null -ne $child) { $child.Dispose() }
}
