#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateSet('win-x64')]
    [string]$Rid = 'win-x64',
    [string]$ToolRoot = (Join-Path $PSScriptRoot '.tools'),
    [string]$CacheRoot = (Join-Path $PSScriptRoot '.cache')
)

$ErrorActionPreference = 'Stop'
$ToolRoot = [System.IO.Path]::GetFullPath($ToolRoot)
$CacheRoot = [System.IO.Path]::GetFullPath($CacheRoot)
$checkedRoot = Join-Path $PSScriptRoot 'checked'
$buildRoot = Join-Path $CacheRoot 'checked-build'
$publishedRoot = Join-Path $CacheRoot 'checked-publish'

if (-not $IsWindows)
{
    throw 'Checked deterministic artifacts must be verified on Windows win-x64.'
}
if (-not (Test-Path (Join-Path $checkedRoot 'manifest.json')))
{
    throw "Checked shader manifest is missing at $checkedRoot."
}

Remove-Item $buildRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $publishedRoot -Recurse -Force -ErrorAction SilentlyContinue
try
{
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

    $checkedFiles = Get-ChildItem $checkedRoot -File |
        ForEach-Object Name |
        Sort-Object
    $rebuiltFiles = Get-ChildItem $publishedRoot -File |
        ForEach-Object Name |
        Sort-Object
    $fileDifferences = Compare-Object $checkedFiles $rebuiltFiles
    if ($fileDifferences)
    {
        $fileDifferences | Format-Table | Out-String | Write-Host
        throw 'Checked and rebuilt shader file sets differ.'
    }

    foreach ($fileName in $checkedFiles)
    {
        $checkedPath = Join-Path $checkedRoot $fileName
        $rebuiltPath = Join-Path $publishedRoot $fileName
        $checkedHash = (Get-FileHash $checkedPath -Algorithm SHA256).Hash
        $rebuiltHash = (Get-FileHash $rebuiltPath -Algorithm SHA256).Hash
        if ($checkedHash -ne $rebuiltHash)
        {
            throw "Hash mismatch for checked shader artifact $fileName."
        }

        $checkedBytes = [System.IO.File]::ReadAllBytes($checkedPath)
        $rebuiltBytes = [System.IO.File]::ReadAllBytes($rebuiltPath)
        $equal = [System.Collections.StructuralComparisons]::StructuralEqualityComparer.Equals(
            $checkedBytes,
            $rebuiltBytes)
        if (-not $equal)
        {
            throw "Byte mismatch for checked shader artifact $fileName."
        }
    }

    Write-Host "Verified $($checkedFiles.Count) checked shader files."
}
finally
{
    Remove-Item $buildRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $publishedRoot -Recurse -Force -ErrorAction SilentlyContinue
}
