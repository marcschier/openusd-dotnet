#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateRange(30, 600)]
    [int]$TimeoutSeconds = 120,
    [ValidateRange(1, 100)]
    [int]$RepeatCount = 1,
    [string]$StagePath = (Join-Path $PSScriptRoot '../test-assets/minimal.usda'),
    [string]$OutputPath,
    [switch]$NoBuild,
    [switch]$Aot
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows)
{
    throw 'The D3D12 Avalonia composition smoke requires Windows.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$openUsdRoot = Join-Path $repoRoot 'native/install/win-x64'
$shimRoot = Join-Path $repoRoot 'native/install/shim/win-x64'
$publishRoot = if ([string]::IsNullOrWhiteSpace($OutputPath))
{
    Join-Path $repoRoot 'artifacts/d3d12-composition-smoke/win-x64'
}
else
{
    [System.IO.Path]::GetFullPath($OutputPath)
}
$project = Join-Path $repoRoot `
    'tests/OpenUsd.D3D12CompositionSmoke/OpenUsd.D3D12CompositionSmoke.csproj'

if (-not (Test-Path $openUsdRoot) -or -not (Test-Path $shimRoot))
{
    throw 'Build or stage the win-x64 native runtime and hdSilk plugin first.'
}
if (Test-Path $publishRoot)
{
    Remove-Item $publishRoot -Recurse -Force
}

$publishArguments = @(
    'publish',
    $project,
    '-c', 'Release',
    '-f', 'net10.0',
    '-r', 'win-x64',
    '-o', $publishRoot,
    '--nologo'
)
if ($Aot)
{
    $publishArguments += '-p:AotProbe=true'
}
if ($NoBuild)
{
    $publishArguments += '--no-build'
}
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

$binTarget = Join-Path $publishRoot 'bin'
$libTarget = Join-Path $publishRoot 'lib'
New-Item -ItemType Directory -Force -Path $binTarget, $libTarget | Out-Null
foreach ($layout in @(
    @{ Source = (Join-Path $openUsdRoot 'bin'); Target = $binTarget },
    @{ Source = (Join-Path $openUsdRoot 'lib'); Target = $libTarget },
    @{ Source = (Join-Path $shimRoot 'bin'); Target = $binTarget },
    @{ Source = (Join-Path $shimRoot 'lib'); Target = $libTarget }))
{
    if (Test-Path $layout.Source)
    {
        Get-ChildItem $layout.Source -File |
            Where-Object { $_.Name -match '\.dll$' } |
            Copy-Item -Destination $layout.Target -Force
    }
}

$pluginPath = Join-Path $publishRoot 'plugin/usd'
New-Item -ItemType Directory -Force -Path $pluginPath | Out-Null
foreach ($pluginSource in @(
    (Join-Path $openUsdRoot 'lib/usd'),
    (Join-Path $openUsdRoot 'plugin/usd'),
    (Join-Path $shimRoot 'plugin/usd')))
{
    if (Test-Path $pluginSource)
    {
        Copy-Item (Join-Path $pluginSource '*') $pluginPath -Recurse -Force
    }
}
foreach ($requiredPluginFile in @(
    (Join-Path $pluginPath 'plugInfo.json'),
    (Join-Path $pluginPath 'hdSilk/resources/plugInfo.json')))
{
    if (-not (Test-Path $requiredPluginFile))
    {
        throw "The staged plugin tree is incomplete: $requiredPluginFile"
    }
}

$stageSource = [System.IO.Path]::GetFullPath($StagePath)
if (-not (Test-Path $stageSource))
{
    throw "Stage file not found: $stageSource"
}
$stagedStage = Join-Path $publishRoot ([System.IO.Path]::GetFileName($stageSource))
Copy-Item $stageSource $stagedStage -Force

$executable = Join-Path $publishRoot 'OpenUsd.D3D12CompositionSmoke.exe'

$oldPath = $env:PATH
$oldPluginPath = $env:OPENUSD_PLUGIN_PATH
$oldStagePath = $env:OPENUSD_STAGE_PATH
$oldStatusFile = $env:OPENUSD_STATUS_FILE
$oldLogFile = $env:OPENUSD_LOG_FILE
$oldArtifactDir = $env:OPENUSD_ARTIFACT_DIR

function Write-SmokeDiagnostics
{
    param(
        [string]$StatusFile,
        [string]$LogFile,
        [string]$StdoutFile,
        [string]$StderrFile
    )

    foreach ($diagnostic in @(
        @{ Name = 'status'; Path = $StatusFile },
        @{ Name = 'Avalonia'; Path = $LogFile },
        @{ Name = 'stdout'; Path = $StdoutFile },
        @{ Name = 'stderr'; Path = $StderrFile }))
    {
        Write-Host "----- D3D12 smoke $($diagnostic.Name) -----"
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

function Invoke-SmokeRun
{
    param(
        [int]$Run,
        [string]$RunRoot
    )

    New-Item -ItemType Directory -Force -Path $RunRoot | Out-Null
    $statusFile = Join-Path $RunRoot 'status.log'
    $logFile = Join-Path $RunRoot 'avalonia.log'
    $stdoutFile = Join-Path $RunRoot 'stdout.log'
    $stderrFile = Join-Path $RunRoot 'stderr.log'
    Remove-Item $statusFile, $logFile, $stdoutFile, $stderrFile `
        -Force -ErrorAction SilentlyContinue
    $process = $null
    try
    {
        $env:OPENUSD_STATUS_FILE = $statusFile
        $env:OPENUSD_LOG_FILE = $logFile
        $env:OPENUSD_ARTIFACT_DIR = $RunRoot
        $process = Start-Process $executable -PassThru `
            -RedirectStandardOutput $stdoutFile `
            -RedirectStandardError $stderrFile
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        $passed = $false
        while ([DateTime]::UtcNow -lt $deadline)
        {
            $process.Refresh()
            $statuses = if (Test-Path $statusFile)
            {
                @(Get-Content $statusFile)
            }
            else
            {
                @()
            }
            if ($statuses | Where-Object { $_ -like 'D3D12_SMOKE_FAIL*' })
            {
                throw 'The D3D12 composition smoke reported failure.'
            }
            if ($statuses | Where-Object { $_ -like 'D3D12_SMOKE_PASS*' })
            {
                $passed = $true
                break
            }
            if ($process.HasExited)
            {
                throw "The D3D12 composition smoke exited with code $($process.ExitCode)."
            }
            Start-Sleep -Milliseconds 100
        }
        if (-not $passed)
        {
            throw "The D3D12 composition smoke timed out after $TimeoutSeconds seconds."
        }
        if (-not $process.WaitForExit(10000))
        {
            throw 'The D3D12 composition smoke passed but did not exit cleanly.'
        }
        if ($process.ExitCode -ne 0)
        {
            throw "The D3D12 composition smoke exited with code $($process.ExitCode)."
        }

        $pixelEvidenceFile = Join-Path $RunRoot 'pixel-evidence.json'
        $initialCaptureFile = Join-Path $RunRoot 'composed-initial.bmp'
        $editedCaptureFile = Join-Path $RunRoot 'composed-edited.bmp'
        foreach ($requiredArtifact in @(
            $pixelEvidenceFile,
            $initialCaptureFile,
            $editedCaptureFile))
        {
            if (-not (Test-Path $requiredArtifact -PathType Leaf))
            {
                throw "The smoke passed without required composed capture artifact $requiredArtifact."
            }
        }
        $pixelEvidence = Get-Content $pixelEvidenceFile -Raw | ConvertFrom-Json
        if ($pixelEvidence.CaptureApi -ne
                'PrintWindow(PW_CLIENTONLY|PW_RENDERFULLCONTENT)+DwmFlush' -or
            $pixelEvidence.Initial.Sha256 -eq $pixelEvidence.Edited.Sha256 -or
            $pixelEvidence.ChangedPixels -le 0 -or
            $pixelEvidence.Lifecycle.StaleImportReuseCount -ne 0)
        {
            throw 'The composed pixel-evidence artifact failed runner validation.'
        }

        $evidence = @(Get-Content $statusFile |
            Where-Object { $_ -like 'D3D12_SMOKE_*' })
        $evidence | Write-Host
        return [pscustomobject]@{
            Run = $Run
            Result = 'PASS'
            Evidence = ($evidence | Where-Object {
                $_ -like 'D3D12_SMOKE_PASS*'
            } | Select-Object -Last 1)
            Artifacts = $RunRoot
        }
    }
    catch
    {
        Write-SmokeDiagnostics $statusFile $logFile $stdoutFile $stderrFile
        return [pscustomobject]@{
            Run = $Run
            Result = 'FAIL'
            Evidence = $_.Exception.Message
            Artifacts = $RunRoot
        }
    }
    finally
    {
        if ($null -ne $process)
        {
            $process.Refresh()
            if (-not $process.HasExited)
            {
                Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
                $process.WaitForExit()
            }
            $process.Dispose()
        }
    }
}

