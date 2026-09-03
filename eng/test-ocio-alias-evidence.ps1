#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
<#
.SYNOPSIS
    Requires the OpenColorIO symbolic-link alias identity test to have actually executed.

.DESCRIPTION
    The alias test proves that a process list reached through a link whose name says
    nothing about its content is still detected, followed, and canonicalized to one
    dependency. Substituting a copy for the link would keep the test green while proving
    nothing, so the test refuses to do that and instead records whether it ran.

    On Linux, where unprivileged symbolic links are always available, this gate fails
    unless the evidence says the test executed -- so promotion evidence cannot pass
    without it. On Windows and macOS a recorded skip with a stated reason is accepted,
    because creating a symbolic link there needs a privilege an ordinary session lacks.
#>
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$Configuration = 'Release',

    [ValidateNotNullOrEmpty()]
    [string]$Framework = 'net10.0'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'tests/OpenUsd.Rendering.Tests/OpenUsd.Rendering.Tests.csproj'
$outputRoot = Join-Path $repoRoot "tests/OpenUsd.Rendering.Tests/bin/$Configuration/$Framework"
$evidencePath = Join-Path $outputRoot 'ocio-alias-evidence.txt'

<#
.SYNOPSIS
    Resolves the dotnet host to build with.

.DESCRIPTION
    The repository pins an SDK under .dotnet, and that host is preferred so the gate
    builds with the same toolchain everything else does. Invoking it without checking
    first is not a fallback: under Stop semantics a missing path raises a terminating
    CommandNotFoundException before any exit code can be inspected, so a runner without
    the local install failed with an error about a path rather than building at all.
    The path is therefore tested, the PATH host is used when it is absent, and a host
    that is nowhere to be found is reported as exactly that.
#>
function Resolve-DotnetHost
{
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $executable = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'dotnet.exe' } else { 'dotnet' }
    $pinned = Join-Path (Join-Path $RepositoryRoot '.dotnet') $executable
    if (Test-Path -LiteralPath $pinned -PathType Leaf)
    {
        return $pinned
    }

    $onPath = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -ne $onPath)
    {
        return $onPath.Source
    }

    throw (
        "No dotnet host was found. The repository-pinned host is absent at $pinned and " +
        "no 'dotnet' executable is on PATH, so OpenUsd.Rendering.Tests cannot be built " +
        'and the OpenColorIO alias evidence cannot be produced.')
}

$dotnet = Resolve-DotnetHost -RepositoryRoot $repoRoot
Write-Host "[ocio-alias-evidence] host=$dotnet"

if (Test-Path -LiteralPath $evidencePath -PathType Leaf)
{
    Remove-Item -LiteralPath $evidencePath -Force
}

$testFailure = $null
try
{
    # A clean runner has no build output, and run-managed-tests.ps1 runs a built test
    # assembly rather than building one. Building explicitly here is what makes this gate
    # usable as the first thing a Linux job runs.
    & $dotnet build $project -c $Configuration -f $Framework
    if ($LASTEXITCODE -ne 0)
    {
        throw "Building OpenUsd.Rendering.Tests exited with code $LASTEXITCODE."
    }

    & (Join-Path $PSScriptRoot 'run-managed-tests.ps1') `
        -Project $project `
        -Framework $Framework `
        -Configuration $Configuration `
        -TestArguments @(
            '--treenode-filter',
            '/*/*/SilkDisplayTransformNativeTests/ConfigIdentity_FollowsAProcessListReachedThroughASymbolicLink')
    if ($LASTEXITCODE -ne 0)
    {
        $testFailure = "The OpenColorIO alias identity test exited with code $LASTEXITCODE."
    }
}
catch
{
    # A recorded skip makes the shared runner report zero passing tests, which it treats
    # as a failure. That is the right default everywhere else, so the decision is taken
    # from the evidence below rather than by relaxing the runner.
    $testFailure = $_.Exception.Message
}

if (-not (Test-Path -LiteralPath $evidencePath -PathType Leaf))
{
    if ($null -ne $testFailure)
    {
        throw $testFailure
    }
    throw (
        'The OpenColorIO alias identity test produced no evidence at ' +
        "$evidencePath. It neither executed nor recorded a reason, which on any host " +
        'means the alias behaviour is unproven.')
}

$evidence = @{}
foreach ($line in Get-Content -LiteralPath $evidencePath)
{
    if ($line -match '^(?<key>[a-z]+)=(?<value>.*)$')
    {
        $evidence[$Matches['key']] = $Matches['value']
    }
}

$status = $evidence['status']
$mechanism = $evidence['mechanism']
$reason = $evidence['reason']
Write-Host (
    "OCIO_ALIAS_EVIDENCE status=$status mechanism=$mechanism " +
    "os=$($evidence['os']) reason=$reason")

if ($status -eq 'executed')
{
    if ($null -ne $testFailure)
    {
        throw $testFailure
    }
    if ($mechanism -ne 'symlink')
    {
        throw (
            "The alias evidence claims execution through '$mechanism' rather than a " +
            'symbolic link. A copy is not an alias and proves nothing.')
    }
    Write-Host 'OpenColorIO alias identity evidence: executed through a symbolic link.'
    exit 0
}

if ($IsLinux)
{
    throw (
        'The OpenColorIO alias identity test did not execute on Linux, where ' +
        "unprivileged symbolic links are available. Reason recorded: $reason")
}

if ([string]::IsNullOrWhiteSpace($reason))
{
    throw 'The alias identity test recorded a skip without a reason.'
}

Write-Host (
    'OpenColorIO alias identity evidence: skipped on this platform because the host ' +
    "refused to create a symbolic link ($reason). Linux CI requires execution.")
exit 0
