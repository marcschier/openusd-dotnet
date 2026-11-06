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
$entries = @(
    @{ Id = 'cubic'; Class = 'SilkCubicCurveNativeTests' },
    @{ Id = 'point-shadow'; Class = 'SilkPointShadowNativeConformanceTests' },
    @{ Id = 'camera'; Class = 'VolumeCameraConformanceTests' })
if (-not $IsMacOS)
{
    $entries += @{ Id = 'transmission'; Class = 'SilkShadowTransmissionConformanceTests' }
    $entries += @{ Id = 'finite-area'; Class = 'SilkFiniteAreaShadowTests' }
}
foreach ($entry in $entries)
{
    $started = [DateTime]::UtcNow
    try
    {
        $selection = if ($entry.Id -ceq 'camera')
        {
            @('--treenode-filter', '/*/*/VolumeCameraConformanceTests/CameraIntegrationBackendGate')
        }
        else { @('--treenode-filter', "/*/*/$($entry.Class)/*") }
        $result = Invoke-ManagedWorkflowScenario `
            -Runner $runner -Project $project -AssemblyDirectory $assemblyDirectory `
            -EvidenceRoot $evidence -Entry $entry -Configuration $Configuration `
            -ReportAssembly 'OpenUsd.Rendering.ConformanceTests' `
            -TestArguments $selection `
            -RequireNoSkips ($entry.Id -cne 'camera' -and
                ($entry.Id -cnotin @('transmission', 'finite-area') -or -not $IsLinux))
        $priorReports = @(
            Get-ChildItem -LiteralPath $evidence -Filter "$($entry.Id).prior-*.tunit-report.json" -File |
                ForEach-Object { $_.FullName })
        $checked = Assert-SilkCubicShadowReport `
            -ReportPath (Join-Path $evidence $result.rawReport) `
            -Platform $platform -Class $entry.Class -StartedUtc $started `
            -LogPassedCount $result.passed -ExpectedSha256 $result.rawReportSha256 `
            -PriorReportPaths $priorReports
        $cameraReceipts = @()
        if ($entry.Id -ceq 'camera')
        {
            $volumeRoot = Join-Path (Join-Path $assemblyDirectory 'TestResults') 'volumes'
            $assertCamera = Join-Path $PSScriptRoot 'assert-volume-camera-evidence.ps1'
            & $assertCamera -Root $volumeRoot -Platform $platform -NotBeforeUtc $started
            $backends = switch ($platform) {
                'windows' { @('D3D12', 'Vulkan') }
                'linux' { @('Vulkan') }
                'macos' { @('Metal') }
            }
            $retainedRoot = Join-Path $evidence 'camera-receipts'
            New-Item -ItemType Directory -Path $retainedRoot -ErrorAction Stop | Out-Null
            foreach ($backend in $backends)
            {
                $directory = "camera-$platform-$backend"
                $source = Join-Path (Join-Path $volumeRoot $directory) 'receipt.json'
                $targetDirectory = Join-Path $retainedRoot $directory
                New-Item -ItemType Directory -Path $targetDirectory -ErrorAction Stop | Out-Null
                $target = Join-Path $targetDirectory 'receipt.json'
                $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
                Copy-Item -LiteralPath $source -Destination $target -ErrorAction Stop
                if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -cne $hash -or
                    (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -cne $hash)
                {
                    throw "Camera receipt changed during retention: $backend"
                }
                $cameraReceipts += @{ backend = $backend; path = "camera-receipts/$directory/receipt.json"
                    sha256 = $hash.ToLowerInvariant() }
            }
            & $assertCamera -Root $retainedRoot -Platform $platform -NotBeforeUtc $started
        }
        [ordered]@{
            startedUtc = $started.ToString('O')
            validation = $checked
            execution = $result
            cameraReceipts = $cameraReceipts
        } | ConvertTo-Json -Depth 8 |
            Set-Content -LiteralPath (Join-Path $evidence "$($entry.Id).execution.json")
        Write-Host "[cubic-shadow] $($entry.Class): $($checked.passed) passes; evidence=$evidence"
    }
    catch
    {
        $message = "$($entry.Class): $($_.Exception.Message)"
        $failures.Add($message)
        if ($entry.Id -ceq 'camera')
        {
            try
            {
                Save-SilkCameraFailureEvidence `
                    -VolumeRoot (Join-Path (Join-Path $assemblyDirectory 'TestResults') 'volumes') `
                    -EvidenceRoot $evidence -Platform $platform -PrimaryFailure $message
            }
            catch
            {
                Write-Warning "Camera diagnostic retention failed: $($_.Exception.Message)" -WarningAction Continue
            }
        }
        Write-Warning "[cubic-shadow] $message" -WarningAction Continue
    }
}
if ($failures.Count -ne 0)
{
    throw "Cubic or point-shadow conformance failed: $($failures -join '; ')"
}
