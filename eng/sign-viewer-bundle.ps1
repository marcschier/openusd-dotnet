#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid,
    [Parameter(Mandatory = $true)]
    [string]$BundleRoot,
    [Parameter(Mandatory = $true)]
    [string]$EvidencePath,
    [string]$ArchivePath,
    [switch]$NotarizeOnly
)

$ErrorActionPreference = 'Stop'

function Write-SigningEvidence
{
    param(
        [string]$Status,
        [string]$Reason,
        [object[]]$SignedFiles = @()
    )

    $evidence = [ordered]@{
        schemaVersion = 1
        rid = $Rid
        status = $Status
        reason = $Reason
        completedAt = [DateTimeOffset]::UtcNow.ToString('O')
        signedFiles = $SignedFiles
    }
    New-Item -ItemType Directory -Force `
        -Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($EvidencePath))) |
        Out-Null
    $evidence | ConvertTo-Json -Depth 8 | Set-Content $EvidencePath
    Write-Host "VIEWER_SIGNING_$($Status.ToUpperInvariant()): $Reason"
}

$root = [IO.Path]::GetFullPath($BundleRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container))
{
    throw "Viewer bundle root not found: $root"
}

if ($Rid -eq 'linux-x64')
{
    Write-SigningEvidence `
        -Status 'skipped' `
        -Reason 'Linux Viewer bundles are intentionally not code-signed.'
    exit 0
}

if ($Rid -eq 'win-x64')
{
    $missing = @(
        'OPENUSD_WINDOWS_CODESIGN_PFX_BASE64',
        'OPENUSD_WINDOWS_CODESIGN_PFX_PASSWORD') | Where-Object {
            [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_))
        }
    if ($missing.Count -gt 0)
    {
        Write-SigningEvidence `
            -Status 'skipped' `
            -Reason "Missing Windows signing credential(s): $($missing -join ', ')."
        exit 0
    }

    $pfxPath = Join-Path $root 'openusd-viewer-codesign.pfx'
    [IO.File]::WriteAllBytes(
        $pfxPath,
        [Convert]::FromBase64String($env:OPENUSD_WINDOWS_CODESIGN_PFX_BASE64))
    try
    {
        $password = ConvertTo-SecureString `
            $env:OPENUSD_WINDOWS_CODESIGN_PFX_PASSWORD `
            -AsPlainText `
            -Force
        $certificate = Import-PfxCertificate `
            -FilePath $pfxPath `
            -CertStoreLocation Cert:\CurrentUser\My `
            -Password $password
        $timestampServer = if (
            [string]::IsNullOrWhiteSpace($env:OPENUSD_WINDOWS_TIMESTAMP_URL))
        {
            'http://timestamp.digicert.com'
        }
        else
        {
            $env:OPENUSD_WINDOWS_TIMESTAMP_URL
        }
        $candidates = @(Get-ChildItem $root -Recurse -File |
            Where-Object { $_.Extension -in @('.exe', '.dll') } |
            Sort-Object FullName)
        if ($candidates.Count -eq 0)
        {
            throw 'No Windows executable or DLL files were found to sign.'
        }

        $signed = foreach ($file in $candidates)
        {
            $result = Set-AuthenticodeSignature `
                -FilePath $file.FullName `
                -Certificate $certificate `
                -TimestampServer $timestampServer
            if ($result.Status -ne 'Valid')
            {
                throw "Signing failed for $($file.FullName): $($result.StatusMessage)"
            }
            [ordered]@{
                path = [IO.Path]::GetRelativePath($root, $file.FullName)
                sha256 = (Get-FileHash $file.FullName -Algorithm SHA256).Hash
            }
        }

        Write-SigningEvidence `
            -Status 'signed' `
            -Reason "Signed $($signed.Count) Windows file(s)." `
            -SignedFiles $signed
    }
    finally
    {
        Remove-Item $pfxPath -Force -ErrorAction SilentlyContinue
    }
    exit 0
}

$missingMac = @(
    'OPENUSD_APPLE_DEVELOPER_ID',
    'OPENUSD_APPLE_ID',
    'OPENUSD_APPLE_TEAM_ID',
    'OPENUSD_APPLE_APP_SPECIFIC_PASSWORD') | Where-Object {
        [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_))
    }
if ($missingMac.Count -gt 0)
{
    Write-SigningEvidence `
        -Status 'skipped' `
        -Reason "Missing Apple signing/notarization credential(s): $($missingMac -join ', ')."
    exit 0
}

if (-not $IsMacOS)
{
    throw 'Apple signing credentials are present, but osx-arm64 signing must run on macOS.'
}

if ($NotarizeOnly)
{
    if ([string]::IsNullOrWhiteSpace($ArchivePath))
    {
        throw '-NotarizeOnly requires -ArchivePath.'
    }
    $archive = [IO.Path]::GetFullPath($ArchivePath)
    if (-not (Test-Path $archive))
    {
        throw "Notarization archive not found: $archive"
    }
    & xcrun notarytool submit $archive `
        --apple-id $env:OPENUSD_APPLE_ID `
        --team-id $env:OPENUSD_APPLE_TEAM_ID `
        --password $env:OPENUSD_APPLE_APP_SPECIFIC_PASSWORD `
        --wait
    if ($LASTEXITCODE -ne 0)
    {
        throw "Apple notarization failed for $archive."
    }
    Write-SigningEvidence `
        -Status 'notarized' `
        -Reason "Apple notarization succeeded for $([IO.Path]::GetFileName($archive))."
    exit 0
}

$macFiles = @(Get-ChildItem $root -Recurse -File |
    Where-Object {
        $_.Extension -eq '.dylib' -or $_.Name -eq 'OpenUsd.Viewer.App'
    } |
    Sort-Object FullName)
if ($macFiles.Count -eq 0)
{
    throw 'No macOS executable or dylib files were found to sign.'
}

$signedMac = foreach ($file in $macFiles)
{
    & codesign `
        --force `
        --options runtime `
        --timestamp `
        --sign $env:OPENUSD_APPLE_DEVELOPER_ID `
        $file.FullName
    if ($LASTEXITCODE -ne 0)
    {
        throw "codesign failed for $($file.FullName)."
    }
    & codesign --verify --strict --verbose=2 $file.FullName
    if ($LASTEXITCODE -ne 0)
    {
        throw "codesign verification failed for $($file.FullName)."
    }
    [ordered]@{
        path = [IO.Path]::GetRelativePath($root, $file.FullName)
        sha256 = (Get-FileHash $file.FullName -Algorithm SHA256).Hash
    }
}

Write-SigningEvidence `
    -Status 'signed' `
    -Reason "Signed $($signedMac.Count) macOS file(s); notarization credentials are wired." `
    -SignedFiles $signedMac
