#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$NativeMemoryProbe
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'These kernel guard checks require Windows.' }
if (-not [IO.Path]::IsPathFullyQualified($NativeMemoryProbe) -or
    -not (Test-Path -LiteralPath $NativeMemoryProbe -PathType Leaf))
{
    throw 'Build test-bounded-native-memory.cpp and pass its existing absolute executable path.'
}
$root = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $root) { throw 'Use a new test evidence directory.' }
New-Item -ItemType Directory -Path $root | Out-Null
$runner = Join-Path $PSScriptRoot 'run-bounded-process.ps1'
$childScript = Join-Path $PSScriptRoot 'test-bounded-process-child.ps1'
$pwsh = (Get-Process -Id $PID).Path
$common = @{ Executable = $pwsh; MaximumCommitBytes = 134217728; TimeoutSeconds = 20 }
function Arguments([string]$Mode, [string[]]$Values = @())
{
    @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $childScript, '-Mode', $Mode) + $Values
}
function Require([bool]$Condition, [string]$Message)
{
    if (-not $Condition) { throw $Message }
}
function RequireGone([int]$ProcessId)
{
    try { $process = [Diagnostics.Process]::GetProcessById($ProcessId) }
    catch [ArgumentException] { return }
    try { Require ($process.WaitForExit(5000)) "Owned process $ProcessId remained alive." }
    finally { $process.Dispose() }
}

$values = @('with spaces', 'with"quote', 'trailing\', '', 'quoted\"tail\')
$argsOutput = Join-Path $root 'arguments'
$result = & $runner @common -OutputDirectory $argsOutput -Arguments (Arguments 'arguments' $values)
Require ($result.completed -and $result.exitCode -eq 0) 'Argument child did not complete.'
$roundTrip = Get-Content -LiteralPath (Join-Path $argsOutput 'stdout.log') -Raw | ConvertFrom-Json
Require ($roundTrip.Count -eq $values.Count) 'Argument count changed.'
for ($index = 0; $index -lt $values.Count; $index++)
{
    Require ($roundTrip[$index] -ceq $values[$index]) "Argument $index changed."
}

$allocationOutput = Join-Path $root 'allocation'
$result = & $runner @common -OutputDirectory $allocationOutput -Arguments (Arguments 'allocate')
$allocation = Get-Content -LiteralPath (Join-Path $allocationOutput 'stdout.log') -Raw | ConvertFrom-Json
Require ($allocation.refused -and $allocation.allocatedBytes -gt 0) 'Native allocation did not hit the kernel limit.'
Require ($allocation.allocatedBytes -lt $common.MaximumCommitBytes) 'Accepted native payload exceeded its limit.'
Require ($null -eq $result.maximumProcessWorkingSetBytes) 'An unrequested working-set guarantee was claimed.'

$invokeOutput = Join-Path $root 'native-descendant'
$result = & $runner -Executable $NativeMemoryProbe -MaximumCommitBytes $common.MaximumCommitBytes `
    -TimeoutSeconds 20 -OutputDirectory $invokeOutput
$allocation = Get-Content -LiteralPath (Join-Path $invokeOutput 'stdout.log') -Raw | ConvertFrom-Json
Require ($allocation.refused -and $allocation.allocatedBytes -gt 0) 'Native-invoked child bypassed allocation limits.'
Require ($result.observedJobProcesses -ge 2) 'The native-invoked descendant was not counted in the job.'
Require ($allocation.peakLivePrivateBytes -le $common.MaximumCommitBytes) 'Accepted private bytes exceeded the cap.'
Require ($allocation.allocatedBytes + 33554432 -lt $common.MaximumCommitBytes) 'Parent/child payload bypassed the cap.'

$timeoutOutput = Join-Path $root 'timeout'
$failed = $false
try
{
    & $runner -Executable $pwsh -MaximumCommitBytes $common.MaximumCommitBytes -TimeoutSeconds 1 `
        -OutputDirectory $timeoutOutput -Arguments (Arguments 'sleep') | Out-Null
}
catch { $failed = $true }
Require $failed 'A timed-out child was reported as successful.'
$timeout = Get-Content -LiteralPath (Join-Path $timeoutOutput 'receipt.json') -Raw | ConvertFrom-Json
Require (-not $timeout.completed) 'A timed-out receipt reported completion.'
RequireGone $timeout.processId

