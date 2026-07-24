#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
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
    throw "Linux package evidence tests require ABI 7, got $stormChildAbiVersion."
}
$testRoot = Join-Path $repoRoot 'artifacts/linux-package-evidence-test'
$packageName = 'OpenUsd.Runtime.Imaging.linux-x64.0.0.0-test.nupkg'
$packagePath = Join-Path $testRoot $packageName
$validationPath = Join-Path $testRoot 'linux-native-validation.json'
$evidencePath = Join-Path $testRoot 'package-evidence.json'
$sourcePath = Join-Path $testRoot 'source.json'

function Add-ZipBytes
{
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][byte[]]$Bytes,
        [switch]$SymbolicLink
    )

    $entry = $Archive.CreateEntry($Path)
    if ($SymbolicLink)
    {
        $entry.ExternalAttributes = [BitConverter]::ToInt32(
            [BitConverter]::GetBytes([Convert]::ToUInt32('A1FF0000', 16)),
            0)
    }
    $stream = $entry.Open()
    try
    {
        $stream.Write($Bytes)
    }
    finally
    {
        $stream.Dispose()
    }
}

try
{
    Remove-Item $testRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
    $validation = [ordered]@{
        schemaVersion = 3
        rid = 'linux-x64'
        stormChildAbiVersion = $stormChildAbiVersion
        stormChildExports = @(
            'openusd_storm_child_get_abi_version',
            'openusd_storm_child_initialize_linux',
            'openusd_storm_child_render_v2',
            'openusd_storm_child_request_frame_v3',
            'openusd_storm_child_pick',
            'openusd_storm_child_set_selection',
            'openusd_storm_child_get_navigation_input',
            'openusd_storm_child_capture_framebuffer')
        runpathPolicy = [ordered]@{
            dynamicTag = 'DT_RUNPATH'
            allowedEntries = @('$ORIGIN')
            rejectLegacyRpath = $true
        }
        libraries = @(
            [ordered]@{
                name = 'libopenusd_storm_child.so'
                dynamicTag = 'DT_RUNPATH'
                runpathEntries = @('$ORIGIN')
                soname = 'libopenusd_storm_child.so.7'
            },
            [ordered]@{
                name = 'libopenusd_hydra.so'
                dynamicTag = 'DT_RUNPATH'
                runpathEntries = @('$ORIGIN')
                soname = 'libopenusd_hydra.so'
            },
            [ordered]@{
                name = 'libopenusd_hdsilk.so'
                dynamicTag = 'DT_RUNPATH'
                runpathEntries = @('$ORIGIN')
                soname = 'libopenusd_hdsilk.so'
            })
    }
    $stormBytes = [System.Text.Encoding]::UTF8.GetBytes('synthetic storm child')
    $stormHash = [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData($stormBytes))
    $validation.stormChildTopology = [ordered]@{
        soname = 'libopenusd_storm_child.so.7'
        linkName = 'libopenusd_storm_child.so'
        realFile = 'libopenusd_storm_child.so.7.0.0'
        realFileSize = $stormBytes.Length
        realFileSha256 = $stormHash
        entries = @(
            [ordered]@{
                name = 'libopenusd_storm_child.so'
                type = 'symlink'
                target = 'libopenusd_storm_child.so.7'
            },
            [ordered]@{
                name = 'libopenusd_storm_child.so.7'
                type = 'symlink'
                target = 'libopenusd_storm_child.so.7.0.0'
            },
            [ordered]@{
                name = 'libopenusd_storm_child.so.7.0.0'
                type = 'regular'
                target = $null
            })
    }
    $validation | ConvertTo-Json -Depth 7 |
        Set-Content $validationPath -Encoding utf8NoBOM
    $validationBytes = [System.IO.File]::ReadAllBytes($validationPath)

    Add-Type -AssemblyName System.IO.Compression
    $archive = [System.IO.Compression.ZipFile]::Open(
        $packagePath,
        [System.IO.Compression.ZipArchiveMode]::Create)
    try
    {
        Add-ZipBytes `
            -Archive $archive `
            -Path 'build/OpenUsd.Runtime.Imaging.linux-x64.native-validation.json' `
            -Bytes $validationBytes
        Add-ZipBytes `
            -Archive $archive `
            -Path 'runtimes/linux-x64/native/libopenusd_storm_child.so' `
            -Bytes ([System.Text.Encoding]::UTF8.GetBytes(
                'libopenusd_storm_child.so.7')) `
            -SymbolicLink
        Add-ZipBytes `
            -Archive $archive `
            -Path 'runtimes/linux-x64/native/libopenusd_storm_child.so.7' `
            -Bytes ([System.Text.Encoding]::UTF8.GetBytes(
                'libopenusd_storm_child.so.7.0.0')) `
            -SymbolicLink
        Add-ZipBytes `
            -Archive $archive `
            -Path 'runtimes/linux-x64/native/libopenusd_storm_child.so.7.0.0' `
            -Bytes $stormBytes
    }
    finally
    {
        $archive.Dispose()
    }

    $packageHash = (Get-FileHash $packagePath -Algorithm SHA256).Hash
    $validationHash = (Get-FileHash $validationPath -Algorithm SHA256).Hash
    $linkBytes = [System.Text.Encoding]::UTF8.GetBytes('libopenusd_storm_child.so.7')
    $linkHash = [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData($linkBytes))
    $sonameLinkBytes = [System.Text.Encoding]::UTF8.GetBytes(
        'libopenusd_storm_child.so.7.0.0')
    $sonameLinkHash = [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData($sonameLinkBytes))
    $evidence = [ordered]@{
        schemaVersion = 3
        rid = 'linux-x64'
        package = $packageName
        packageSize = (Get-Item $packagePath).Length
        packageSha256 = $packageHash
        nativeValidation = 'linux-native-validation.json'
        nativeValidationSha256 = $validationHash
        stormChildSoname = 'libopenusd_storm_child.so.7'
        stormChildRealFile = 'libopenusd_storm_child.so.7.0.0'
        stormChildRealFileSha256 = $stormHash
        stormChildEntries = @(
            [ordered]@{
                path = 'runtimes/linux-x64/native/libopenusd_storm_child.so'
                type = 'symlink'
                target = 'libopenusd_storm_child.so.7'
                size = $linkBytes.Length
                sha256 = $linkHash
            },
            [ordered]@{
                path = 'runtimes/linux-x64/native/libopenusd_storm_child.so.7'
                type = 'symlink'
                target = 'libopenusd_storm_child.so.7.0.0'
                size = $sonameLinkBytes.Length
                sha256 = $sonameLinkHash
            },
            [ordered]@{
                path = 'runtimes/linux-x64/native/libopenusd_storm_child.so.7.0.0'
                type = 'regular'
                target = $null
                size = $stormBytes.Length
                sha256 = $stormHash
            })
        execution = @(
            'PACKAGE_STORM_CHILD_EXECUTION_OK',
            "STORM_CHILD_ABI=$stormChildAbiVersion",
            'STORM_CHILD_CAPTURE_STATUS=1',
            'STORM_CHILD_NAVIGATION_STATUS=1',
            'STORM_CHILD_NAVIGATION_ERROR=A valid Storm native child is required.',
            'STORM_CHILD_NAVIGATION_RESET=true',
            'LD_LIBRARY_PATH_PRESENT=false',
            'PROJECT_OPENUSD_MAPS_CONFINED=true',
            'PROJECT_OPENUSD_MAP_COUNT=2',
            'STORM_CHILD_MAP_PUBLISH_ROOT=true',
            'OPENUSD_MAP_PUBLISH_ROOT=true',
            'CWD_IS_PUBLISH=true')
    }
    $evidence | ConvertTo-Json -Depth 7 |
        Set-Content $evidencePath -Encoding utf8NoBOM
    @{
        source = 'archive'
        rid = 'linux-x64'
        sha256 = 'A' * 64
    } | ConvertTo-Json | Set-Content $sourcePath -Encoding utf8NoBOM

    & (Join-Path $PSScriptRoot 'validate-linux-package-evidence.ps1') `
        -EvidencePath $evidencePath `
        -NativeSourceMetadataPath $sourcePath `
        -RequireArchiveSource

    $evidence.packageSha256 = '0' * 64
    $evidence | ConvertTo-Json -Depth 7 |
        Set-Content $evidencePath -Encoding utf8NoBOM
    $hashRejected = $false
    try
    {
        & (Join-Path $PSScriptRoot 'validate-linux-package-evidence.ps1') `
            -EvidencePath $evidencePath `
            -NativeSourceMetadataPath $sourcePath `
            -RequireArchiveSource
    }
    catch
    {
        if ($_.Exception.Message -notmatch 'package SHA-256')
        {
            throw
        }
        $hashRejected = $true
    }
    if (-not $hashRejected)
    {
        throw 'Malformed Linux package evidence hash was accepted.'
    }

    $archive = [System.IO.Compression.ZipFile]::Open(
        $packagePath,
        [System.IO.Compression.ZipArchiveMode]::Update)
    try
    {
        Add-ZipBytes `
            -Archive $archive `
            -Path 'runtimes/linux-x64/native/libopenusd_storm_child.so.7.99' `
            -Bytes $stormBytes
    }
    finally
    {
        $archive.Dispose()
    }
    $evidence.packageSize = (Get-Item $packagePath).Length
    $evidence.packageSha256 = (Get-FileHash $packagePath -Algorithm SHA256).Hash
    $evidence | ConvertTo-Json -Depth 7 |
        Set-Content $evidencePath -Encoding utf8NoBOM
    $topologyRejected = $false
    try
    {
        & (Join-Path $PSScriptRoot 'validate-linux-package-evidence.ps1') `
            -EvidencePath $evidencePath `
            -NativeSourceMetadataPath $sourcePath `
            -RequireArchiveSource
    }
    catch
    {
        if ($_.Exception.Message -notmatch 'Unexpected Storm child')
        {
            throw
        }
        $topologyRejected = $true
    }
    if (-not $topologyRejected)
    {
        throw 'An arbitrary Storm child .so.* package entry was accepted.'
    }

    Write-Output 'Synthetic Linux package evidence schema/hash/topology tests passed.'
}
finally
{
    Remove-Item $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
