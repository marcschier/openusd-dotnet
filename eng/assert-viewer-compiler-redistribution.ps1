#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BundleRoot
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $BundleRoot -PathType Container))
{
    throw "Viewer layout directory not found: $BundleRoot"
}

foreach ($file in Get-ChildItem -LiteralPath $BundleRoot -Recurse -File -Force)
{
    if ($file.Name -imatch '^(?:lib)?spirv[-_]?cross(?:\.dll|\.dylib|\.so(?:\.[0-9]+)*)$')
    {
        throw "Viewer archive cannot include '$($file.FullName)': " +
            'SPIRV-Cross source reproduction and independent redistribution review are incomplete.'
    }
    if ($file.Name -imatch '^(?:dxc\.exe|(?:lib)?dxcompiler\.(?:dll|so|dylib)|(?:lib)?dxil\.(?:dll|so|dylib))$')
    {
        throw "Viewer archive cannot include '$($file.FullName)': DXC must be supplied externally."
    }
}
