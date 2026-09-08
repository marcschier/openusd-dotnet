#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Write', 'Verify')][string]$Operation,
    [Parameter(Mandatory = $true)][string]$SdkRoot,
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')][string]$Rid,
    [string]$SourceRoot,
    [string]$PatchLockPath = (Join-Path $PSScriptRoot 'openusd-storage-admission.lock.json')
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$SdkRoot = [IO.Path]::GetFullPath($SdkRoot)
$PatchLockPath = [IO.Path]::GetFullPath($PatchLockPath)
$lock = Get-Content $PatchLockPath -Raw | ConvertFrom-Json
$sourceLock = Get-Content (Join-Path $PSScriptRoot 'openusd.lock.json') -Raw | ConvertFrom-Json
$metadataPath = Join-Path $SdkRoot '.openusd-storage-admission.json'
if ([string]::IsNullOrWhiteSpace($Rid))
{
    $Rid = if ($IsWindows) { 'win-x64' } elseif ($IsMacOS) { 'osx-arm64' } else { 'linux-x64' }
}
$binaries = switch ($Rid)
{
    'win-x64' { 'lib/usd_ms.dll'; 'lib/usd_ms.lib' }
    'linux-x64' { 'lib/libusd_ms.so' }
    'osx-arm64' { 'lib/libusd_ms.dylib' }
}
$requiredFiles = @(
    'include/pxr/usd/sdf/storageAdmission.h',
    'include/pxr/base/vt/arrayEdit.h'
) + @($binaries)

function Get-Hash([string]$Path)
{
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing SDK provenance input: $Path" }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

if ($lock.sourceCommit -cne $sourceLock.openUsd.commit -or $lock.accessorVersion -ne 1)
{
    throw 'The SDK storage-admission patch set does not match the pinned source/accessor version.'
}
foreach ($patch in $lock.patches)
{
    if ((Get-Hash (Join-Path $repository $patch.path)) -cne $patch.sha256)
    {
        throw "SDK patch identity mismatch: $($patch.id)"
    }
}

if ($Operation -eq 'Write')
{
    if (-not $SdkRoot.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        $SdkRoot.StartsWith((Join-Path $repository 'native\install'), [StringComparison]::OrdinalIgnoreCase))
    {
        throw 'SDK admission metadata must be written to an isolated repository-owned SDK graph.'
    }
    $ancestor = Get-Item -LiteralPath $SdkRoot
    while ($ancestor)
    {
        if ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint)
        {
            throw 'SDK admission metadata cannot be written through reparse points.'
        }
        $ancestor = $ancestor.Parent
    }
    if ([string]::IsNullOrWhiteSpace($SourceRoot)) { throw 'Writing SDK provenance requires the patched SourceRoot.' }
    $SourceRoot = [IO.Path]::GetFullPath($SourceRoot)
    foreach ($patch in $lock.patches)
    {
        foreach ($file in $patch.files)
        {
            if ((Get-Hash (Join-Path $SourceRoot $file.path)) -cne $file.afterSha256)
            {
                throw "Patched SDK source identity mismatch: $($file.path)"
            }
        }
    }
    $files = [Collections.Generic.List[object]]::new()
    foreach ($patch in $lock.patches)
    {
        foreach ($file in $patch.files | Where-Object {
            $_.path -in @('pxr\base\vt\arrayEdit.h', 'pxr\usd\sdf\storageAdmission.h') })
        {
            $relative = Join-Path 'include' $file.path
            if ((Get-Hash (Join-Path $SdkRoot $relative)) -cne $file.afterSha256)
            {
                throw "Installed SDK header does not match the patched source: $relative"
            }
            $files.Add([ordered]@{ path = $relative.Replace('\', '/'); sha256 = $file.afterSha256 })
        }
    }
    foreach ($relative in $binaries)
    {
        $files.Add([ordered]@{ path = $relative.Replace('\', '/'); sha256 = Get-Hash (Join-Path $SdkRoot $relative) })
    }
    $metadata = [ordered]@{
        schemaVersion = 1
        sourceCommit = $lock.sourceCommit
        sourceArchiveSha256 = $sourceLock.openUsd.archiveSha256
        sourceBuildScriptSha256 = Get-Hash (Join-Path $SourceRoot $sourceLock.openUsd.buildScript)
        sourceLockSha256 = Get-Hash (Join-Path $PSScriptRoot 'openusd.lock.json')
        patchLockSha256 = Get-Hash $PatchLockPath
        accessorVersion = $lock.accessorVersion
        patches = @($lock.patches | ForEach-Object { [ordered]@{ id = $_.id; sha256 = $_.sha256 } })
        files = $files.ToArray()
    }
    $metadata | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $metadataPath -Encoding utf8NoBOM
}

$metadata = Get-Content $metadataPath -Raw | ConvertFrom-Json
if ($metadata.schemaVersion -ne 1 -or $metadata.accessorVersion -ne 1 -or
    $metadata.sourceCommit -cne $lock.sourceCommit -or
    $metadata.patchLockSha256 -cne (Get-Hash $PatchLockPath))
{
    throw 'Installed SDK admission provenance does not match the tracked patch set.'
}
if ($metadata.files -isnot [Array] -or $metadata.files.Count -eq 0)
{
    throw 'SDK provenance requires a nonempty file inventory with pinned headers and platform binaries.'
}
$inventory = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($file in $metadata.files)
{
    if ($file.path -isnot [string] -or [string]::IsNullOrWhiteSpace($file.path) -or
        $file.sha256 -isnot [string] -or $file.sha256 -cnotmatch '^[0-9A-F]{64}$')
    {
        throw 'SDK provenance requires a relative file path and SHA256 for every inventory entry.'
    }
    $relative = $file.path.Replace('\', '/')
    if ([IO.Path]::IsPathRooted($relative) -or $relative -match '^[A-Za-z]:' -or
        @($relative.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -ne 0)
    {
        throw 'Invalid SDK provenance file path.'
    }
    if (-not $inventory.Add($relative))
    {
        throw "Duplicate SDK provenance file entry: $relative"
    }
}
foreach ($required in $requiredFiles)
{
    if (-not $inventory.Contains($required))
    {
        throw "SDK provenance is missing required $Rid file entry: $required"
    }
}
foreach ($file in $metadata.files)
{
    if ((Get-Hash (Join-Path $SdkRoot $file.path)) -cne $file.sha256)
    {
        throw "Installed SDK admission file drift: $($file.path)"
    }
}
foreach ($patch in $lock.patches)
{
    foreach ($file in $patch.files | Where-Object {
        $_.path -in @('pxr\base\vt\arrayEdit.h', 'pxr\usd\sdf\storageAdmission.h') })
    {
        if ((Get-Hash (Join-Path $SdkRoot (Join-Path 'include' $file.path))) -cne $file.afterSha256)
        {
            throw "Installed SDK header differs from its exact tracked patch pin: $($file.path)"
        }
    }
}
Write-Output "SDK_STORAGE_PROVENANCE_OK: source=$($metadata.sourceCommit), accessor=1, rid=$Rid, files=$($metadata.files.Count)"
