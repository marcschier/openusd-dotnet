#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

[CmdletBinding()]
param(
    [string]$OutputRoot = 'artifacts/viewer-distribution-tests'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$root = if ([IO.Path]::IsPathRooted($OutputRoot))
{
    $OutputRoot
}
else
{
    Join-Path $repoRoot $OutputRoot
}
Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $root | Out-Null

$oldPfx = $env:OPENUSD_WINDOWS_CODESIGN_PFX_BASE64
$oldPassword = $env:OPENUSD_WINDOWS_CODESIGN_PFX_PASSWORD
try
{
    $env:OPENUSD_WINDOWS_CODESIGN_PFX_BASE64 = $null
    $env:OPENUSD_WINDOWS_CODESIGN_PFX_PASSWORD = $null
    $bundleRoot = Join-Path $root 'unsigned'
    New-Item -ItemType Directory -Force -Path $bundleRoot | Out-Null
    Set-Content (Join-Path $bundleRoot 'OpenUsd.Viewer.App.exe') 'not a real exe'
    $evidencePath = Join-Path $root 'signing.json'
    & (Join-Path $PSScriptRoot 'sign-viewer-bundle.ps1') `
        -Rid win-x64 `
        -BundleRoot $bundleRoot `
        -EvidencePath $evidencePath
    if ($LASTEXITCODE -ne 0)
    {
        throw 'The unsigned signing path failed.'
    }
    if (-not (Test-Path $evidencePath))
    {
        throw 'The unsigned signing path did not emit evidence.'
    }
    $evidence = Get-Content $evidencePath -Raw | ConvertFrom-Json
    if ([string]$evidence.status -cne 'skipped' -or
        [string]$evidence.reason -notmatch 'OPENUSD_WINDOWS_CODESIGN_PFX_BASE64' -or
        [string]$evidence.reason -notmatch 'OPENUSD_WINDOWS_CODESIGN_PFX_PASSWORD')
    {
        throw 'The unsigned signing evidence did not state why signing was skipped.'
    }
}
finally
{
    $env:OPENUSD_WINDOWS_CODESIGN_PFX_BASE64 = $oldPfx
    $env:OPENUSD_WINDOWS_CODESIGN_PFX_PASSWORD = $oldPassword
}

Write-Output "VIEWER_DISTRIBUTION_TESTS passed evidence=$evidencePath"
