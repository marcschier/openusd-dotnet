#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [string]$EvidencePath = 'artifacts/package-linux-storm-child/package-evidence.json',
    [string]$NativeSourceMetadataPath = 'artifacts/native-input/linux-x64/source.json',
    [switch]$RequireArchiveSource
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $repoRoot 'src/OpenUsd.Runtime.Packaging/LinuxStormChildTopology.ps1')
$stormChildHeader = Join-Path $repoRoot 'native/openusd_storm_child/include/openusd_storm_child.h'
$stormChildHeaderText = Get-Content $stormChildHeader -Raw
$stormChildAbiMatch = [regex]::Match(
    $stormChildHeaderText,
    'OPENUSD_STORM_CHILD_ABI_VERSION\s+(\d+)u?')
if (-not $stormChildAbiMatch.Success)
{
    throw "Could not read the Storm child ABI from $stormChildHeader."
}
$stormChildAbiVersion = [int]$stormChildAbiMatch.Groups[1].Value
if ($stormChildAbiVersion -ne 7)
{
    throw "Storm child package evidence requires ABI 7, got $stormChildAbiVersion."
}

function Assert-FullSha256
{
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($Value -notmatch '^[0-9A-F]{64}$')
    {
        throw "$Name is not an uppercase full SHA-256 digest."
    }
}

function Get-ZipEntryHash
{
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Compression.ZipArchiveEntry]$Entry
    )

    $stream = $Entry.Open()
    try
    {
        return [Convert]::ToHexString(
            [System.Security.Cryptography.SHA256]::HashData($stream))
    }
    finally
    {
        $stream.Dispose()
    }
}

function Get-ZipEntryBytes
{
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Compression.ZipArchiveEntry]$Entry
    )

    $stream = $Entry.Open()
    $memory = [System.IO.MemoryStream]::new()
    try
    {
        $stream.CopyTo($memory)
        return $memory.ToArray()
    }
    finally
    {
        $memory.Dispose()
        $stream.Dispose()
    }
}

function Test-ZipEntryIsSymbolicLink
{
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Compression.ZipArchiveEntry]$Entry
    )

    $attributes = [BitConverter]::ToUInt32(
        [BitConverter]::GetBytes([int32]$Entry.ExternalAttributes),
        0)
    $unixMode = ($attributes -shr 16) -band 0xFFFF
    return ($unixMode -band 0xF000) -eq 0xA000
}

if (-not (Test-Path $EvidencePath -PathType Leaf))
{
    throw "The required Linux package evidence is missing: $EvidencePath"
}
$artifactRoot = [System.IO.Path]::GetDirectoryName(
    [System.IO.Path]::GetFullPath($EvidencePath))
$evidence = Get-Content $EvidencePath -Raw | ConvertFrom-Json
if ([int]$evidence.schemaVersion -ne 3 -or $evidence.rid -ne 'linux-x64')
{
    throw 'Linux package evidence must use schema 3 for linux-x64.'
}

$packageName = [string]$evidence.package
if ([System.IO.Path]::GetFileName($packageName) -cne $packageName -or
    $packageName -notmatch '^OpenUsd\.Runtime\.Imaging\.linux-x64\..+\.nupkg$')
{
    throw "Linux package evidence has an invalid package name: $packageName"
}
$packagePath = Join-Path $artifactRoot $packageName
if (-not (Test-Path $packagePath -PathType Leaf))
{
    throw "The evidenced Linux package is missing: $packagePath"
}
Assert-FullSha256 -Value ([string]$evidence.packageSha256) -Name 'packageSha256'
$actualPackageHash = (Get-FileHash $packagePath -Algorithm SHA256).Hash
if ($actualPackageHash -cne [string]$evidence.packageSha256)
{
    throw 'The Linux package SHA-256 does not match package evidence.'
}
if ((Get-Item $packagePath).Length -ne [long]$evidence.packageSize)
{
    throw 'The Linux package size does not match package evidence.'
}

$validationName = [string]$evidence.nativeValidation
if ([System.IO.Path]::GetFileName($validationName) -cne $validationName)
{
    throw "Linux package evidence has an invalid validation name: $validationName"
}
$validationPath = Join-Path $artifactRoot $validationName
if (-not (Test-Path $validationPath -PathType Leaf))
{
    throw "The Linux native validation evidence is missing: $validationPath"
}
Assert-FullSha256 `
    -Value ([string]$evidence.nativeValidationSha256) `
    -Name 'nativeValidationSha256'
$actualValidationHash = (Get-FileHash $validationPath -Algorithm SHA256).Hash
if ($actualValidationHash -cne [string]$evidence.nativeValidationSha256)
{
    throw 'The Linux native validation SHA-256 does not match package evidence.'
}

