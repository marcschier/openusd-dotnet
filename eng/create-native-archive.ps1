#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid,

    [string]$InstallRoot = (Join-Path $PSScriptRoot '../native/install'),

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [string]$MetadataOutputPath
)

$ErrorActionPreference = 'Stop'
$installPath = [System.IO.Path]::GetFullPath($InstallRoot)
$archivePath = [System.IO.Path]::GetFullPath($OutputPath)
$nativeRoot = Split-Path -Parent $installPath
$archiveBase = Split-Path -Parent $nativeRoot
$openUsdRoot = Join-Path $installPath $Rid
$shimRoot = Join-Path $installPath "shim/$Rid"
$installMetadataPath = Join-Path $openUsdRoot '.openusd-install-metadata.json'

if ((Split-Path -Leaf $installPath) -cne 'install' -or
    (Split-Path -Leaf $nativeRoot) -cne 'native')
{
    throw "InstallRoot must identify a 'native/install' directory: $installPath"
}
if (-not (Test-Path -LiteralPath $openUsdRoot -PathType Container) -or
    -not (Test-Path -LiteralPath $shimRoot -PathType Container))
{
    throw "The native install is incomplete for '$Rid' under '$installPath'."
}
& (Join-Path $PSScriptRoot 'native-install-metadata.ps1') `
    -Operation Verify `
    -Rid $Rid `
    -InstallRoot $installPath
if (-not (Test-Path -LiteralPath $installMetadataPath -PathType Leaf))
{
    throw "The native install metadata is missing: $installMetadataPath"
}

$archiveDirectory = Split-Path -Parent $archivePath
New-Item -ItemType Directory -Force -Path $archiveDirectory | Out-Null
Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
$members = @(
    "native/install/$Rid",
    "native/install/shim/$Rid"
)
$allowedPrefixes = @(
    "native/install/$Rid/",
    "native/install/shim/$Rid/"
)
if ($Rid -ceq 'win-x64')
{
    # The Windows Vulkan loader is built from locked source next to the per-RID
    # install rather than inside it, and packing OpenUsd.Runtime.Core.win-x64
    # requires it. Without it in the archive the win-x64 runtime packages can
    # only ever be packed on a Windows host that built the loader, which the
    # publish job is not. It is a verified output of this same native build, so
    # it travels with the bytes it was verified alongside.
    $vulkanSdkDirectories = @(
        Get-ChildItem -LiteralPath $installPath -Directory -Filter 'vulkan-sdk-*')
    if ($vulkanSdkDirectories.Count -ne 1)
    {
        throw (
            "Exactly one native/install/vulkan-sdk-* directory is required for " +
            "win-x64; found $($vulkanSdkDirectories.Count).")
    }

    $vulkanSdkName = $vulkanSdkDirectories[0].Name
    $members += "native/install/$vulkanSdkName"
    $allowedPrefixes += "native/install/$vulkanSdkName/"
}

& tar -czf $archivePath -C $archiveBase @members
if ($LASTEXITCODE -ne 0)
{
    throw "Could not create the native archive '$archivePath'."
}

$listedMembers = @(& tar -tf $archivePath)
if ($LASTEXITCODE -ne 0 -or $listedMembers.Count -eq 0)
{
    throw "Could not inspect the native archive '$archivePath'."
}
foreach ($member in $listedMembers)
{
    $normalized = $member.Replace('\', '/')
    if (-not ($allowedPrefixes | Where-Object {
            $normalized.StartsWith($_, [StringComparison]::Ordinal)
        }))
    {
        throw "The native archive contains an unexpected member: $member"
    }
}

$archiveSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
$installMetadataSha256 =
    (Get-FileHash -LiteralPath $installMetadataPath -Algorithm SHA256).Hash
$installMetadata = Get-Content -LiteralPath $installMetadataPath -Raw |
    ConvertFrom-Json
$sidecarPath = if ([string]::IsNullOrWhiteSpace($MetadataOutputPath))
{
    Join-Path $archiveDirectory 'native-artifact.json'
}
else
{
    [System.IO.Path]::GetFullPath($MetadataOutputPath)
}
$sidecarDirectory = Split-Path -Parent $sidecarPath
New-Item -ItemType Directory -Force -Path $sidecarDirectory | Out-Null
$sidecar = [ordered]@{
    schemaVersion = 1
    rid = $Rid
    archive = [System.IO.Path]::GetFileName($archivePath)
    archiveSha256 = $archiveSha256
    archiveLength = (Get-Item -LiteralPath $archivePath).Length
    installMetadataSha256 = $installMetadataSha256
    openUsdCommit = [string]$installMetadata.openUsdCommit
    lockSha256 = [string]$installMetadata.lockSha256
    dataAbiVersion = [int]$installMetadata.shimDataAbiVersion
    dataCapabilities = [long]$installMetadata.shimDataCapabilities
    stormAbiVersion = [int]$installMetadata.stormAbiVersion
    silkSessionAbiVersion = [int]$installMetadata.silkSessionAbiVersion
    silkPageAbiVersion = [int]$installMetadata.shimPageAbiVersion
    stormChildAbiVersion = [int]$installMetadata.stormChildAbiVersion
}
[System.IO.File]::WriteAllText(
    $sidecarPath,
    (($sidecar | ConvertTo-Json -Depth 4) + [Environment]::NewLine),
    [System.Text.UTF8Encoding]::new($false))

Write-Output (
    "NATIVE_ARCHIVE created rid=$Rid sha256=$archiveSha256 " +
    "archive=$archivePath metadata=$sidecarPath")
