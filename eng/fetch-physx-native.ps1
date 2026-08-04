#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid,
    [string]$CacheRoot = (Join-Path $PSScriptRoot '../native/downloads'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$lock = Get-Content (Join-Path $PSScriptRoot 'physx.lock.json') -Raw | ConvertFrom-Json
$CacheRoot = [System.IO.Path]::GetFullPath($CacheRoot)
New-Item -ItemType Directory -Force -Path $CacheRoot | Out-Null

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
    VcpkgRoot = $vcpkgRoot
    VcpkgExecutable = $vcpkgExe
}