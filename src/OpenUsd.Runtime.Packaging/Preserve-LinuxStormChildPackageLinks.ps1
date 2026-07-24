#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$StormChildLibrary,
    [switch]$AllowLinkPlaceholders
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LinuxStormChildTopology.ps1')

$topology = Get-OpenUsdStormChildFileTopology `
    -LibraryPath $StormChildLibrary `
    -AllowLinkPlaceholders:$AllowLinkPlaceholders
$entryRoot = 'runtimes/linux-x64/native/'
$expectedNames = @($topology.entries | ForEach-Object { [string]$_.name })

Add-Type -AssemblyName System.IO.Compression
$archive = [System.IO.Compression.ZipFile]::Open(
    [System.IO.Path]::GetFullPath($PackagePath),
    [System.IO.Compression.ZipArchiveMode]::Update)
try
{
    $actualEntries = @($archive.Entries |
        Where-Object { $_.FullName -like "${entryRoot}libopenusd_storm_child.so*" })
    if ($actualEntries.Count -ne $expectedNames.Count)
    {
        throw 'The packed Storm child entry count does not match the validated install topology.'
    }
    foreach ($entry in $actualEntries)
    {
        if ($expectedNames -cnotcontains [System.IO.Path]::GetFileName($entry.FullName))
        {
            throw "Unexpected packed Storm child entry '$($entry.FullName)'."
        }
    }

    foreach ($link in @($topology.entries | Where-Object type -eq 'symlink'))
    {
        $entryPath = $entryRoot + [string]$link.name
        $existing = $archive.GetEntry($entryPath)
        if ($null -eq $existing)
        {
            throw "The packed Storm child link entry is missing: $entryPath"
        }
        $existing.Delete()
        $replacement = $archive.CreateEntry(
            $entryPath,
            [System.IO.Compression.CompressionLevel]::NoCompression)
        $replacement.ExternalAttributes = [BitConverter]::ToInt32(
            [BitConverter]::GetBytes([Convert]::ToUInt32('A1FF0000', 16)),
            0)
        $bytes = [System.Text.Encoding]::UTF8.GetBytes([string]$link.target)
        $stream = $replacement.Open()
        try
        {
            $stream.Write($bytes)
        }
        finally
        {
            $stream.Dispose()
        }
    }
}
finally
{
    $archive.Dispose()
}

Write-Output (
    'Preserved Linux Storm child package link chain: ' +
    (($topology.entries | ForEach-Object {
        if ($_.type -eq 'symlink') { "$($_.name)->$($_.target)" } else { $_.name }
    }) -join ', '))
