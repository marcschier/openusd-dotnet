#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$root = Join-Path $repo ("artifacts\runtime-patch-contract\" + [Guid]::NewGuid().ToString('N'))
$sdk = Join-Path $root 'sdk'
$pin = Get-Content (Join-Path $PSScriptRoot 'openusd-runtime-patches.lock.json') -Raw | ConvertFrom-Json
$verify = Join-Path $PSScriptRoot 'sdk-runtime-patch-metadata.ps1'
New-Item -ItemType Directory -Path (Join-Path $sdk 'lib') -Force | Out-Null

function Assert-Refused([scriptblock]$Action, [string]$Message)
{
    try { & $Action | Out-Null }
    catch
    {
        if ($_.Exception.Message.Contains($Message, [StringComparison]::Ordinal)) { return }
        throw
    }
    throw "Expected refusal containing '$Message'."
}

try
{
    foreach ($rid in @('win-x64', 'linux-x64', 'osx-arm64'))
    {
        $relative = switch ($rid)
        {
            'win-x64' { 'lib\usd_ms.dll' }
            'linux-x64' { 'lib\libusd_ms.so' }
            'osx-arm64' { 'lib\libusd_ms.dylib' }
        }
        $binary = Join-Path $sdk $relative
        [IO.File]::WriteAllText($binary, 'synthetic pinned runtime')
        $metadataPath = Join-Path $sdk '.openusd-runtime-patches.json'
        $metadata = [ordered]@{
            schemaVersion = 1
            rid = $rid
            sourceCommit = $pin.sourceCommit
            patchLockSha256 = (Get-FileHash (Join-Path $PSScriptRoot 'openusd-runtime-patches.lock.json')).Hash
            storageAdmissionPatchLockSha256 =
                (Get-FileHash (Join-Path $PSScriptRoot 'openusd-storage-admission.lock.json')).Hash
            libraryPath = $relative
            librarySha256 = (Get-FileHash $binary).Hash
        }
        Assert-Refused { & $verify -Operation Verify -SdkRoot $sdk -Rid $rid } 'lacks required'
        $metadata | ConvertTo-Json | Set-Content -LiteralPath $metadataPath
        & $verify -Operation Verify -SdkRoot $sdk -Rid $rid | Out-Null
        [IO.File]::AppendAllText($binary, 'changed')
        Assert-Refused { & $verify -Operation Verify -SdkRoot $sdk -Rid $rid } 'librarySha256'
        [IO.File]::WriteAllText($binary, 'synthetic pinned runtime')
        $metadata.patchLockSha256 = 'wrong'
        $metadata | ConvertTo-Json | Set-Content -LiteralPath $metadataPath
        Assert-Refused { & $verify -Operation Verify -SdkRoot $sdk -Rid $rid } 'patchLockSha256'
        Assert-Refused { & $verify -Operation Write -SdkRoot $sdk -Rid $rid } 'source root'
        if ($IsWindows)
        {
            [IO.File]::SetAttributes($metadataPath, [IO.File]::GetAttributes($metadataPath) -bor [IO.FileAttributes]::Hidden)
        }
        Remove-Item -LiteralPath $metadataPath -Force
    }
    Write-Output 'SDK_RUNTIME_PATCH_METADATA_OK: all RIDs, missing provenance, binary drift, patch drift and missing source'
}
finally
{
    Remove-Item -LiteralPath $root -Recurse -Force
}
