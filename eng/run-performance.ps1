#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateSet('Smoke', 'Artifacts')]
    [string]$Mode = 'Smoke',

    [ValidateSet('Dry', 'Short')]
    [string]$BenchmarkJob,

    [ValidateRange(1, 20)]
    [int]$Repeat = 3,

    [ValidateNotNullOrEmpty()]
    [string]$Configuration = 'Release',

    [ValidateNotNullOrEmpty()]
    [string]$ArtifactsPath = 'artifacts/performance',

    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repoRoot

$sdkVersion = (& dotnet --version).Trim()
if ($sdkVersion -cne '10.0.301')
{
    throw "Performance gates require .NET SDK 10.0.301; found '$sdkVersion'."
}

$Mode = if ($Mode.Equals('Artifacts', [StringComparison]::OrdinalIgnoreCase))
{
    'Artifacts'
}
else
{
    'Smoke'
}
if ($PSBoundParameters.ContainsKey('BenchmarkJob'))
{
    $BenchmarkJob = if (
        $BenchmarkJob.Equals('Short', [StringComparison]::OrdinalIgnoreCase))
    {
        'Short'
    }
    else
    {
        'Dry'
    }
}
if (-not $PSBoundParameters.ContainsKey('BenchmarkJob'))
{
    $BenchmarkJob = if ($Mode -ceq 'Artifacts') { 'Short' } else { 'Dry' }
}

$outputRoot = if ([System.IO.Path]::IsPathRooted($ArtifactsPath))
{
    [System.IO.Path]::GetFullPath($ArtifactsPath)
}
else
{
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ArtifactsPath))
}
$modeRoot = Join-Path $outputRoot $Mode.ToLowerInvariant()
if (Test-Path -LiteralPath $modeRoot)
{
    Remove-Item -LiteralPath $modeRoot -Recurse -Force
}
New-Item -ItemType Directory -Force $modeRoot | Out-Null

$testProject = Join-Path $repoRoot `
    'tests/OpenUsd.Performance.Tests/OpenUsd.Performance.Tests.csproj'
$benchmarkProject = Join-Path $repoRoot `
    'benchmarks/OpenUsd.Benchmarks/OpenUsd.Benchmarks.csproj'
$managedTestRunner = Join-Path $PSScriptRoot 'run-managed-tests.ps1'
$summaryPath = Join-Path $modeRoot 'summary.json'
$summary = [ordered]@{
    schemaVersion = 1
    status = 'running'
    mode = $Mode
    sdkVersion = $sdkVersion
    configuration = $Configuration
    deterministicRuns = $Repeat
    benchmarkJob = $BenchmarkJob
    benchmarkResultsInformational = $true
}

function Invoke-LoggedDotNet
{
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    & dotnet @Arguments 2>&1 | Tee-Object -FilePath $LogPath
    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet $($Arguments -join ' ') exited with code $LASTEXITCODE."
    }
}

try
{
    if (-not $NoBuild)
    {
        Invoke-LoggedDotNet `
            -Arguments @(
                'build',
                $testProject,
                '-c', $Configuration,
                '-f', 'net10.0',
                '--nologo',
                '-p:OpenUsdRequireMetalShaderLibrary=false'
            ) `
            -LogPath (Join-Path $modeRoot 'build-tests.log')
        Invoke-LoggedDotNet `
            -Arguments @(
                'build',
                $benchmarkProject,
                '-c', $Configuration,
                '-f', 'net10.0',
                '--nologo',
                '-p:OpenUsdRequireMetalShaderLibrary=false'
            ) `
            -LogPath (Join-Path $modeRoot 'build-benchmarks.log')
    }

    for ($run = 1; $run -le $Repeat; $run++)
    {
        $testResults = Join-Path $modeRoot "tests-$run"
        Write-Host "[performance] Deterministic safety run $run of $Repeat"
        & $managedTestRunner `
            -Project $testProject `
            -Framework net10.0 `
            -Configuration $Configuration `
            -MinimumExpectedTests 18 `
            -TestArguments @('--results-directory', $testResults)
        if ($LASTEXITCODE -ne 0)
        {
            throw "Deterministic safety run $run failed with exit code $LASTEXITCODE."
        }
    }

    $benchmarkRoot = Join-Path $modeRoot 'BenchmarkDotNet.Artifacts'
    $benchmarkArguments = @(
        'run',
        '--project', $benchmarkProject,
        '-c', $Configuration,
        '-f', 'net10.0',
        '--no-build',
        '--',
        '--job', $BenchmarkJob,
        '--artifacts', $benchmarkRoot,
        '--exporters', 'fulljson',
        '--join'
    )
    if ($Mode -ceq 'Smoke')
    {
        $benchmarkArguments += @(
            '--filter',
            '*PackedStringBenchmarks.PackUtf8Strings*',
            '*SilkCommandBenchmarks.ParseCommandPage*')
    }
    else
    {
        $benchmarkArguments += @('--filter', '*')
    }

    Write-Host "[performance] BenchmarkDotNet $BenchmarkJob run ($Mode)"
    Invoke-LoggedDotNet `
        -Arguments $benchmarkArguments `
        -LogPath (Join-Path $modeRoot 'benchmark.log')
    $summary.status = 'passed'
}
catch
{
    $summary.status = 'failed'
    $summary.error = $_.Exception.Message
    throw
}
finally
{
    $summary | ConvertTo-Json | Set-Content -LiteralPath $summaryPath
}
