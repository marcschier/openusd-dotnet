#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid,
    [string]$CacheRoot = (Join-Path $PSScriptRoot '../native/downloads'),
    [string]$SourceRoot = (Join-Path $PSScriptRoot '../native/src'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$lockPath = Join-Path $PSScriptRoot 'cesium.lock.json'
$lock = Get-Content $lockPath -Raw | ConvertFrom-Json
$CacheRoot = [System.IO.Path]::GetFullPath($CacheRoot)
$SourceRoot = [System.IO.Path]::GetFullPath($SourceRoot)

New-Item -ItemType Directory -Force -Path $CacheRoot | Out-Null
New-Item -ItemType Directory -Force -Path $SourceRoot | Out-Null

function Get-VerifiedDownload
{
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [Parameter(Mandatory = $true)][string]$FileName,
        [Parameter(Mandatory = $true)][string]$Sha256
    )

    $path = Join-Path $CacheRoot $FileName
    if (Test-Path $path)
    {
        $actual = (Get-FileHash $path -Algorithm SHA256).Hash
        if ($actual -eq $Sha256)
        {
            Write-Host "Verified $FileName"
            return $path
        }

        if (-not $Force)
        {
            throw "Hash mismatch for $path. Expected $Sha256, got $actual. Use -Force to replace it."
        }

        Remove-Item $path -Force
    }

    $partial = "$path.partial"
    Remove-Item $partial -Force -ErrorAction SilentlyContinue
    Write-Host "Downloading $Uri"
    Invoke-WebRequest -Uri $Uri -OutFile $partial
    $downloadHash = (Get-FileHash $partial -Algorithm SHA256).Hash
    if ($downloadHash -ne $Sha256)
    {
        Remove-Item $partial -Force
        throw "Hash mismatch for $Uri. Expected $Sha256, got $downloadHash."
    }

    Move-Item $partial $path
    return $path
}

function Get-PinnedGitRepository
{
    param(
        [Parameter(Mandatory = $true)][string]$Repository,
        [Parameter(Mandatory = $true)][string]$Commit,
        [Parameter(Mandatory = $true)][string]$Path
    )

    if (Test-Path $Path)
    {
        $actual = & git -C $Path rev-parse HEAD
        if ($LASTEXITCODE -ne 0)
        {
            throw "Failed to inspect pinned repository at $Path."
        }

        if ($actual -eq $Commit)
        {
            return
        }

        if (-not $Force)
        {
            throw "Pinned repository at $Path is $actual, expected $Commit. Use -Force to replace it."
        }

        Remove-Item $Path -Recurse -Force
    }

    Write-Host "Cloning $Repository at $Commit"
    & git clone --no-checkout $Repository $Path
    if ($LASTEXITCODE -ne 0)
    {
        throw "Failed to clone $Repository."
    }

    & git -C $Path checkout --detach $Commit
    if ($LASTEXITCODE -ne 0)
    {
        throw "Failed to check out $Commit in $Path."
    }
}

$cesiumArchive = Get-VerifiedDownload `
    -Uri $lock.cesiumNative.archiveUrl `
    -FileName $lock.cesiumNative.archiveName `
    -Sha256 $lock.cesiumNative.archiveSha256

$cesiumSource = Join-Path $SourceRoot $lock.cesiumNative.extractDirectory
if (-not (Test-Path $cesiumSource))
{
    Write-Host "Extracting $($lock.cesiumNative.archiveName)"
    & tar -xzf $cesiumArchive -C $SourceRoot
    if ($LASTEXITCODE -ne 0)
    {
        throw "Failed to extract $cesiumArchive."
    }
}

$manifestHash = (Get-FileHash (Join-Path $cesiumSource 'vcpkg.json') -Algorithm SHA256).Hash
if ($manifestHash -ne $lock.cesiumNative.vcpkgManifestSha256)
{
    throw "cesium-native vcpkg.json hash mismatch. Expected $($lock.cesiumNative.vcpkgManifestSha256), got $manifestHash."
}

$configurationHash = (Get-FileHash (Join-Path $cesiumSource 'vcpkg-configuration.json') -Algorithm SHA256).Hash
if ($configurationHash -ne $lock.cesiumNative.vcpkgConfigurationSha256)
{
    throw "cesium-native vcpkg-configuration.json hash mismatch. Expected $($lock.cesiumNative.vcpkgConfigurationSha256), got $configurationHash."
}

$vcpkgCacheRoot = Join-Path $repoRoot 'native/.cache'
New-Item -ItemType Directory -Force -Path $vcpkgCacheRoot | Out-Null
$vcpkgRoot = Join-Path $vcpkgCacheRoot "vcpkg-$($lock.vcpkg.baseline.Substring(0, 12))"
Get-PinnedGitRepository `
    -Repository $lock.vcpkg.repository `
    -Commit $lock.vcpkg.baseline `
    -Path $vcpkgRoot

$vcpkgExe = if ($IsWindows) { Join-Path $vcpkgRoot 'vcpkg.exe' } else { Join-Path $vcpkgRoot 'vcpkg' }
if (-not (Test-Path $vcpkgExe))
{
    Push-Location $vcpkgRoot
    try
    {
        if ($IsWindows)
        {
            & .\bootstrap-vcpkg.bat -disableMetrics
        }
        else
        {
            & ./bootstrap-vcpkg.sh -disableMetrics
        }

        if ($LASTEXITCODE -ne 0)
        {
            throw 'Failed to bootstrap pinned vcpkg.'
        }
    }
    finally
    {
        Pop-Location
    }
}

[pscustomobject]@{
    Rid = $Rid
    RepositoryRoot = $repoRoot.Path
    CacheRoot = $CacheRoot
    SourceRoot = $cesiumSource
    VcpkgRoot = $vcpkgRoot
    VcpkgExecutable = $vcpkgExe
}