$validation = Get-Content $validationPath -Raw | ConvertFrom-Json
if ([int]$validation.schemaVersion -ne 3 -or
    $validation.rid -ne 'linux-x64' -or
    [int]$validation.stormChildAbiVersion -ne $stormChildAbiVersion)
{
    throw (
        'Linux native validation must use schema 3, linux-x64, and ABI ' +
        "$stormChildAbiVersion.")
}
if ($validation.runpathPolicy.dynamicTag -ne 'DT_RUNPATH' -or
    -not [bool]$validation.runpathPolicy.rejectLegacyRpath -or
    @($validation.runpathPolicy.allowedEntries).Count -ne 1 -or
    @($validation.runpathPolicy.allowedEntries)[0] -cne '$ORIGIN')
{
    throw 'Linux native validation has an unexpected DT_RUNPATH policy.'
}

$requiredLibraries = @(
    'libopenusd_storm_child.so',
    'libopenusd_hydra.so',
    'libopenusd_hdsilk.so')
$libraries = @($validation.libraries)
if ($libraries.Count -ne $requiredLibraries.Count)
{
    throw 'Linux native validation must contain exactly three project libraries.'
}
$stormLibrary = @($libraries | Where-Object name -eq 'libopenusd_storm_child.so')
if ($stormLibrary.Count -ne 1 -or
    [string]$stormLibrary[0].soname -cne $script:OpenUsdStormChildSoname)
{
    throw "Linux native validation must require DT_SONAME $script:OpenUsdStormChildSoname."
}
$validationTopology = Assert-OpenUsdStormChildTopology `
    -Entries @($validation.stormChildTopology.entries)
if ([string]$validation.stormChildTopology.soname -cne $script:OpenUsdStormChildSoname -or
    [string]$validation.stormChildTopology.linkName -cne $validationTopology.linkName -or
    [string]$validation.stormChildTopology.realFile -cne $validationTopology.realFile)
{
    throw 'Linux native validation has inconsistent Storm child topology metadata.'
}
Assert-FullSha256 `
    -Value ([string]$validation.stormChildTopology.realFileSha256) `
    -Name 'Storm child real ELF sha256'
if ([long]$validation.stormChildTopology.realFileSize -le 0)
{
    throw 'Linux native validation has an invalid Storm child real ELF size.'
}
if ([string]$evidence.stormChildSoname -cne $script:OpenUsdStormChildSoname -or
    [string]$evidence.stormChildRealFile -cne $validationTopology.realFile -or
    [string]$evidence.stormChildRealFileSha256 -cne
        [string]$validation.stormChildTopology.realFileSha256)
{
    throw 'Linux package evidence does not match the validated Storm child identity.'
}
foreach ($libraryName in $requiredLibraries)
{
    $library = @($libraries | Where-Object name -eq $libraryName)
    if ($library.Count -ne 1 -or
        $library[0].dynamicTag -ne 'DT_RUNPATH' -or
        @($library[0].runpathEntries).Count -ne 1 -or
        @($library[0].runpathEntries)[0] -cne '$ORIGIN')
    {
        throw "Linux native validation is malformed for $libraryName."
    }
}

Add-Type -AssemblyName System.IO.Compression
$package = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
try
{
    $validationEntry = $package.GetEntry(
        'build/OpenUsd.Runtime.Imaging.linux-x64.native-validation.json')
    if ($null -eq $validationEntry -or
        (Get-ZipEntryHash $validationEntry) -cne $actualValidationHash)
    {
        throw 'The packaged Linux native validation manifest is missing or changed.'
    }

    $stormArchiveEntries = @($package.Entries | Where-Object {
        [System.IO.Path]::GetFileName($_.FullName) -like
            'libopenusd_storm_child.so*'
    })
    foreach ($stormArchiveEntry in $stormArchiveEntries)
    {
        if (-not $stormArchiveEntry.FullName.StartsWith(
            'runtimes/linux-x64/native/',
            [System.StringComparison]::Ordinal))
        {
            throw "Flattened Storm child package entry: $($stormArchiveEntry.FullName)"
        }
    }
    $archiveTopologyEntries = @($stormArchiveEntries | ForEach-Object {
        $isLink = Test-ZipEntryIsSymbolicLink -Entry $_
        [ordered]@{
            name = [System.IO.Path]::GetFileName($_.FullName)
            type = if ($isLink) { 'symlink' } else { 'regular' }
            target = if ($isLink) {
                [System.Text.Encoding]::UTF8.GetString((Get-ZipEntryBytes -Entry $_))
            } else {
                $null
            }
        }
    })
    $archiveTopology = Assert-OpenUsdStormChildTopology -Entries $archiveTopologyEntries
    if ($archiveTopology.realFile -cne $validationTopology.realFile)
    {
        throw 'The package Storm child topology differs from native validation.'
    }

    $stormEvidence = @($evidence.stormChildEntries)
    if ($stormEvidence.Count -ne $stormArchiveEntries.Count)
    {
        throw 'Linux package evidence does not cover the exact Storm child entry set.'
    }
    $stormEvidencePaths = @($stormEvidence | ForEach-Object { [string]$_.path })
    if (@($stormEvidencePaths | Select-Object -Unique).Count -ne
        $stormArchiveEntries.Count)
    {
        throw 'Linux package evidence contains duplicate or missing Storm child paths.'
    }
    foreach ($archiveEntry in $stormArchiveEntries)
    {
        if ($stormEvidencePaths -cnotcontains $archiveEntry.FullName)
        {
            throw "Linux package evidence omits '$($archiveEntry.FullName)'."
        }
    }
    foreach ($entryEvidence in $stormEvidence)
    {
        $entryPath = [string]$entryEvidence.path
        if ($entryPath -notmatch (
            '^runtimes/linux-x64/native/libopenusd_storm_child\.so(\..+)?$'))
        {
            throw "Unexpected Storm child package entry: $entryPath"
        }
        Assert-FullSha256 `
            -Value ([string]$entryEvidence.sha256) `
            -Name "$entryPath sha256"
        $entry = $package.GetEntry($entryPath)
        $isLink = $null -ne $entry -and (Test-ZipEntryIsSymbolicLink -Entry $entry)
        $expectedType = if ($isLink) { 'symlink' } else { 'regular' }
        $expectedTarget = if ($isLink) {
            [System.Text.Encoding]::UTF8.GetString((Get-ZipEntryBytes -Entry $entry))
        } else {
            $null
        }
        if ($null -eq $entry -or
            [string]$entryEvidence.type -cne $expectedType -or
            [string]$entryEvidence.target -cne [string]$expectedTarget -or
            $entry.Length -ne [long]$entryEvidence.size -or
            (Get-ZipEntryHash $entry) -cne [string]$entryEvidence.sha256)
        {
            throw "Storm child package evidence does not match $entryPath."
        }
    }

    $realEntryPath =
        'runtimes/linux-x64/native/' + [string]$archiveTopology.realFile
    $realEntry = $package.GetEntry($realEntryPath)
    if ($null -eq $realEntry -or
        (Test-ZipEntryIsSymbolicLink -Entry $realEntry) -or
        $realEntry.Length -ne [long]$validation.stormChildTopology.realFileSize -or
        (Get-ZipEntryHash $realEntry) -cne
            [string]$validation.stormChildTopology.realFileSha256)
    {
        throw 'The packaged Storm child real ELF does not match native validation.'
    }
}
finally
{
    $package.Dispose()
}

$requiredExecution = @(
    'PACKAGE_STORM_CHILD_EXECUTION_OK',
    "STORM_CHILD_ABI=$stormChildAbiVersion",
    'STORM_CHILD_CAPTURE_STATUS=1',
    'STORM_CHILD_NAVIGATION_STATUS=1',
    'STORM_CHILD_NAVIGATION_ERROR=A valid Storm native child is required.',
    'STORM_CHILD_NAVIGATION_RESET=true',
    'LD_LIBRARY_PATH_PRESENT=false',
    'PROJECT_OPENUSD_MAPS_CONFINED=true',
    'STORM_CHILD_MAP_PUBLISH_ROOT=true',
    'OPENUSD_MAP_PUBLISH_ROOT=true',
    'CWD_IS_PUBLISH=true')
foreach ($line in $requiredExecution)
{
    if (@($evidence.execution) -notcontains $line)
    {
        throw "Linux package execution evidence is missing '$line'."
    }
}
$mapCountLine = @($evidence.execution |
    Where-Object { $_ -match '^PROJECT_OPENUSD_MAP_COUNT=\d+$' })
if ($mapCountLine.Count -ne 1 -or [int]($mapCountLine[0] -split '=')[1] -lt 2)
{
    throw 'Linux package execution must map at least Storm child and OpenUSD.'
}

if ($RequireArchiveSource)
{
    if (-not (Test-Path $NativeSourceMetadataPath -PathType Leaf))
    {
        throw "Linux archive source metadata is missing: $NativeSourceMetadataPath"
    }
    $source = Get-Content $NativeSourceMetadataPath -Raw | ConvertFrom-Json
    Assert-FullSha256 -Value ([string]$source.sha256) -Name 'archive source sha256'
    if ($source.source -ne 'archive' -or $source.rid -ne 'linux-x64')
    {
        throw 'Linux package archive mode did not use an immutable linux-x64 archive.'
    }
}

Write-Output (
    'Validated Linux package evidence schema, hashes, ABI-7 DT_SONAME/link ' +
    'topology, navigation/capture, DT_RUNPATH, and maps.')
