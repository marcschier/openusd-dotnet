#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
<#
.SYNOPSIS
    Inventories, acquires or verifies the pinned complete SimReady warehouse snapshot.
.DESCRIPTION
    Inventory is metadata-only and reports storage admission without downloading assets.
    Download resumes verified files and partials only after the entire remaining source and
    cache/output budgets fit. Existing source changes are never overwritten. See
    docs\simready-warehouse.md for provenance, bounds and the still-unfinished rendering work.
#>
[CmdletBinding()]
param(
    [ValidateSet('Inventory', 'Download', 'Verify')]
    [string]$Operation = 'Inventory',
    [string]$DataRoot = 'D:\simready',
    [string]$ProfilePath = (Join-Path $PSScriptRoot 'datasets\simready-warehouse-01.json')
)

$ErrorActionPreference = 'Stop'
& python (Join-Path $PSScriptRoot 'simready_warehouse.py') `
    --operation $Operation --data-root $DataRoot --profile $ProfilePath
if ($LASTEXITCODE -ne 0)
{
    throw "Warehouse $Operation failed with exit code $LASTEXITCODE; see the explicit acquisition/storage diagnostic."
}
