#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
<#
.SYNOPSIS
    Downloads and verifies the pinned NVIDIA MDL SDK release archive for one RID.

.DESCRIPTION
    This is an opt-in developer/CI step, never part of a package build. Nothing this
    repository publishes contains an MDL SDK binary; see eng/mdl.lock.json.

    The archive is verified against the SHA-256 recorded in eng/mdl.lock.json before it is
    extracted. The digest is not computed from the download and then written back -- that
    would attest to whatever arrived. It is compared against the value the lock already
    carries, which was read from the GitHub release asset digest, and a mismatch deletes the
    download and fails.

    Only three things are extracted: the neuraylib headers the adapter compiles against, the
    runtime library the adapter loads at run time, and the licence and third-party notices.
    The rest of the archive -- examples, documentation, the freeimage and OpenImageIO plugins,
    the example material libraries -- is deliberately left in the archive so nothing about
    what this repository builds depends on it.
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid = $(
        if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'win-x64' }
        elseif ($IsMacOS) { 'osx-arm64' }
        else { 'linux-x64' }),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$lock = Get-Content (Join-Path $PSScriptRoot 'mdl.lock.json') -Raw | ConvertFrom-Json
$release = $lock.mdlSdk.release
$asset = $lock.mdlSdk.prebuiltAssets | Where-Object { $_.rid -eq $Rid }
if ($null -eq $asset)
{
    throw "eng/mdl.lock.json records no MDL SDK asset for $Rid."
}

$downloadRoot = Join-Path $repoRoot 'native/downloads'
$installRoot = Join-Path $repoRoot "native/install/mdl-sdk/$Rid"
$archivePath = Join-Path $downloadRoot $asset.name
New-Item -ItemType Directory -Force -Path $downloadRoot | Out-Null

function Assert-ArchiveDigest
{
    param([Parameter(Mandatory = $true)][string]$Path)

    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $asset.sha256.ToLowerInvariant())
    {
        Remove-Item -LiteralPath $Path -Force
        throw (
            "MDL SDK archive digest mismatch for $($asset.name). Expected " +
            "$($asset.sha256), got $actual. The download was deleted.")
    }
}

if ((Test-Path $installRoot) -and -not $Force)
{
    Write-Host "MDL SDK $release for $Rid is already extracted at $installRoot."
    return
}

if (-not (Test-Path $archivePath))
{
    $uri = "https://github.com/NVIDIA/MDL-SDK/releases/download/$release/$($asset.name)"
    Write-Host "Downloading $($asset.name) ($([math]::Round($asset.size / 1MB)) MiB)..."
    Invoke-WebRequest -Uri $uri -OutFile $archivePath -MaximumRedirection 5
}
Assert-ArchiveDigest -Path $archivePath
Write-Host "Verified $($asset.name) against the eng/mdl.lock.json SHA-256."

$stageRoot = Join-Path $repoRoot "artifacts/mdl-sdk/$Rid"
if (Test-Path $stageRoot)
{
    Remove-Item $stageRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null

Write-Host 'Extracting...'
if ($asset.name.EndsWith('.zip', [StringComparison]::Ordinal))
{
    Expand-Archive -LiteralPath $archivePath -DestinationPath $stageRoot -Force
}
else
{
    & tar -xzf $archivePath -C $stageRoot
    if ($LASTEXITCODE -ne 0)
    {
        throw "Could not extract $($asset.name)."
    }
}

# The archives put everything under a single top-level directory whose name carries the
# build number. Resolving it rather than hard-coding it keeps this script working across
# point releases without a second thing to update.
$extracted = Get-ChildItem -LiteralPath $stageRoot -Directory
$sdkRoot = if ($extracted.Count -eq 1) { $extracted[0].FullName } else { $stageRoot }

$includeSource = Join-Path $sdkRoot 'include'
if (-not (Test-Path $includeSource))
{
    throw "The extracted MDL SDK has no include directory at '$includeSource'."
}

if (Test-Path $installRoot)
{
    Remove-Item $installRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
Copy-Item $includeSource (Join-Path $installRoot 'include') -Recurse -Force

$libraryName = switch ($Rid)
{
    'win-x64' { 'libmdl_sdk.dll' }
    'linux-x64' { 'libmdl_sdk.so' }
    'osx-arm64' { 'libmdl_sdk.dylib' }
}
$library = Get-ChildItem -LiteralPath $sdkRoot -Recurse -File -Filter $libraryName |
    Select-Object -First 1
if ($null -eq $library)
{
    throw "The extracted MDL SDK has no $libraryName."
}
$runtimeRoot = Join-Path $installRoot 'runtime'
New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
Copy-Item $library.FullName (Join-Path $runtimeRoot $libraryName) -Force

# Licence and third-party notices travel with the runtime, always. A runtime staged without
# them is a runtime nobody can lawfully pass on, and the point of pinning a BSD-3-Clause
# baseline is that its terms stay attached to it.
$notices = @()
foreach ($pattern in @('LICENSE*', 'THIRD*PARTY*', 'NOTICE*', 'COPYRIGHT*'))
{
    $notices += Get-ChildItem -LiteralPath $sdkRoot -File -Filter $pattern -ErrorAction SilentlyContinue
}
if ($notices.Count -eq 0)
{
    throw 'The extracted MDL SDK carries no licence or third-party notice file.'
}
foreach ($notice in $notices)
{
    Copy-Item $notice.FullName (Join-Path $installRoot $notice.Name) -Force
}

Remove-Item $stageRoot -Recurse -Force

$metadata = [ordered]@{
    rid = $Rid
    release = $release
    tagCommit = $lock.mdlSdk.commit
    asset = $asset.name
    sha256 = $asset.sha256
    license = $lock.mdlSdk.license.spdx
    notices = @($notices | ForEach-Object { $_.Name })
    extractedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    redistributable = $false
    redistributionNote = (
        'This tree is a developer/CI acquisition only. No OpenUsd package contains any file ' +
        'from it; the SDK-backed adapter loads libmdl_sdk from a user-supplied location at ' +
        'run time.')
}
$metadata | ConvertTo-Json -Depth 4 |
    Set-Content (Join-Path $installRoot '.mdl-sdk-acquisition.json') -Encoding utf8

Write-Host "MDL SDK $release ($($lock.mdlSdk.license.spdx)) staged at $installRoot"
Write-Host "  headers: $(Join-Path $installRoot 'include')"
Write-Host "  runtime: $(Join-Path $runtimeRoot $libraryName)"
Write-Host "  notices: $(($notices | ForEach-Object { $_.Name }) -join ', ')"
