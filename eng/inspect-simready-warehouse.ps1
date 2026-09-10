#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AuditExecutable,
    [Parameter(Mandatory = $true)]
    [string]$NativeRuntimeRoot,
    [Parameter(Mandatory = $true)]
    [string]$ReportPath,
    [ValidateSet('Audit', 'Localize')]
    [string]$Operation = 'Audit',
    [string]$DataRoot = 'D:\simready',
    [string]$CacheRoot,
    [switch]$WithMdlCore,
    [string]$ProfilePath = (Join-Path $PSScriptRoot 'datasets\simready-warehouse-01.json')
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
if (-not $IsWindows)
{
    throw 'The current warehouse runtime wrapper requires Windows.'
}
foreach ($path in @($AuditExecutable, $NativeRuntimeRoot, $ReportPath, $DataRoot))
{
    if (-not [IO.Path]::IsPathFullyQualified($path))
    {
        throw 'Audit executable, runtime, report and data paths must be absolute.'
    }
}
if (-not (Test-Path -LiteralPath $AuditExecutable -PathType Leaf))
{
    throw 'Build the openusd_warehouse_audit_probe target before running the audit.'
}
$report = [IO.Path]::GetFullPath($ReportPath)
$data = [IO.Path]::GetFullPath($DataRoot)
if ((Test-Path -LiteralPath $report) -or (Test-Path -LiteralPath ($report + '.log')))
{
    throw "Existing audit evidence will not be overwritten: $report"
}
if (($Operation -eq 'Localize' -and [string]::IsNullOrWhiteSpace($CacheRoot)) -or
    (-not [string]::IsNullOrWhiteSpace($CacheRoot) -and -not [IO.Path]::IsPathFullyQualified($CacheRoot)))
{
    throw 'Localization requires an explicit new absolute cache directory.'
}
if ($WithMdlCore -and $Operation -eq 'Localize')
{
    throw 'Localize source layers first, then audit the derived cache with the optional MDL core.'
}
$profile = Get-Content -LiteralPath $ProfilePath -Raw | ConvertFrom-Json
$source = Join-Path $data "$($profile.id)\source\$($profile.revision)"
if ($report.StartsWith($source + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))
{
    throw 'Audit evidence cannot be written inside the immutable source snapshot.'
}
$plugin = Join-Path $NativeRuntimeRoot 'plugin\usd'
foreach ($path in @(
    (Join-Path $NativeRuntimeRoot 'bin\openusd_dotnet.dll'),
    (Join-Path $NativeRuntimeRoot 'lib\usd_ms.dll'),
    (Join-Path $plugin 'plugInfo.json')))
{
    if (-not (Test-Path -LiteralPath $path -PathType Leaf))
    {
        throw "The matched Windows audit runtime is missing: $path"
    }
}

& (Join-Path $PSScriptRoot 'fetch-simready-warehouse.ps1') `
    -Operation Verify -DataRoot $data -ProfilePath $ProfilePath | Out-Host
$parent = Split-Path -Parent $report
New-Item -ItemType Directory -Path $parent -Force | Out-Null
$temporary = Join-Path $parent ('.warehouse-audit-' + [Guid]::NewGuid().ToString('N') + '.json')
$diagnostics = $temporary + '.log'
$previousPath = $env:PATH
$previousPlugins = $env:PXR_PLUGINPATH_NAME
$previousSearch = $env:PXR_AR_DEFAULT_SEARCH_PATH
try
{
    $env:PATH = "$(Join-Path $NativeRuntimeRoot 'bin');$(Join-Path $NativeRuntimeRoot 'lib');$env:PATH"
    $env:PXR_PLUGINPATH_NAME = $plugin
    $env:PXR_AR_DEFAULT_SEARCH_PATH = $null
    $moduleRoot = $null
    if ($WithMdlCore)
    {
        $moduleReport = @(& python (Join-Path $PSScriptRoot 'fetch_simready_mdl.py') `
            --operation Verify --data-root $data)
        if ($LASTEXITCODE -ne 0)
        {
            throw 'The pinned standalone MDL sources did not pass verification.'
        }
        $moduleRoot = ($moduleReport -join "`n" | ConvertFrom-Json).moduleRoot
        $env:PXR_AR_DEFAULT_SEARCH_PATH = $moduleRoot
    }
    $arguments = if ($Operation -eq 'Localize')
    {
        @('--localize', $plugin, $source, $CacheRoot, [IO.Path]::GetFullPath($ProfilePath))
    }
    else
    {
        $root = if ([string]::IsNullOrWhiteSpace($CacheRoot)) { $source } else { $CacheRoot }
        @($plugin, (Join-Path $root $profile.rootScene))
    }
    & $AuditExecutable @arguments 1> $temporary 2> $diagnostics
    $code = $LASTEXITCODE
    if ($code -gt 1)
    {
        Get-Content -LiteralPath $diagnostics -Tail 8 | Write-Error
        throw "Native warehouse audit failed with exit code $code."
    }
    $result = Get-Content -LiteralPath $temporary -Raw | ConvertFrom-Json -AsHashtable
    if ($result.schemaVersion -ne 1)
    {
        throw 'The native audit did not produce a supported report.'
    }
    $result.provenance = [ordered]@{
        dataset = $profile.dataset
        revision = $profile.revision
        inventorySha256 = $profile.inventory.sha256
        profileSha256 = (Get-FileHash -LiteralPath $ProfilePath -Algorithm SHA256).Hash
        auditExecutableSha256 = (Get-FileHash -LiteralPath $AuditExecutable -Algorithm SHA256).Hash
        usdLibrarySha256 = (Get-FileHash -LiteralPath (Join-Path $NativeRuntimeRoot 'lib\usd_ms.dll') -Algorithm SHA256).Hash
        sourceSnapshotVerified = $true
        mdlModuleRoot = $moduleRoot
        exitCode = $code
    }
    $result | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $temporary -Encoding utf8NoBOM
    Move-Item -LiteralPath $temporary -Destination $report
    Move-Item -LiteralPath $diagnostics -Destination ($report + '.log')
    Write-Output "Warehouse $Operation report: $report"
    if ($code -ne 0)
    {
        throw 'The warehouse audit found unresolved dependencies or invalid content; see the report, not a rendering-success claim.'
    }
}
finally
{
    $env:PATH = $previousPath
    $env:PXR_PLUGINPATH_NAME = $previousPlugins
    $env:PXR_AR_DEFAULT_SEARCH_PATH = $previousSearch
    if (Test-Path -LiteralPath $temporary -PathType Leaf)
    {
        Write-Warning "Failed audit output retained for diagnosis: $temporary"
    }
    if (Test-Path -LiteralPath $diagnostics -PathType Leaf)
    {
        Write-Warning "Failed audit diagnostics retained: $diagnostics"
    }
}
