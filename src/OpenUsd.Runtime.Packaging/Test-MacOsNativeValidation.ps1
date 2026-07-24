#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'MacOsNativeValidation.Common.ps1')

Assert-OpenUsdMacOsRPaths -RPaths @('@loader_path') -Context 'valid'

$invalidCases = @(
    @(),
    @('/absolute/native/install'),
    @('@loader_path', '@loader_path/../Frameworks'),
    @('@loader_path', '@loader_path'),
    @('@loader_path/../../src/project'),
    @('relative/build/output'))
foreach ($invalidCase in $invalidCases)
{
    $rejected = $false
    try
    {
        Assert-OpenUsdMacOsRPaths -RPaths $invalidCase -Context 'negative-test'
    }
    catch
    {
        $rejected = $true
    }
    if (-not $rejected)
    {
        throw "Unsafe LC_RPATH case was accepted: [$($invalidCase -join ', ')]."
    }
}

Assert-OpenUsdMacOsDependency `
    -Dependency '@rpath/libusd_ms.dylib' `
    -Context 'valid'
Assert-OpenUsdMacOsDependency `
    -Dependency '/System/Library/Frameworks/Metal.framework/Metal' `
    -Context 'valid'

foreach ($dependency in @(
    '/opt/openusd/lib/libusd_ms.dylib',
    '@loader_path/../../native/install/libusd_ms.dylib',
    'relative/build/libusd_ms.dylib'))
{
    $rejected = $false
    try
    {
        Assert-OpenUsdMacOsDependency -Dependency $dependency -Context 'negative-test'
    }
    catch
    {
        $rejected = $true
    }
    if (-not $rejected)
    {
        throw "Unsafe Mach-O dependency was accepted: $dependency"
    }
}

Write-Output 'macOS Mach-O LC_RPATH and dependency parser tests passed.'
