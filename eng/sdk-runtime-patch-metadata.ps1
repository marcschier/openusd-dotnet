#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Write', 'Verify')][string]$Operation,
    [Parameter(Mandatory = $true)][string]$SdkRoot,
    [Parameter(Mandatory = $true)][ValidateSet('win-x64', 'linux-x64', 'osx-arm64')][string]$Rid,
    [string]$SourceRoot
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sdk = [IO.Path]::GetFullPath($SdkRoot)
$lockPath = Join-Path $PSScriptRoot 'openusd-runtime-patches.lock.json'
$pin = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
$installLock = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'openusd.install.lock.json') -Raw | ConvertFrom-Json
$pinHash = (Get-FileHash -LiteralPath $lockPath).Hash
$storageLockPath = Join-Path $PSScriptRoot 'openusd-storage-admission.lock.json'
$storagePin = Get-Content -LiteralPath $storageLockPath -Raw | ConvertFrom-Json
$storageHash = (Get-FileHash -LiteralPath $storageLockPath).Hash
if ($pin.sourceCommit -cne $installLock.openUsd.commit -or
    $pinHash -cne $installLock.openUsd.runtimePatchLockSha256 -or
    $storagePin.sourceCommit -cne $pin.sourceCommit -or
    $storageHash -cne $installLock.openUsd.storageAdmissionPatchLockSha256)
{
    throw 'The SDK runtime patches do not match the pinned install source.'
}
foreach ($patch in @($pin.patches) + @($storagePin.patches))
{
    if ((Get-FileHash -LiteralPath (Join-Path $repo $patch.path)).Hash -cne $patch.sha256)
    {
        throw "SDK runtime patch identity mismatch: $($patch.id)"
    }
    if (Test-Path -LiteralPath (Join-Path $sdk 'include') -PathType Container)
    {
        foreach ($file in $patch.files | Where-Object {
            $_.path.EndsWith('.h', [StringComparison]::Ordinal) -and $_.installedHeader -ne $false })
        {
            $installedHeader = Join-Path (Join-Path $sdk 'include') $file.path
            if (-not (Test-Path -LiteralPath $installedHeader -PathType Leaf) -or
                (Get-FileHash -LiteralPath $installedHeader).Hash -cne $file.afterSha256)
            {
                throw "Installed SDK runtime patch header mismatch: $($file.path)"
            }
        }
    }
}
$binary = switch ($Rid)
{
    'win-x64' { 'lib\usd_ms.dll' }
    'linux-x64' { 'lib\libusd_ms.so' }
    'osx-arm64' { 'lib\libusd_ms.dylib' }
}
$metadataPath = Join-Path $sdk '.openusd-runtime-patches.json'
$expected = [ordered]@{
    schemaVersion = 1
    rid = $Rid
    sourceCommit = $pin.sourceCommit
    patchLockSha256 = $pinHash
    storageAdmissionPatchLockSha256 = $storageHash
    libraryPath = $binary
    librarySha256 = (Get-FileHash -LiteralPath (Join-Path $sdk $binary)).Hash
}
if ($Operation -eq 'Write')
{
    if ([string]::IsNullOrWhiteSpace($SourceRoot))
    {
        throw 'Writing SDK runtime provenance requires the patched source root.'
    }
    foreach ($root in @($sdk, [IO.Path]::GetFullPath($SourceRoot)))
    {
        if (-not $root.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))
        {
            throw 'SDK runtime provenance must be written inside the repository-owned build graph.'
        }
        $ancestor = Get-Item -LiteralPath $root -Force
        while ($ancestor.FullName.Length -gt $repo.Length)
        {
            if ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint)
            {
                throw 'SDK runtime provenance cannot be written through shared reparse points.'
            }
            $ancestor = $ancestor.Parent
        }
    }
    foreach ($patch in @($pin.patches) + @($storagePin.patches))
    {
        foreach ($file in $patch.files)
        {
            if ((Get-FileHash -LiteralPath (Join-Path $SourceRoot $file.path)).Hash -cne $file.afterSha256)
            {
                throw "SDK runtime patch source mismatch: $($file.path)"
            }
        }
    }
    $expected | ConvertTo-Json | Set-Content -LiteralPath $metadataPath -Encoding utf8NoBOM
}
if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf))
{
    throw 'The SDK lacks required runtime-patch provenance. Rebuild the pinned SDK; do not reuse an older install.'
}
$metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
foreach ($key in $expected.Keys)
{
    if ($metadata.$key -cne $expected[$key])
    {
        throw "SDK runtime-patch provenance mismatch: $key"
    }
}
Write-Output "Verified $Rid SDK runtime patches: $pinHash"
