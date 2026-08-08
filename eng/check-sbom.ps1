#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
<#
.SYNOPSIS
    Verifies that the checked release SBOM matches the pinned dependency inputs.
#>
[CmdletBinding()]
param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')),
    [switch]$Update
)

$ErrorActionPreference = 'Stop'

$generator = Join-Path $Root 'eng/generate-sbom.py'
$output = Join-Path $Root 'eng/sbom/openusd-release.cdx.json'

if ($Update)
{
    python $generator --output $output
}
else
{
    python $generator --output $output --check
}

if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

python $generator --output $output --validate
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}
