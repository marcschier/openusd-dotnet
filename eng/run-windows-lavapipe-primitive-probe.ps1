#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$Configuration = 'Release',

    [Parameter(Mandatory = $true)]
    [string]$EvidenceRoot
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows)
{
    throw 'The Windows lavapipe primitive-ID diagnostic requires Windows.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$lock = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'mesa-lavapipe-test-runtime.lock.json') -Raw |
    ConvertFrom-Json
$loaderLock = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'vulkan-test-runtime.lock.json') -Raw |
    ConvertFrom-Json
$root = Join-Path ([IO.Path]::GetTempPath()) (
    'openusd-lavapipe-primitive-' + [Guid]::NewGuid().ToString('N'))
$evidence = [IO.Path]::GetFullPath($EvidenceRoot)
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
New-Item -ItemType Directory -Path $root | Out-Null
$archive = Join-Path $root ([string]$lock.package.archive)
$runtimeRoot = Join-Path $repoRoot "tests\OpenUsd.Rendering.ConformanceTests\bin\$Configuration\net10.0"
$reportPath = Join-Path $runtimeRoot (
    'TestResults\OpenUsd.Rendering.ConformanceTests-windows-net10.0.tunit-report.json')
$htmlReportPath = Join-Path $runtimeRoot (
    'TestResults\OpenUsd.Rendering.ConformanceTests-windows-net10.0-report.html')
$reports = @(
    [pscustomobject]@{
        Path = $reportPath
        Previous = Join-Path $root 'previous-report.json'
        Current = Join-Path $root 'current-report.json'
        Evidence = 'windows-lavapipe-primitive.tunit-report.json'
        PreviousMoved = $false
    },
    [pscustomobject]@{
        Path = $htmlReportPath
        Previous = Join-Path $root 'previous-report.html'
        Current = Join-Path $root 'current-report.html'
        Evidence = 'windows-lavapipe-primitive.report.html'
        PreviousMoved = $false
    }
)
$keepRoot = $false
$driverRegistration = $null
$oldEnvironment = @{}
$environment = @{
    OPENUSD_PRIMITIVE_PROBE_BACKEND = 'Vulkan'
    OPENUSD_PRIMITIVE_PROBE_EXPECTED_DEVICE_NAME = [string]$lock.runtime.deviceNameContains
    OPENUSD_REQUIRE_SWIFTSHADER = '0'
    MESA_SHADER_CACHE_DISABLE = 'true'
    LP_NUM_THREADS = '1'
}

function Assert-Hash
{
    param([string]$Path, [string]$Expected)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf))
    {
        throw "The required diagnostic asset is missing: $Path"
    }
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($actual -cne $Expected)
    {
        throw "Diagnostic asset hash mismatch for '$Path'. Expected $Expected; actual $actual."
    }
    Write-Host "[lavapipe-primitive] $Path sha256=$actual"
}

