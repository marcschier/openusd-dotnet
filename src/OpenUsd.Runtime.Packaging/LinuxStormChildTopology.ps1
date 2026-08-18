#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

$script:OpenUsdStormChildLinkName = 'libopenusd_storm_child.so'
$script:OpenUsdStormChildSoname = 'libopenusd_storm_child.so.8'
$script:OpenUsdStormChildVersionName = 'libopenusd_storm_child.so.8.0.0'

function Assert-OpenUsdStormChildTopology
{
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Entries
    )

    $entriesByName = @{}
    foreach ($entry in $Entries)
    {
        $name = [string]$entry.name
        if ([string]::IsNullOrWhiteSpace($name) -or $entriesByName.ContainsKey($name))
        {
            throw "The Storm child topology contains an empty or duplicate entry '$name'."
        }
        if ($name -cnotin @(
            $script:OpenUsdStormChildLinkName,
            $script:OpenUsdStormChildSoname,
            $script:OpenUsdStormChildVersionName))
        {
            throw "Unexpected Storm child library entry '$name'."
        }
        $entriesByName.Add($name, $entry)
    }

    if (-not $entriesByName.ContainsKey($script:OpenUsdStormChildLinkName) -or
        -not $entriesByName.ContainsKey($script:OpenUsdStormChildSoname))
    {
        throw (
            'The Storm child topology requires libopenusd_storm_child.so and ' +
            'libopenusd_storm_child.so.8.')
    }

    $unversioned = $entriesByName[$script:OpenUsdStormChildLinkName]
    if ([string]$unversioned.type -cne 'symlink' -or
        [string]$unversioned.target -cne $script:OpenUsdStormChildSoname)
    {
        throw (
            'libopenusd_storm_child.so must be a symbolic link whose exact target is ' +
            'libopenusd_storm_child.so.8.')
    }

    $sonameEntry = $entriesByName[$script:OpenUsdStormChildSoname]
    if ([string]$sonameEntry.type -cne 'symlink' -or
        [string]$sonameEntry.target -cne $script:OpenUsdStormChildVersionName)
    {
        throw (
            'libopenusd_storm_child.so.8 must be a symbolic link whose exact target is ' +
            'libopenusd_storm_child.so.8.0.0.')
    }

    if (-not $entriesByName.ContainsKey($script:OpenUsdStormChildVersionName) -or
        [string]$entriesByName[$script:OpenUsdStormChildVersionName].type -cne 'regular')
    {
        throw (
            "The final Storm child target '$script:OpenUsdStormChildVersionName' " +
            'must be one regular file.')
    }

    if ($entriesByName.Count -ne 3)
    {
        throw (
            "The Storm child topology has $($entriesByName.Count) entries; " +
            'the exact link chain requires 3.')
    }

    return [ordered]@{
        soname = $script:OpenUsdStormChildSoname
        linkName = $script:OpenUsdStormChildLinkName
        realFile = $script:OpenUsdStormChildVersionName
        entries = @($Entries | Sort-Object { [string]$_.name })
    }
}

function Get-OpenUsdStormChildFileTopology
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$LibraryPath,
        [switch]$AllowLinkPlaceholders
    )

    $directory = [System.IO.Path]::GetDirectoryName(
        [System.IO.Path]::GetFullPath($LibraryPath))
    $entries = @()
    foreach ($item in @(Get-ChildItem -LiteralPath $directory -Force |
        Where-Object { $_.Name -like 'libopenusd_storm_child.so*' }))
    {
        $target = $null
        $type = 'regular'
        if ($item.LinkType -eq 'SymbolicLink')
        {
            $type = 'symlink'
            $target = [string]$item.Target
        }
        elseif (-not [string]::IsNullOrEmpty([string]$item.LinkType))
        {
            throw "Storm child entry '$($item.Name)' uses unsupported link type '$($item.LinkType)'."
        }
        elseif ($AllowLinkPlaceholders -and
            $item.Length -lt 256 -and
            $item.Name -in @(
                $script:OpenUsdStormChildLinkName,
                $script:OpenUsdStormChildSoname))
        {
            $candidate = [System.IO.File]::ReadAllText($item.FullName)
            if ($candidate -cin @(
                $script:OpenUsdStormChildSoname,
                $script:OpenUsdStormChildVersionName))
            {
                $type = 'symlink'
                $target = $candidate
            }
        }

        if ($type -eq 'symlink' -and
            ([System.IO.Path]::IsPathRooted($target) -or
                $target.Contains('/') -or
                $target.Contains('\')))
        {
            throw "Storm child link '$($item.Name)' has a non-local target '$target'."
        }

        $entries += [ordered]@{
            name = $item.Name
            type = $type
            target = $target
        }
    }

    $topology = Assert-OpenUsdStormChildTopology -Entries $entries
    $realPath = Join-Path $directory $topology.realFile
    if (-not (Test-Path -LiteralPath $realPath -PathType Leaf))
    {
        throw "The final Storm child ELF is missing: $realPath"
    }
    $topology['realPath'] = $realPath
    return $topology
}
