#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$shaderRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$validatorPath = Join-Path $shaderRoot 'validate-checked-payload.ps1'
$manifestPath = Join-Path $shaderRoot 'shader-manifest.json'
$source = Get-Content -LiteralPath $validatorPath -Raw
$start = $source.IndexOf('$authoredProgramMatches = @(', [StringComparison]::Ordinal)
if ($start -lt 0)
{
    throw 'The checked Metal precision selection was not found.'
}
$end = $source.IndexOf('$metalArguments += @(', $start, [StringComparison]::Ordinal)
if ($end -lt 0)
{
    throw 'The checked Metal precision selection was not found.'
}
$selection = [ScriptBlock]::Create($source.Substring($start, $end - $start))
$shaderManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$checkedManifest = Get-Content -LiteralPath (Join-Path (Join-Path $shaderRoot 'checked') 'manifest.json') -Raw |
    ConvertFrom-Json
$model = 'import json,sys; sys.path.insert(0,sys.argv[1]); import shader_model; ' +
    'm=json.load(open(sys.argv[2], encoding="utf-8")); ' +
    'print(json.dumps([{"name": p["name"], "mode": p.get("floatingPointMode")} ' +
    'for p in shader_model.metal_library_programs(m)]))'
$expectedJson = & python -c $model (Join-Path $shaderRoot 'scripts') $manifestPath
if ($LASTEXITCODE -ne 0)
{
    throw 'The Metal library precision model could not be loaded.'
}
$expected = @($expectedJson | ConvertFrom-Json)
$precise = 0
$default = 0
foreach ($entry in $expected)
{
    $programMatches = @($checkedManifest.programs | Where-Object name -CEQ $entry.name)
    if ($programMatches.Count -ne 1)
    {
        throw "Expected one checked program for $($entry.name)."
    }
    $program = $programMatches[0]
    $metalArguments = @()
    . $selection
    if ($entry.mode -ceq 'precise')
    {
        if ($metalArguments.Count -ne 1 -or $metalArguments[0] -cne '-fno-fast-math')
        {
            throw "$($entry.name) must disable Metal fast math."
        }
        $precise++
    }
    else
    {
        if ($null -ne $entry.mode -or $metalArguments.Count -ne 0)
        {
            throw "$($entry.name) must use the default Metal floating-point mode."
        }
        $default++
    }
}
if ($precise -eq 0 -or $default -eq 0)
{
    throw 'The Metal library must exercise both precise and default floating-point modes.'
}
Write-Host "Validated Metal precision selection for $precise precise and $default default programs."
