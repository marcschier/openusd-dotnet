#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid,
    [string]$CacheRoot = (Join-Path $PSScriptRoot '.cache'),
    [string]$ToolRoot = (Join-Path $PSScriptRoot '.tools'),
    [switch]$Offline,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$lock = Get-Content (Join-Path $PSScriptRoot 'toolchain.lock.json') -Raw |
    ConvertFrom-Json
$CacheRoot = [System.IO.Path]::GetFullPath($CacheRoot)
$ToolRoot = [System.IO.Path]::GetFullPath($ToolRoot)

function Get-HostRid
{
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    if ($IsWindows -and $architecture -eq 'X64')
    {
        return 'win-x64'
    }
    if ($IsLinux -and $architecture -eq 'X64')
    {
        return 'linux-x64'
    }
    if ($IsMacOS -and $architecture -eq 'Arm64')
    {
        return 'osx-arm64'
    }

    throw "The shader toolchain does not support this host: $architecture."
}

function Get-VerifiedArchive
{
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$ArchiveName,
        [Parameter(Mandatory = $true)][string]$Sha256
    )

    $path = Join-Path $CacheRoot "downloads/$ArchiveName"
    if (Test-Path $path)
    {
        $actual = (Get-FileHash $path -Algorithm SHA256).Hash
        if ($actual -eq $Sha256)
        {
            Write-Host "Verified $ArchiveName"
            return $path
        }
        if (-not $Force)
        {
            throw "Hash mismatch for $path. Expected $Sha256, got $actual."
        }

        Remove-Item $path -Force
    }

    if ($Offline)
    {
        throw "Offline cache miss: $path"
    }

    $partial = "$path.partial"
    Remove-Item $partial -Force -ErrorAction SilentlyContinue
    Write-Host "Downloading $Url"
    Invoke-WebRequest -Uri $Url -OutFile $partial
    $actual = (Get-FileHash $partial -Algorithm SHA256).Hash
    if ($actual -ne $Sha256)
    {
        Remove-Item $partial -Force
        throw "Hash mismatch for $Url. Expected $Sha256, got $actual."
    }

    Move-Item $partial $path
    return $path
}

if (-not $Rid)
{
    $Rid = Get-HostRid
}

New-Item -ItemType Directory -Force -Path `
    (Join-Path $CacheRoot 'downloads'), `
    (Join-Path $CacheRoot 'sources'), `
    (Join-Path $ToolRoot $Rid) | Out-Null

$slangAsset = $lock.slang.assets | Where-Object rid -EQ $Rid
if (-not $slangAsset)
{
    throw "No Slang asset is locked for $Rid."
}

$slangArchive = Get-VerifiedArchive `
    -Url $slangAsset.url `
    -ArchiveName $slangAsset.archiveName `
    -Sha256 $slangAsset.sha256
$spirvToolsArchive = Get-VerifiedArchive `
    -Url $lock.spirvTools.url `
    -ArchiveName $lock.spirvTools.archiveName `
    -Sha256 $lock.spirvTools.sha256
$spirvHeadersArchive = Get-VerifiedArchive `
    -Url $lock.spirvHeaders.url `
    -ArchiveName $lock.spirvHeaders.archiveName `
    -Sha256 $lock.spirvHeaders.sha256

& (Join-Path $PSScriptRoot 'expand-verified-archive.ps1') `
    -Archive $slangArchive `
    -Destination (Join-Path $ToolRoot "$Rid/slang") `
    -Sha256 $slangAsset.sha256 `
    -IncludePaths @('bin', 'lib', 'LICENSE') `
    -Force:$Force | Out-Null
& (Join-Path $PSScriptRoot 'expand-verified-archive.ps1') `
    -Archive $spirvToolsArchive `
    -Destination (Join-Path $CacheRoot 'sources/spirv-tools') `
    -Sha256 $lock.spirvTools.sha256 `
    -Force:$Force | Out-Null
& (Join-Path $PSScriptRoot 'expand-verified-archive.ps1') `
    -Archive $spirvHeadersArchive `
    -Destination (Join-Path $CacheRoot 'sources/spirv-headers') `
    -Sha256 $lock.spirvHeaders.sha256 `
    -Force:$Force | Out-Null

Write-Output (Join-Path $ToolRoot $Rid)
