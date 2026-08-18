#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LinuxStormChildTopology.ps1')

function New-Entry
{
    param(
        [string]$Name,
        [string]$Type,
        [AllowNull()][string]$Target = $null
    )

    return [ordered]@{ name = $Name; type = $Type; target = $Target }
}

function Assert-Rejected
{
    param(
        [object[]]$Entries,
        [string]$ExpectedMessage
    )

    try
    {
        Assert-OpenUsdStormChildTopology -Entries $Entries | Out-Null
    }
    catch
    {
        if ($_.Exception.Message -notmatch $ExpectedMessage)
        {
            throw
        }
        return
    }
    throw "Malformed Storm child topology was accepted: $ExpectedMessage"
}

$versioned = @(
    (New-Entry 'libopenusd_storm_child.so' 'symlink' 'libopenusd_storm_child.so.8'),
    (New-Entry 'libopenusd_storm_child.so.8' 'symlink' 'libopenusd_storm_child.so.8.0.0'),
    (New-Entry 'libopenusd_storm_child.so.8.0.0' 'regular'))

Assert-OpenUsdStormChildTopology -Entries $versioned | Out-Null
Assert-Rejected -Entries @($versioned[0]) -ExpectedMessage 'requires'
Assert-Rejected -Entries @(
    (New-Entry 'libopenusd_storm_child.so' 'regular'),
    $versioned[1],
    $versioned[2]) -ExpectedMessage 'must be a symbolic link'
Assert-Rejected -Entries @(
    (New-Entry 'libopenusd_storm_child.so' 'symlink' 'libopenusd_storm_child.so.8.1'),
    $versioned[1],
    $versioned[2]) -ExpectedMessage 'exact target'
Assert-Rejected -Entries @(
    $versioned[0],
    (New-Entry 'libopenusd_storm_child.so.8' 'symlink' 'libopenusd_storm_child.so.8.0'),
    (New-Entry 'libopenusd_storm_child.so.8.0' 'regular')) -ExpectedMessage 'Unexpected'
Assert-Rejected -Entries @(
    $versioned[0],
    $versioned[1],
    $versioned[2],
    (New-Entry 'libopenusd_storm_child.so.8.9' 'regular')) -ExpectedMessage 'Unexpected'
Assert-Rejected -Entries @(
    $versioned[0],
    (New-Entry 'libopenusd_storm_child.so.8' 'symlink' '/tmp/libopenusd_storm_child.so.8.1'),
    $versioned[2]) -ExpectedMessage 'exact target'

Write-Output 'Linux Storm child ABI-8 SONAME topology tests passed.'
