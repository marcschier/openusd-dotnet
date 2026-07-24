#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$CoverageFile,
    [double]$MinLineRate = 0.80
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $CoverageFile))
{
    Write-Host "Coverage file not found: $CoverageFile" -ForegroundColor Red
    exit 1
}

[xml]$report = Get-Content $CoverageFile
$lineRate = [double]$report.coverage.'line-rate'

Write-Host ("Line coverage: {0:P2}" -f $lineRate)
Write-Host ("Required:      {0:P0}" -f $MinLineRate)

if ($lineRate -lt $MinLineRate)
{
    Write-Host "Coverage gate failed." -ForegroundColor Red
    exit 1
}

Write-Host "Coverage gate passed." -ForegroundColor Green
