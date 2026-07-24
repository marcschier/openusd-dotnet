#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'shared-stage-soak-identity.ps1')

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$scratch = Join-Path $repoRoot ".soak-identity-test-$PID"
$backendDirectory = Join-Path $scratch 'src/OpenUsd.Rendering.Silk.D3D12'
$backendSource = Join-Path $backendDirectory 'Mutation.cs'
$privateDirectory = Join-Path $scratch 'native/private'
$privateHeader = Join-Path $privateDirectory 'openusd_render_camera_internal.h'
$artifactPath = Join-Path $scratch 'stale.json'
$oldSourceHash = $env:OPENUSD_SOAK_SOURCE_HASH
$oldExecutableHash = $env:OPENUSD_SOAK_EXECUTABLE_HASH
$oldExecutableTimestamp = $env:OPENUSD_SOAK_EXECUTABLE_TIMESTAMP_UTC
try
{
    New-Item -ItemType Directory -Force -Path $backendDirectory | Out-Null
    New-Item -ItemType Directory -Force -Path $privateDirectory | Out-Null
    Set-Content $backendSource 'internal static class Mutation { }' -NoNewline
    Set-Content $privateHeader 'camera contract' -NoNewline
    $before = Get-OpenUsdSoakSourceHash $scratch
    Add-Content $privateHeader ' changed' -NoNewline
    $privateAfter = Get-OpenUsdSoakSourceHash $scratch
    if ($before -eq $privateAfter)
    {
        throw 'A shared render camera contract mutation did not change the soak source identity.'
    }
    Set-Content $privateHeader 'camera contract' -NoNewline
    Add-Content $backendSource ' // changed' -NoNewline
    $after = Get-OpenUsdSoakSourceHash $scratch
    if ($before -eq $after)
    {
        throw 'A Silk backend source mutation did not change the soak source identity.'
    }

    $timestamp = [DateTimeOffset]::UtcNow
    $env:OPENUSD_SOAK_SOURCE_HASH = $after
    $env:OPENUSD_SOAK_EXECUTABLE_HASH = 'EXECUTABLE'
    $env:OPENUSD_SOAK_EXECUTABLE_TIMESTAMP_UTC = $timestamp.ToString('O')
    @{
        status = 'passed'
        sourceHash = $before
        executableHash = 'EXECUTABLE'
        executableTimestamp = $timestamp.ToString('O')
        resourcesReleased = $true
        targetedColorUpsertObserved = $true
        rendererFaultCount = 0
        memoryCheckpoints = @(1..20)
        postLossStormFrames = 1
    } | ConvertTo-Json -Depth 4 | Set-Content $artifactPath

    $rejected = $false
    try
    {
        Assert-OpenUsdSoakArtifact $artifactPath $true
    }
    catch
    {
        if ($_.Exception.Message -notmatch 'Stale soak source hash')
        {
            throw
        }
        $rejected = $true
    }
    if (-not $rejected)
    {
        throw 'Stale evidence survived a mutated included backend source.'
    }
}
finally
{
    $env:OPENUSD_SOAK_SOURCE_HASH = $oldSourceHash
    $env:OPENUSD_SOAK_EXECUTABLE_HASH = $oldExecutableHash
    $env:OPENUSD_SOAK_EXECUTABLE_TIMESTAMP_UTC = $oldExecutableTimestamp
    Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output 'Shared-stage soak identity mutation rejection passed.'
