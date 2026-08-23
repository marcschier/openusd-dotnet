#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$StormChildLibrary,
    [Parameter(Mandatory = $true)][string]$HydraLibrary,
    [Parameter(Mandatory = $true)][string]$HdSilkLibrary,
    [Parameter(Mandatory = $true)][string]$StormChildHeader,
    [Parameter(Mandatory = $true)][string]$EvidencePath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'MacOsNativeValidation.Common.ps1')
$requiredStormChildAbiVersion = 8

if (-not $IsMacOS)
{
    throw 'macOS Mach-O package validation must run on macOS.'
}

$otool = Get-Command otool -ErrorAction SilentlyContinue
$nm = Get-Command nm -ErrorAction SilentlyContinue
if ($null -eq $otool -or $null -eq $nm)
{
    throw 'otool and nm are required to validate macOS runtime package inputs.'
}

$header = Get-Content $StormChildHeader -Raw
$abiMatch = [regex]::Match(
    $header,
    'OPENUSD_STORM_CHILD_ABI_VERSION\s+(\d+)u?')
if (-not $abiMatch.Success)
{
    throw "The Storm child header does not declare an ABI version: $StormChildHeader"
}
$stormChildAbiVersion = [int]$abiMatch.Groups[1].Value
if ($stormChildAbiVersion -ne $requiredStormChildAbiVersion)
{
    throw (
        "The Storm child header must declare ABI version " +
        "$requiredStormChildAbiVersion`: $StormChildHeader")
}

$requiredExports = @(
    [regex]::Matches(
        $header,
        '\b(openusd_storm_child_[a-z0-9_]+)\s*\(') |
        ForEach-Object { $_.Groups[1].Value } |
        Where-Object { $_ -ne 'openusd_storm_child_initialize_linux' } |
        Sort-Object -Unique)
foreach ($requiredExport in @(
    'openusd_storm_child_get_abi_version',
    'openusd_storm_child_render_v2',
    'openusd_storm_child_request_frame_v3',
    'openusd_storm_child_pick',
    'openusd_storm_child_set_selection',
    'openusd_storm_child_set_transform_overrides',
    'openusd_storm_child_get_navigation_input',
    'openusd_storm_child_capture_framebuffer'))
{
    if ($requiredExports -notcontains $requiredExport)
    {
        throw "The current Storm child header is missing '$requiredExport'."
    }
}

function Get-MachOEvidence
{
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ExpectedInstallName,
        [switch]$ValidateStormExports
    )

    if (-not (Test-Path $Path -PathType Leaf))
    {
        throw "macOS runtime package input is missing: $Path"
    }

    $installNames = @(& $otool.Source -D $Path 2>&1)
    if ($LASTEXITCODE -ne 0)
    {
        throw "otool could not inspect the install name for '$Path'."
    }
    $installName = @($installNames | Select-Object -Skip 1)[0].Trim()
    if ($installName -ne $ExpectedInstallName)
    {
        throw (
            "macOS runtime package input '$Path' must use install name " +
            "'$ExpectedInstallName', not '$installName'.")
    }

    $loadCommands = @(& $otool.Source -l $Path 2>&1)
    if ($LASTEXITCODE -ne 0)
    {
        throw "otool could not inspect load commands for '$Path'."
    }
    $rpaths = @()
    for ($index = 0; $index -lt $loadCommands.Count; $index++)
    {
        if ($loadCommands[$index].Trim() -ne 'cmd LC_RPATH')
        {
            continue
        }
        for ($line = $index + 1;
             $line -lt [Math]::Min($index + 6, $loadCommands.Count);
             $line++)
        {
            $match = [regex]::Match(
                $loadCommands[$line],
                '^\s*path\s+(\S+)\s+\(offset')
            if ($match.Success)
            {
                $rpaths += $match.Groups[1].Value
                break
            }
        }
    }
    Assert-OpenUsdMacOsRPaths -RPaths $rpaths -Context $Path

    $dependencies = @(& $otool.Source -L $Path 2>&1)
    if ($LASTEXITCODE -ne 0)
    {
        throw "otool could not inspect dependencies for '$Path'."
    }
    $dependencyNames = @(
        $dependencies |
            Select-Object -Skip 1 |
            ForEach-Object {
                ([regex]::Match($_, '^\s*(\S+)\s+\(')).Groups[1].Value
            } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    foreach ($dependency in $dependencyNames)
    {
        Assert-OpenUsdMacOsDependency -Dependency $dependency -Context $Path
    }

    $exports = @()
    if ($ValidateStormExports)
    {
        $symbolLines = @(& $nm.Source -gjU $Path 2>&1)
        if ($LASTEXITCODE -ne 0)
        {
            throw "nm could not inspect Storm child exports '$Path'."
        }
        $exports = @($symbolLines | ForEach-Object { $_.TrimStart('_') })
        foreach ($requiredExport in $requiredExports)
        {
            if ($exports -notcontains $requiredExport)
            {
                throw "The macOS Storm child does not export '$requiredExport': $Path"
            }
        }
    }

    return [ordered]@{
        name = [System.IO.Path]::GetFileName($Path)
        installName = $installName
        rpaths = $rpaths
        dependencies = $dependencyNames
        exports = $exports
    }
}

$libraries = @(
    (Get-MachOEvidence `
        -Path $StormChildLibrary `
        -ExpectedInstallName '@rpath/libopenusd_storm_child.dylib' `
        -ValidateStormExports),
    (Get-MachOEvidence `
        -Path $HydraLibrary `
        -ExpectedInstallName '@rpath/libopenusd_hydra.dylib'),
    (Get-MachOEvidence `
        -Path $HdSilkLibrary `
        -ExpectedInstallName '@rpath/libopenusd_hdsilk.dylib'))

$evidence = [ordered]@{
    schemaVersion = 2
    rid = 'osx-arm64'
    rpathPolicy = [ordered]@{
        exactAllowlist = @('@loader_path')
        rejectRootedPaths = $true
        rejectSourceBuildInstallPaths = $true
    }
    stormChildAbiVersion = $stormChildAbiVersion
    stormChildExports = $requiredExports
    libraries = $libraries
}

New-Item -ItemType Directory -Force -Path (
    [System.IO.Path]::GetDirectoryName($EvidencePath)) | Out-Null
$evidence | ConvertTo-Json -Depth 6 | Set-Content $EvidencePath -Encoding utf8NoBOM
Write-Output (
    "Validated macOS package Mach-O inputs: ABI $stormChildAbiVersion, " +
    'current picking, selection, navigation, and capture exports, ' +
    '@rpath install names, and exact LC_RPATH [@loader_path].')