$exitOutput = Join-Path $root 'nonzero'
$failed = $false
try { & $runner @common -OutputDirectory $exitOutput -Arguments (Arguments 'exit') | Out-Null }
catch { $failed = $true }
Require $failed 'A nonzero child exit was reported as successful.'
$exitReceipt = Get-Content -LiteralPath (Join-Path $exitOutput 'receipt.json') -Raw | ConvertFrom-Json
Require ($exitReceipt.exitCode -eq 7) 'The original child exit code was lost.'

$descendantOutput = Join-Path $root 'descendant'
$result = & $runner @common -OutputDirectory $descendantOutput -Arguments (Arguments 'descendant')
$descendant = [int](Get-Content -LiteralPath (Join-Path $descendantOutput 'stdout.log') -Raw).Trim()
RequireGone $descendant
RequireGone $result.processId
Require ($result.observedJobProcesses -ge 2) 'The Start-Process descendant did not join the job.'

$stopOutput = Join-Path $root 'explicit-stop'
New-Item -ItemType Directory -Path $stopOutput | Out-Null
$owned = [OpenUsd.Tooling.BoundedWindowsProcess]::Start(
    $pwsh, (Arguments 'sleep'), (Get-Location).Path,
    (Join-Path $stopOutput 'stdout.log'), (Join-Path $stopOutput 'stderr.log'),
    $common.MaximumCommitBytes, $null)
try
{
    $owned.Stop()
    $owned.Stop()
    Require ($owned.WaitForExit(0)) 'Explicit stop did not complete.'
    RequireGone $owned.ProcessId
}
finally { $owned.Dispose() }
$owned.Dispose()
$owned.Stop()

$treeStopOutput = Join-Path $root 'explicit-tree-stop'
New-Item -ItemType Directory -Path $treeStopOutput | Out-Null
$ownedTree = [OpenUsd.Tooling.BoundedWindowsProcess]::Start(
    $pwsh, (Arguments 'descendant'), (Get-Location).Path,
    (Join-Path $treeStopOutput 'stdout.log'), (Join-Path $treeStopOutput 'stderr.log'),
    $common.MaximumCommitBytes, $null)
try
{
    Require ($ownedTree.WaitForExit(10000)) 'The descendant-launching parent did not exit.'
    $treeChild = [int](Get-Content -LiteralPath (Join-Path $treeStopOutput 'stdout.log') -Raw).Trim()
    $ownedTree.Stop()
    RequireGone $treeChild
}
finally { $ownedTree.Dispose() }

$workingSetOutput = Join-Path $root 'working-set'
try
{
    $result = & $runner @common -OutputDirectory $workingSetOutput -Arguments (Arguments 'arguments' @('bounded')) `
        -MaximumWorkingSetBytes 67108864
    Require ($result.maximumProcessWorkingSetBytes -eq 67108864) 'Requested working-set limit was dropped.'
    $workingSetStatus = 'enforced'
}
catch
{
    $failure = $_.Exception.GetBaseException()
    if ($failure -isnot [ComponentModel.Win32Exception] -or $failure.NativeErrorCode -ne 1314) { throw }
    Require (-not (Test-Path -LiteralPath (Join-Path $workingSetOutput 'stdout.log'))) `
        'A child was started after privileged limit setup failed.'
    $workingSetStatus = 'privilege-refused-before-start'
}
[ordered]@{
    status = 'passed'
    cases = @('argument-roundtrip', 'native-commit-refusal', 'timeout-cleanup', 'nonzero-exit',
        'descendant-cleanup', 'native-invocation-commit-limit', 'idempotent-stop', 'explicit-working-set-limit',
        'explicit-stop-after-parent-exit')
    workingSetStatus = $workingSetStatus
    maximumCommitBytes = $common.MaximumCommitBytes
    sourceSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'BoundedWindowsProcess.cs')).Hash
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'summary.json') -Encoding utf8
Write-Output 'Passed: 9 bounded-process checks; no warehouse or topology data was loaded.'
