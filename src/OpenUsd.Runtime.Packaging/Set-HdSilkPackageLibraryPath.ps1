#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SourcePath,
    [Parameter(Mandatory)]
    [string]$DestinationPath,
    [Parameter(Mandatory)]
    [string]$LibraryPath
)

$ErrorActionPreference = 'Stop'
$document = Get-Content $SourcePath -Raw | ConvertFrom-Json
$plugins = @($document.Plugins)
if ($plugins.Count -ne 1 -or $plugins[0].Name -ne 'hdSilk')
{
    throw "Expected exactly one hdSilk plugin in '$SourcePath'."
}

$plugins[0].LibraryPath = $LibraryPath
New-Item -ItemType Directory -Force -Path (Split-Path $DestinationPath) | Out-Null
$document | ConvertTo-Json -Depth 100 | Set-Content $DestinationPath -Encoding utf8NoBOM
