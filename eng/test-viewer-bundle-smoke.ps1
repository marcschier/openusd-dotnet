#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid = 'win-x64',
    [Parameter(Mandatory = $true)]
    [string]$BundlePath,
    [string]$StagePath = (Join-Path $PSScriptRoot '../test-assets/minimal.usda'),
    [string]$OutputRoot = 'artifacts/viewer-distribution-smoke',
    [ValidateRange(10, 600)]
    [int]$SmokeSeconds = 120,
    [string]$ExpectedStatusPattern = 'frame rendered'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$bundle = (Resolve-Path $BundlePath).Path
if (-not (Test-Path $bundle))
{
    throw "Viewer bundle not found: $bundle"
}

$checksumPath = "$bundle.sha256"
if (-not (Test-Path $checksumPath))
{
    throw "Viewer bundle checksum not found: $checksumPath"
}
$checksumLine = (Get-Content $checksumPath -Raw).Trim()
$parts = $checksumLine -split '\s+', 2
if ($parts.Count -ne 2)
{
    throw "Invalid checksum file: $checksumPath"
}
$expected = $parts[0].ToUpperInvariant()
$actual = (Get-FileHash $bundle -Algorithm SHA256).Hash.ToUpperInvariant()
if ($actual -cne $expected)
{
    throw "Checksum verification failed for $bundle. Expected $expected; actual $actual."
}

$outputRoot = if ([IO.Path]::IsPathRooted($OutputRoot))
{
    $OutputRoot
}
else
{
    Join-Path $repoRoot $OutputRoot
}
$installRoot = Join-Path $outputRoot $Rid
Remove-Item $installRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
if ($bundle.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase))
{
    Expand-Archive -Path $bundle -DestinationPath $installRoot -Force
}
else
{
    & tar -xzf $bundle -C $installRoot
    if ($LASTEXITCODE -ne 0)
    {
        throw "tar failed while extracting $bundle."
    }
}

foreach ($requiredPath in @(
    (Join-Path $installRoot 'plugin/usd/plugInfo.json'),
    (Join-Path $installRoot 'plugin/usd/hdStorm/resources/plugInfo.json')))
{
    if (-not (Test-Path $requiredPath))
    {
        throw "The installed Viewer bundle is missing required native asset: $requiredPath"
    }
}

$stage = [IO.Path]::GetFullPath($StagePath)
if (-not (Test-Path $stage))
{
    throw "Stage file not found: $stage"
}
$stagedStage = Join-Path $installRoot ([IO.Path]::GetFileName($stage))
Copy-Item $stage $stagedStage -Force

$executable = Join-Path $installRoot 'OpenUsd.Viewer.App'
if ($IsWindows)
{
    $executable += '.exe'
}
if (-not (Test-Path $executable))
{
    throw "Viewer executable not found: $executable"
}

$statusFile = Join-Path $installRoot 'viewer-status.txt'
$logFile = Join-Path $installRoot 'viewer.log'
$stdoutFile = Join-Path $installRoot 'viewer.stdout.log'
$stderrFile = Join-Path $installRoot 'viewer.stderr.log'
Remove-Item $statusFile, $logFile, $stdoutFile, $stderrFile `
    -Force `
    -ErrorAction SilentlyContinue

function Write-ViewerDiagnostics
{
    foreach ($diagnostic in @(
        @{ Name = 'status'; Path = $statusFile },
        @{ Name = 'Avalonia trace'; Path = $logFile },
        @{ Name = 'stdout'; Path = $stdoutFile },
        @{ Name = 'stderr'; Path = $stderrFile }))
    {
        Write-Host "----- viewer $($diagnostic.Name) -----"
        if (Test-Path $diagnostic.Path)
        {
            Get-Content $diagnostic.Path | Write-Host
        }
        else
        {
            Write-Host '(not produced)'
        }
    }
}

$oldPath = $env:PATH
$oldPluginPath = $env:OPENUSD_PLUGIN_PATH
$oldStagePath = $env:OPENUSD_STAGE_PATH
$oldStatusFile = $env:OPENUSD_STATUS_FILE
$oldLogFile = $env:OPENUSD_LOG_FILE
$oldLdLibraryPath = $env:LD_LIBRARY_PATH
$oldDyldLibraryPath = $env:DYLD_LIBRARY_PATH
$process = $null
try
{
    $env:PATH = $installRoot + [IO.Path]::PathSeparator + $oldPath
    $env:LD_LIBRARY_PATH = $installRoot + [IO.Path]::PathSeparator + $oldLdLibraryPath
    $env:DYLD_LIBRARY_PATH = $installRoot + [IO.Path]::PathSeparator + $oldDyldLibraryPath
    $env:OPENUSD_PLUGIN_PATH = Join-Path $installRoot 'plugin/usd'
    $env:OPENUSD_STAGE_PATH = $stagedStage
    $env:OPENUSD_STATUS_FILE = $statusFile
    $env:OPENUSD_LOG_FILE = $logFile

    $arguments = if ($IsWindows) { @('--windows-rendering=angle') } else { @() }
    $process = Start-Process $executable -PassThru `
        -ArgumentList $arguments `
        -RedirectStandardOutput $stdoutFile `
        -RedirectStandardError $stderrFile
    $deadline = [DateTime]::UtcNow.AddSeconds($SmokeSeconds)
    $renderedStatus = $null
    while ([DateTime]::UtcNow -lt $deadline)
    {
        $process.Refresh()
        if (Test-Path $statusFile)
        {
            $statuses = @(Get-Content $statusFile)
            $renderedStatus = $statuses |
                Where-Object { $_ -match $ExpectedStatusPattern } |
                Select-Object -Last 1
            if ($null -ne $renderedStatus)
            {
                break
            }
            $failure = $statuses |
                Where-Object {
                    $_ -match '^(Renderer initialization failed|Renderer frame failed|' +
                        'Viewer diagnostic sequence failed|Renderer failed|' +
                        'Renderer unavailable|Renderer lost)'
                } |
                Select-Object -Last 1
            if ($null -ne $failure)
            {
                throw "Viewer reported a renderer failure: $failure"
            }
        }
        if ($process.HasExited)
        {
            throw "Viewer exited before rendering a frame with code $($process.ExitCode)."
        }
        Start-Sleep -Milliseconds 250
    }
    if ($null -eq $renderedStatus)
    {
        throw "Viewer did not report pattern '$ExpectedStatusPattern' within $SmokeSeconds seconds."
    }
    if ($process.HasExited)
    {
        throw "Viewer exited after reporting a frame with code $($process.ExitCode)."
    }
    Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
    $process.WaitForExit()
    Write-Output "VIEWER_BUNDLE_SMOKE_RENDERED rid=$Rid status=$renderedStatus"
}
catch
{
    Write-ViewerDiagnostics
    throw
}
finally
{
    if ($process -is [Diagnostics.Process] -and -not $process.HasExited)
    {
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
        $process.WaitForExit()
    }
    $env:PATH = $oldPath
    $env:OPENUSD_PLUGIN_PATH = $oldPluginPath
    $env:OPENUSD_STAGE_PATH = $oldStagePath
    $env:OPENUSD_STATUS_FILE = $oldStatusFile
    $env:OPENUSD_LOG_FILE = $oldLogFile
    $env:LD_LIBRARY_PATH = $oldLdLibraryPath
    $env:DYLD_LIBRARY_PATH = $oldDyldLibraryPath
}