try
{
    $env:PATH = $binTarget + [System.IO.Path]::PathSeparator +
        $libTarget + [System.IO.Path]::PathSeparator +
        $publishRoot + [System.IO.Path]::PathSeparator + $oldPath
    $env:OPENUSD_PLUGIN_PATH = $pluginPath
    $env:OPENUSD_STAGE_PATH = $stagedStage

    $results = @()
    for ($run = 1; $run -le $RepeatCount; $run++)
    {
        $runRoot = if ($RepeatCount -eq 1)
        {
            $publishRoot
        }
        else
        {
            Join-Path $publishRoot ('runs/run-{0:D2}' -f $run)
        }
        Write-Host "----- D3D12 composition smoke run $run/$RepeatCount -----"
        $results += Invoke-SmokeRun $run $runRoot
    }
    Write-Host '----- D3D12 composition smoke summary -----'
    $results | Format-Table Run, Result, Evidence, Artifacts -AutoSize | Out-String |
        Write-Host
    $failures = @($results | Where-Object { $_.Result -ne 'PASS' })
    if ($failures.Count -ne 0)
    {
        throw "$($failures.Count) of $RepeatCount D3D12 composition smoke runs failed."
    }
}
finally
{
    $env:PATH = $oldPath
    $env:OPENUSD_PLUGIN_PATH = $oldPluginPath
    $env:OPENUSD_STAGE_PATH = $oldStagePath
    $env:OPENUSD_STATUS_FILE = $oldStatusFile
    $env:OPENUSD_LOG_FILE = $oldLogFile
    $env:OPENUSD_ARTIFACT_DIR = $oldArtifactDir
}
