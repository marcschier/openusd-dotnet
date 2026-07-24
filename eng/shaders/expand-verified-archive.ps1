#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Archive,

    [Parameter(Mandatory = $true)]
    [string]$Destination,

    [Parameter(Mandatory = $true)]
    [string]$Sha256,

    [string[]]$IncludePaths = @(),

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$archivePath = [System.IO.Path]::GetFullPath($Archive)
$destinationPath = [System.IO.Path]::GetFullPath($Destination)
$marker = Join-Path $destinationPath '.archive-sha256'
$actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
if ($actualHash -ne $Sha256)
{
    throw "Hash mismatch for $archivePath. Expected $Sha256, got $actualHash."
}
if ((Test-Path -LiteralPath $marker) -and -not $Force)
{
    $expandedHash = (Get-Content -LiteralPath $marker -Raw).Trim()
    if ($expandedHash -eq $Sha256)
    {
        Write-Output $destinationPath
        return
    }
}

function Assert-SafeArchiveMember
{
    param([Parameter(Mandatory = $true)][string]$Member)

    $normalized = $Member.Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($normalized) -or
        [System.IO.Path]::IsPathRooted($normalized) -or
        $normalized.StartsWith('/', [StringComparison]::Ordinal) -or
        $normalized.Split('/') -contains '..')
    {
        throw "The archive contains an unsafe member: $Member"
    }
}

Remove-Item -LiteralPath $destinationPath -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $destinationPath | Out-Null
if ($archivePath.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase))
{
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
    try
    {
        foreach ($entry in $zip.Entries)
        {
            Assert-SafeArchiveMember $entry.FullName
        }
    }
    finally
    {
        $zip.Dispose()
    }

    $extractRoot = "$destinationPath.extract"
    Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
    try
    {
        Expand-Archive -LiteralPath $archivePath -DestinationPath $extractRoot
        if ($IncludePaths.Count -eq 0)
        {
            Get-ChildItem -LiteralPath $extractRoot -Force |
                Copy-Item -Destination $destinationPath -Recurse -Force
        }
        else
        {
            foreach ($includePath in $IncludePaths)
            {
                $source = Join-Path $extractRoot $includePath
                if (-not (Test-Path -LiteralPath $source))
                {
                    throw "The archive does not contain required path '$includePath'."
                }
                Copy-Item `
                    -LiteralPath $source `
                    -Destination (Join-Path $destinationPath $includePath) `
                    -Recurse `
                    -Force
            }
        }
    }
    finally
    {
        Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
else
{
    $members = @(& tar -tf $archivePath)
    if ($LASTEXITCODE -ne 0)
    {
        throw "Failed to inspect $archivePath."
    }
    foreach ($member in $members)
    {
        Assert-SafeArchiveMember $member
    }
    $tarArguments = @('-xf', $archivePath, '-C', $destinationPath) + $IncludePaths
    & tar @tarArguments
    if ($LASTEXITCODE -ne 0)
    {
        throw "Failed to extract $archivePath."
    }
}

[System.IO.File]::WriteAllText(
    $marker,
    "$Sha256`n",
    [System.Text.UTF8Encoding]::new($false))
Write-Output $destinationPath
