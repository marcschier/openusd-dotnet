#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if (-not $IsMacOS)
{
    throw 'Xcode selection requires macOS.'
}

$modelJson = & python (Join-Path $PSScriptRoot 'scripts/shader-commands.py') `
    --lock (Join-Path $PSScriptRoot 'toolchain.lock.json') `
    --manifest (Join-Path $PSScriptRoot 'shader-manifest.json') `
    --model-only
if ($LASTEXITCODE -ne 0)
{
    throw 'Could not read the locked shader toolchain model.'
}
$model = $modelJson | ConvertFrom-Json
$developerRoot = "/Applications/Xcode_$($model.xcodeVersion).app/Contents/Developer"
if (-not (Test-Path $developerRoot))
{
    throw "Locked Xcode was not found at $developerRoot."
}

& sudo xcode-select -s $developerRoot
if ($LASTEXITCODE -ne 0)
{
    throw "Could not select locked Xcode $($model.xcodeVersion)."
}
$actual = (& xcodebuild -version | Select-Object -First 1).Trim()
if ($actual -ne "Xcode $($model.xcodeVersion)")
{
    throw "Expected Xcode $($model.xcodeVersion), got '$actual'."
}

Write-Host "Selected $actual."

