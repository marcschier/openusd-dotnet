#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [string]$OutputRoot = (
        Join-Path $PSScriptRoot 'out/checked-payload/osx-arm64')
)

$ErrorActionPreference = 'Stop'
if (-not $IsMacOS)
{
    throw 'The validated Metal library can only be prepared on macOS.'
}
$architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
if ($architecture -ne 'Arm64')
{
    throw "Metal library preparation requires arm64, got $architecture."
}

& (Join-Path $PSScriptRoot 'select-xcode.ps1')
& (Join-Path $PSScriptRoot 'validate-checked-payload.ps1') `
    -Rid osx-arm64 `
    -OutputRoot $OutputRoot

$manifestPath = Join-Path $OutputRoot 'metallib-manifest.json'
$stagedPath = Join-Path $PSScriptRoot 'checked/mesh.metallib'
$stagedManifestPath = Join-Path `
    $PSScriptRoot `
    'checked/mesh.metallib.manifest.json'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
& python (Join-Path $PSScriptRoot 'scripts/metal_sidecar.py') `
    --sidecar $manifestPath `
    --library $stagedPath `
    --manifest (Join-Path $PSScriptRoot 'shader-manifest.json') `
    --lock (Join-Path $PSScriptRoot 'toolchain.lock.json') `
    --repository-root $repoRoot `
    --verify-files
if ($LASTEXITCODE -ne 0)
{
    throw 'The staged Metal sidecar failed central validation.'
}
$manifestHash = (
    Get-FileHash $manifestPath -Algorithm SHA256
).Hash.ToLowerInvariant()
$stagedManifestHash = (
    Get-FileHash $stagedManifestPath -Algorithm SHA256
).Hash.ToLowerInvariant()
if ($manifestHash -ne $stagedManifestHash)
{
    throw 'The staged Metal manifest differs from the validated manifest.'
}

Write-Host (
    "Prepared validated ten-entry Metal library $stagedPath.")
