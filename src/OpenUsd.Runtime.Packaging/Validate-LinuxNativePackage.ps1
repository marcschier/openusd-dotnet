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
. (Join-Path $PSScriptRoot 'LinuxElfValidation.ps1')
. (Join-Path $PSScriptRoot 'LinuxStormChildTopology.ps1')

$allowedRunpathEntries = @('$ORIGIN')
$requiredStormChildAbiVersion = 7

if (-not $IsLinux)
{
    throw 'Linux ELF package validation must run on Linux.'
}

$readElf = Get-Command readelf -ErrorAction SilentlyContinue
if ($null -eq $readElf)
{
    throw 'readelf is required to validate Linux runtime package inputs.'
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

function Get-LibraryEvidence
{
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [AllowNull()][string]$RequiredSoname = $null
    )

    if (-not (Test-Path $Path -PathType Leaf))
    {
        throw "Linux runtime package input is missing: $Path"
    }

    # stderr is deliberately not merged into the captured output. readelf writes
    # its dynamic section to stdout beginning with a blank line, and merging a
    # stderr warning into that stream under ErrorActionPreference Stop ends the
    # capture at the first error record, leaving just that blank line and a
    # binding failure that says nothing about what went wrong.
    $dynamic = @(& $readElf.Source --dynamic --wide $Path 2>$null)
    if ($LASTEXITCODE -ne 0)
    {
        $diagnostics = @(& $readElf.Source --dynamic --wide $Path 2>&1)
        throw "readelf could not inspect '$Path': $($diagnostics -join [Environment]::NewLine)"
    }

    if (-not ($dynamic | Where-Object { $_ -match '\S' }))
    {
        throw "readelf reported no dynamic section for '$Path'."
    }

    $dynamicEntries = @(Get-OpenUsdElfDynamicEntries -Lines $dynamic)
    $runpathEntries = @(Assert-OpenUsdElfRunpath `
        -DynamicEntries $dynamicEntries `
        -LibraryPath $Path `
        -AllowedEntries $allowedRunpathEntries)

    # A [string] parameter cannot actually hold $null: PowerShell coerces the
    # default to an empty string, so a null check here is always true and the
    # libraries that require no particular SONAME were still sent down the
    # assert path, where Assert-OpenUsdElfSoname rejects the empty value as a
    # mandatory argument. Test for content instead.
    $soname = if (-not [string]::IsNullOrEmpty($RequiredSoname))
    {
        Assert-OpenUsdElfSoname `
            -DynamicEntries $dynamicEntries `
            -LibraryPath $Path `
            -RequiredSoname $RequiredSoname
    }
    else
    {
        Get-OpenUsdElfDynamicValue `
            -DynamicEntries $dynamicEntries `
            -Tag 'SONAME'
    }
    if (-not [string]::IsNullOrWhiteSpace($soname) -and
        $soname -ne [System.IO.Path]::GetFileName($Path))
    {
        $sonamePath = Join-Path ([System.IO.Path]::GetDirectoryName($Path)) $soname
        if (-not (Test-Path $sonamePath -PathType Leaf))
        {
            throw "The SONAME dependency '$soname' is missing beside '$Path'."
        }
    }

    return [ordered]@{
        name = [System.IO.Path]::GetFileName($Path)
        dynamicTag = 'DT_RUNPATH'
        runpathEntries = $runpathEntries
        soname = $soname
    }
}

$stormChildTopology = Get-OpenUsdStormChildFileTopology -LibraryPath $StormChildLibrary
$symbols = @(& $readElf.Source --dyn-syms --wide $StormChildLibrary 2>$null)
if ($LASTEXITCODE -ne 0)
{
    $symbolDiagnostics = @(& $readElf.Source --dyn-syms --wide $StormChildLibrary 2>&1)
    throw (
        "readelf could not inspect Storm child exports '$StormChildLibrary': " +
        ($symbolDiagnostics -join [Environment]::NewLine))
}

$requiredExports = @(
    'openusd_storm_child_get_abi_version',
    'openusd_storm_child_initialize_linux',
    'openusd_storm_child_render_v2',
    'openusd_storm_child_request_frame_v3',
    'openusd_storm_child_pick',
    'openusd_storm_child_set_selection',
    'openusd_storm_child_get_navigation_input',
    'openusd_storm_child_capture_framebuffer')
foreach ($export in $requiredExports)
{
    if (-not ($symbols | Where-Object { $_ -match "\s$([regex]::Escape($export))$" }))
    {
        throw "The Linux Storm child does not export '$export': $StormChildLibrary"
    }
}

$libraries = @(
    (Get-LibraryEvidence `
        -Path $StormChildLibrary `
        -RequiredSoname $script:OpenUsdStormChildSoname),
    (Get-LibraryEvidence -Path $HydraLibrary),
    (Get-LibraryEvidence -Path $HdSilkLibrary))
$realStormChild = Get-Item -LiteralPath $stormChildTopology.realPath
$stormChildTopologyEvidence = [ordered]@{
    soname = $stormChildTopology.soname
    linkName = $stormChildTopology.linkName
    realFile = $stormChildTopology.realFile
    realFileSize = $realStormChild.Length
    realFileSha256 = (Get-FileHash `
        -LiteralPath $realStormChild.FullName `
        -Algorithm SHA256).Hash
    entries = $stormChildTopology.entries
}
$evidence = [ordered]@{
    schemaVersion = 3
    rid = 'linux-x64'
    stormChildAbiVersion = $stormChildAbiVersion
    stormChildExports = $requiredExports
    stormChildTopology = $stormChildTopologyEvidence
    runpathPolicy = [ordered]@{
        dynamicTag = 'DT_RUNPATH'
        allowedEntries = $allowedRunpathEntries
        rejectLegacyRpath = $true
    }
    libraries = $libraries
}

New-Item -ItemType Directory -Force -Path (
    [System.IO.Path]::GetDirectoryName($EvidencePath)) | Out-Null
$evidence | ConvertTo-Json -Depth 4 | Set-Content $EvidencePath -Encoding utf8NoBOM
Write-Output (
    "Validated Linux package ELF inputs: ABI $stormChildAbiVersion, " +
    "DT_SONAME $script:OpenUsdStormChildSoname, exact ABI-7 link topology, " +
    'dispatcher/picking/selection/navigation/capture exports, and exact DT_RUNPATH [$ORIGIN].')
