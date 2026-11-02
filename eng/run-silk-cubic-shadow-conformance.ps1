#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'managed-workflow-reports.ps1')
. (Join-Path $PSScriptRoot 'silk-cubic-shadow-report.ps1')

$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$platform = if ($IsWindows) { 'windows' } elseif ($IsLinux) { 'linux' } elseif ($IsMacOS) { 'macos' }
else { throw 'Cubic and point-shadow conformance requires Windows, Linux, or macOS.' }
$platformRoot = Join-Path $repo "artifacts/silk-cubic-shadow-conformance/$platform"
New-Item -ItemType Directory -Path $platformRoot -Force -ErrorAction Stop | Out-Null
$evidence = Join-Path $platformRoot ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence -ErrorAction Stop | Out-Null

if ($env:OPENUSD_MESH_PREPARATION_REQUIRED -cne '1' -or
    $env:OPENUSD_DIRECT_LIGHT_EXECUTION_REQUIRED -cne '1' -or
    [string]::IsNullOrWhiteSpace($env:OPENUSD_TEST_PLUGIN_PATH) -or
    -not (Test-Path -LiteralPath $env:OPENUSD_TEST_PLUGIN_PATH -PathType Container))
{
    throw 'The required native plugin, mesh preparation, and direct-light execution settings are missing.'
}
if ($IsWindows -and $env:OPENUSD_REQUIRE_SWIFTSHADER -cne '1')
{
    throw 'Windows conformance requires the packaged SwiftShader Vulkan runtime.'
}
if ($IsLinux -and
    ([string]::IsNullOrWhiteSpace($env:VK_DRIVER_FILES) -or
     $env:VK_DRIVER_FILES -cne $env:VK_ICD_FILENAMES -or
     -not (Test-Path -LiteralPath $env:VK_DRIVER_FILES -PathType Leaf)))
{
    throw 'Linux conformance requires the selected lavapipe ICD in both Vulkan loader variables.'
}

$project = Join-Path $repo 'tests/OpenUsd.Rendering.ConformanceTests/OpenUsd.Rendering.ConformanceTests.csproj'
$assemblyDirectory = Join-Path $repo "tests/OpenUsd.Rendering.ConformanceTests/bin/$Configuration/net10.0"
$runner = Join-Path $repo 'eng/run-managed-tests.ps1'
$failures = [Collections.Generic.List[string]]::new()
foreach ($entry in @(
    @{ Id = 'cubic'; Class = 'SilkCubicCurveNativeTests' },
    @{ Id = 'point-shadow'; Class = 'SilkPointShadowNativeConformanceTests' }))
{
    $started = [DateTime]::UtcNow
    try
    {
        $result = Invoke-ManagedWorkflowScenario `
            -Runner $runner -Project $project -AssemblyDirectory $assemblyDirectory `
            -EvidenceRoot $evidence -Entry $entry -Configuration $Configuration `
            -ReportAssembly 'OpenUsd.Rendering.ConformanceTests' -RequireNoSkips $true
        $priorReports = @(
            Get-ChildItem -LiteralPath $evidence -Filter "$($entry.Id).prior-*.tunit-report.json" -File |
                ForEach-Object { $_.FullName })
        $checked = Assert-SilkCubicShadowReport `
            -ReportPath (Join-Path $evidence $result.rawReport) `
            -Platform $platform -Class $entry.Class -StartedUtc $started `
            -LogPassedCount $result.passed -ExpectedSha256 $result.rawReportSha256 `
            -PriorReportPaths $priorReports
        [ordered]@{
            startedUtc = $started.ToString('O')
            validation = $checked
            execution = $result
        } | ConvertTo-Json -Depth 8 |
            Set-Content -LiteralPath (Join-Path $evidence "$($entry.Id).execution.json")
        Write-Host "[cubic-shadow] $($entry.Class): $($checked.passed) passes; evidence=$evidence"
    }
    catch
    {
        $message = "$($entry.Class): $($_.Exception.Message)"
        $failures.Add($message)
        Write-Warning "[cubic-shadow] $message" -WarningAction Continue
    }
}
if ($failures.Count -ne 0)
{
    throw "Cubic or point-shadow conformance failed: $($failures -join '; ')"
}
