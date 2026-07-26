#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid,
    [ValidateSet('Full', 'Spirv', 'Metal')]
    [string]$ArtifactScope = 'Full',
    [string]$ToolRoot = (Join-Path $PSScriptRoot '.tools'),
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'out')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$manifestPath = Join-Path $PSScriptRoot 'shader-manifest.json'
$lockPath = Join-Path $PSScriptRoot 'toolchain.lock.json'
$ToolRoot = [System.IO.Path]::GetFullPath($ToolRoot)
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$commandPath = Join-Path $OutputRoot 'executed-commands.json'
if (-not (Test-Path $commandPath))
{
    throw "Executed shader command manifest is missing at $commandPath."
}
$plan = Get-Content $commandPath -Raw | ConvertFrom-Json

if (-not $Rid)
{
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    $Rid = if ($IsWindows -and $architecture -eq 'X64')
    {
        'win-x64'
    }
    elseif ($IsLinux -and $architecture -eq 'X64')
    {
        'linux-x64'
    }
    elseif ($IsMacOS -and $architecture -eq 'Arm64')
    {
        'osx-arm64'
    }
    else
    {
        throw "The shader toolchain does not support this host: $architecture."
    }
}

if ($Rid -eq 'linux-x64' -and $ArtifactScope -ne 'Spirv')
{
    throw 'Linux shader validation is restricted to the SPIR-V artifact scope.'
}

if ($Rid -eq 'osx-arm64' -and $ArtifactScope -ne 'Metal')
{
    throw 'macOS shader validation is restricted to the Metal artifact scope.'
}

# The Metal scope never emits SPIR-V, so there is nothing for spirv-val to check.
if ($ArtifactScope -ne 'Metal')
{
    $executableSuffix = if ($IsWindows) { '.exe' } else { '' }
    $validator = Join-Path `
        $ToolRoot `
        "$Rid/spirv-tools/bin/spirv-val$executableSuffix"
    if (-not (Test-Path $validator))
    {
        throw "spirv-val was not found at $validator. Run build-toolchain.ps1 first."
    }

    $validatorVersion = (
        & $validator --version 2>&1 |
            Select-Object -First 1
    ).ToString().Trim()
    if ($validatorVersion -notlike "*$($plan.toolchain.spirvToolsCommit)*")
    {
        throw "spirv-val is not stamped with commit $($plan.toolchain.spirvToolsCommit)."
    }

    Push-Location $repoRoot
    try
    {
        foreach ($program in $plan.programs)
        {
            & $validator ([string[]]$program.commands.spirvValidation.arguments)
            if ($LASTEXITCODE -ne 0)
            {
                exit $LASTEXITCODE
            }
        }
    }
    finally
    {
        Pop-Location
    }
}

$arguments = @(
    (Join-Path $PSScriptRoot 'scripts/verify-artifacts.py'),
    '--output-root', $OutputRoot,
    '--manifest', $manifestPath,
    '--lock', $lockPath,
    '--artifact-scope', $ArtifactScope.ToLowerInvariant()
)
if ($IsMacOS -and ($ArtifactScope -eq 'Full' -or $ArtifactScope -eq 'Metal'))
{
    $arguments += '--require-metallib'
}

& python @arguments
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

Write-Host "Verified $($plan.programs.Count) shader programs."
