#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid,

    [ValidateSet('build', 'archive')]
    [string]$Source = 'build',

    [string]$ArchiveUri,

    [string]$ArchiveSha256,

    [string]$PipelineRoot,

    [string]$InstallRoot,

    [string]$WorkRoot
)

$ErrorActionPreference = 'Stop'
$arguments = @{
    Rid = $Rid
    Source = $Source
}
if (-not [string]::IsNullOrWhiteSpace($InstallRoot))
{
    $arguments.InstallRoot = [System.IO.Path]::GetFullPath($InstallRoot)
}
if (-not [string]::IsNullOrWhiteSpace($WorkRoot))
{
    $arguments.WorkRoot = [System.IO.Path]::GetFullPath($WorkRoot)
}

if (-not [string]::IsNullOrWhiteSpace($PipelineRoot))
{
    if ($Source -cne 'archive')
    {
        throw 'A native pipeline artifact can be consumed only in archive mode.'
    }
    $pipelinePath = [System.IO.Path]::GetFullPath($PipelineRoot)
    $sidecarPath = Join-Path $pipelinePath 'native-artifact.json'
    $sidecar = Get-Content -LiteralPath $sidecarPath -Raw | ConvertFrom-Json
    if ([int]$sidecar.schemaVersion -ne 1 -or [string]$sidecar.rid -cne $Rid)
    {
        throw "The native pipeline sidecar does not describe '$Rid'."
    }

    $lockPath = Join-Path $PSScriptRoot 'openusd.lock.json'
    $lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
    $lockHash = (Get-FileHash -LiteralPath $lockPath -Algorithm SHA256).Hash
    if ([string]$sidecar.openUsdCommit -cne [string]$lock.openUsd.commit -or
        [string]$sidecar.lockSha256 -cne $lockHash)
    {
        throw 'The native pipeline sidecar does not match the current native lock.'
    }

    $archivePath = Join-Path $pipelinePath ([string]$sidecar.archive)
    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if ($actualHash -cne [string]$sidecar.archiveSha256)
    {
        throw "The native pipeline archive hash does not match its sidecar: $actualHash."
    }
    $arguments.ArchivePath = $archivePath
    $arguments.ArchiveSha256 = $actualHash
}
elseif ($Source -ceq 'archive')
{
    $arguments.ArchiveUri = $ArchiveUri
    $arguments.ArchiveSha256 = $ArchiveSha256
}

& (Join-Path $PSScriptRoot 'prepare-render-native.ps1') @arguments