$diagnosticFailure = $null
$cleanupFailures = [Collections.Generic.List[Exception]]::new()
try
{
    foreach ($reportFile in $reports)
    {
        $destination = Join-Path $evidence $reportFile.Evidence
        if (Test-Path -LiteralPath $destination)
        {
            throw "Diagnostic report evidence already exists: $destination"
        }
    }
    & curl.exe --fail --location --retry 3 --output $archive ([string]$lock.package.url)
    if ($LASTEXITCODE -ne 0)
    {
        throw "The lavapipe archive download failed with exit code $LASTEXITCODE."
    }
    Assert-Hash -Path $archive -Expected ([string]$lock.package.archiveSha256)
    & tar.exe -xf $archive -C $root ([string]$lock.runtime.manifest) ([string]$lock.runtime.driver)
    if ($LASTEXITCODE -ne 0)
    {
        throw "The lavapipe archive extraction failed with exit code $LASTEXITCODE."
    }
    $manifestPath = Join-Path $root ([string]$lock.runtime.manifest)
    $driverPath = Join-Path $root ([string]$lock.runtime.driver)
    Assert-Hash -Path $driverPath -Expected ([string]$lock.runtime.driverSha256)
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.file_format_version -cne '1.0.1' -or
        $manifest.ICD.api_version -cne [string]$lock.runtime.apiVersion -or
        $manifest.ICD.library_path -cne ('.\' + [string]$lock.runtime.driver))
    {
        throw 'The pinned lavapipe driver manifest has unexpected contents.'
    }

    $loaderPath = @(
        (Join-Path $runtimeRoot ([string]$loaderLock.runtimes.'win-x64'.loader)),
        (Join-Path $runtimeRoot (
            'runtimes\win-x64\native\' + [string]$loaderLock.runtimes.'win-x64'.loader))
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if ($null -eq $loaderPath)
    {
        throw 'The built conformance test has no locked Vulkan loader.'
    }
    Assert-Hash -Path $loaderPath -Expected ([string]$loaderLock.runtimes.'win-x64'.loaderSha256)
    $environment.VK_DRIVER_FILES = $manifestPath
    $environment.VK_ICD_FILENAMES = $manifestPath
    $environment.OPENUSD_VULKAN_LOADER_PATH = $loaderPath
    foreach ($name in $environment.Keys)
    {
        $oldEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $environment[$name], 'Process')
    }
    . (Join-Path $PSScriptRoot 'vulkan-test-runtime-registry.ps1')
    $driverRegistration = Register-VulkanTestRuntimeDriver -ManifestPath $manifestPath

    $primaryFailure = $null
    $retentionFailures = [Collections.Generic.List[Exception]]::new()
    $testStarted = $false
    try
    {
        foreach ($reportFile in $reports)
        {
            if (Test-Path -LiteralPath $reportFile.Path -PathType Leaf)
            {
                Move-Item -LiteralPath $reportFile.Path -Destination $reportFile.Previous
                $reportFile.PreviousMoved = $true
            }
        }
        $testProject = Join-Path $repoRoot (
            'tests\OpenUsd.Rendering.ConformanceTests\OpenUsd.Rendering.ConformanceTests.csproj')
        $testStarted = $true
        & (Join-Path $PSScriptRoot 'run-managed-tests.ps1') `
            -Project $testProject `
            -Framework net10.0 -Configuration $Configuration -MinimumExpectedTests 3 `
            -TestArguments @('--treenode-filter', '/*/*/SilkPrimitiveIdTransportConformanceTests/*')
        if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf))
        {
            throw 'The Windows lavapipe primitive-ID report is missing.'
        }
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        $cases = @($report.groups | Where-Object {
            $_.className -ceq 'SilkPrimitiveIdTransportConformanceTests'
        } | ForEach-Object { $_.tests } | Where-Object {
            $_.methodName -ceq 'SingleIndexedDrawPreservesPrimitiveIdsWithoutProvingPtex' -and
                $_.status -ceq 'passed'
        })
        if ($report.summary.total -ne 3 -or $report.summary.passed -ne 3 -or
            $report.summary.failed -ne 0 -or $report.summary.skipped -ne 0 -or
            $report.summary.cancelled -ne 0 -or $report.summary.timedOut -ne 0 -or
            $cases.Count -ne 3)
        {
            throw 'The three Windows lavapipe primitive-ID cases must pass without skips.'
        }
        foreach ($boundary in @(255, 32767, 65535))
        {
            $name = "SingleIndexedDrawPreservesPrimitiveIdsWithoutProvingPtex(Vulkan, $boundary)"
            if (@($cases | Where-Object { $_.displayName -ceq $name }).Count -ne 1)
            {
                throw "The Windows lavapipe case '$name' did not pass exactly once."
            }
        }
    }
    catch
    {
        $primaryFailure = $_
    }
    finally
    {
        foreach ($reportFile in $reports)
        {
            try
            {
                if ($testStarted -and (Test-Path -LiteralPath $reportFile.Path -PathType Leaf))
                {
                    Move-Item -LiteralPath $reportFile.Path -Destination $reportFile.Current
                    $sourceHash = (Get-FileHash -LiteralPath $reportFile.Current -Algorithm SHA256).Hash
                    $destination = Join-Path $evidence $reportFile.Evidence
                    [IO.File]::Copy($reportFile.Current, $destination, $false)
                    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -cne $sourceHash)
                    {
                        throw "Diagnostic report evidence changed during retention: $destination"
                    }
                }
            }
            catch
            {
                $keepRoot = $true
                $retentionFailures.Add($_.Exception)
            }
            try
            {
                if ($reportFile.PreviousMoved)
                {
                    Move-Item -LiteralPath $reportFile.Previous -Destination $reportFile.Path
                }
            }
            catch
            {
                $keepRoot = $true
                $retentionFailures.Add($_.Exception)
            }
        }
    }
    if ($null -ne $primaryFailure -and $retentionFailures.Count -gt 0)
    {
        $failures = [Collections.Generic.List[Exception]]::new()
        $failures.Add($primaryFailure.Exception)
        $failures.AddRange($retentionFailures)
        throw [AggregateException]::new(
            'The lavapipe diagnostic and report retention both failed.', $failures)
    }
    if ($null -ne $primaryFailure)
    {
        throw $primaryFailure
    }
    if ($retentionFailures.Count -gt 0)
    {
        throw [AggregateException]::new('Lavapipe report retention failed.', $retentionFailures)
    }
}
catch
{
    $diagnosticFailure = $_
}
finally
{
    try
    {
        if ($null -ne $driverRegistration)
        {
            Restore-VulkanTestRuntimeDriver -State $driverRegistration
        }
    }
    catch
    {
        $keepRoot = $true
        $cleanupFailures.Add($_.Exception)
    }
    foreach ($name in $oldEnvironment.Keys)
    {
        try
        {
            [Environment]::SetEnvironmentVariable($name, $oldEnvironment[$name], 'Process')
        }
        catch
        {
            $keepRoot = $true
            $cleanupFailures.Add($_.Exception)
        }
    }
    if (-not $keepRoot)
    {
        try
        {
            Remove-Item -LiteralPath $root -Recurse -Force
        }
        catch
        {
            $keepRoot = $true
            $cleanupFailures.Add($_.Exception)
        }
    }
    if ($keepRoot)
    {
        Write-Warning "Diagnostic scratch was retained at '$root'." -WarningAction Continue
    }
}
if ($null -ne $diagnosticFailure -and $cleanupFailures.Count -gt 0)
{
    $failures = [Collections.Generic.List[Exception]]::new()
    $failures.Add($diagnosticFailure.Exception)
    $failures.AddRange($cleanupFailures)
    throw [AggregateException]::new('The lavapipe diagnostic and cleanup both failed.', $failures)
}
if ($null -ne $diagnosticFailure)
{
    throw $diagnosticFailure
}
if ($cleanupFailures.Count -gt 0)
{
    throw [AggregateException]::new('Lavapipe cleanup failed.', $cleanupFailures)
}
