#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$manifestPath = Join-Path $PSScriptRoot 'shader-manifest.json'
$requiredInputsJson = & python `
    (Join-Path $PSScriptRoot 'scripts/checked_payload.py') `
    --print-required-inputs `
    --manifest $manifestPath
if ($LASTEXITCODE -ne 0)
{
    throw 'Could not read the required checked input set.'
}

$requiredInputs = @($requiredInputsJson | ConvertFrom-Json)
if ($requiredInputs.Count -eq 0)
{
    throw 'The required checked input set is empty.'
}

foreach ($relativePath in $requiredInputs)
{
    $fullPath = Join-Path $repoRoot $relativePath
    if (-not (Test-Path $fullPath -PathType Leaf))
    {
        throw "Required checked input is missing: $relativePath"
    }
    $content = [System.IO.File]::ReadAllBytes($fullPath)
    if ($content -contains [byte]13)
    {
        throw (
            'Required checked input must use LF-only line endings: ' +
            $relativePath)
    }
}

Write-Host (
    "Validated LF-only line endings for $($requiredInputs.Count) " +
    'required checked inputs.')
