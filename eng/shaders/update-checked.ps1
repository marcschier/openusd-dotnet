#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateSet('win-x64')]
    [string]$Rid = 'win-x64',
    [string]$CacheRoot = (Join-Path $PSScriptRoot '.cache'),
    [string]$ToolRoot = (Join-Path $PSScriptRoot '.tools'),
    [switch]$Offline,
    [switch]$RefreshDownloads
)

$ErrorActionPreference = 'Stop'
$CacheRoot = [System.IO.Path]::GetFullPath($CacheRoot)
$ToolRoot = [System.IO.Path]::GetFullPath($ToolRoot)
$checkedRoot = Join-Path $PSScriptRoot 'checked'
$buildRoot = Join-Path $CacheRoot 'checked-build'
$publishedRoot = Join-Path $CacheRoot 'checked-publish'

if (-not $IsWindows)
{
    throw 'Checked deterministic artifacts must be updated on Windows win-x64.'
}
if ($Offline -and $RefreshDownloads)
{
    throw '-Offline and -RefreshDownloads cannot be combined.'
}

& (Join-Path $PSScriptRoot 'test-checked-input-line-endings.ps1')

Remove-Item (Join-Path $CacheRoot 'sources') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $CacheRoot 'build') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $ToolRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $buildRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $publishedRoot -Recurse -Force -ErrorAction SilentlyContinue
if ($RefreshDownloads)
{
    Remove-Item (Join-Path $CacheRoot 'downloads') `
        -Recurse `
        -Force `
        -ErrorAction SilentlyContinue
}

$fetchArguments = @{
    Rid = $Rid
    CacheRoot = $CacheRoot
    ToolRoot = $ToolRoot
}
if ($Offline)
{
    $fetchArguments.Offline = $true
}

& (Join-Path $PSScriptRoot 'fetch-toolchain.ps1') @fetchArguments | Out-Null
& (Join-Path $PSScriptRoot 'build-toolchain.ps1') `
    -Rid $Rid `
    -CacheRoot $CacheRoot `
    -ToolRoot $ToolRoot | Out-Null
& (Join-Path $PSScriptRoot 'build-shaders.ps1') `
    -Rid $Rid `
    -ToolRoot $ToolRoot `
    -OutputRoot $buildRoot | Out-Null

& python (Join-Path $PSScriptRoot 'scripts/publish-checked.py') `
    --input-root $buildRoot `
    --output-root $publishedRoot `
    --manifest (Join-Path $PSScriptRoot 'shader-manifest.json') `
    --lock (Join-Path $PSScriptRoot 'toolchain.lock.json')
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

Remove-Item $checkedRoot -Recurse -Force -ErrorAction SilentlyContinue
Move-Item $publishedRoot $checkedRoot
Remove-Item $buildRoot -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Updated checked shader artifacts at $checkedRoot."
