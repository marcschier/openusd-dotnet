#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [string]$LoaderPath = "$env:SystemRoot\System32\vulkan-1.dll",
    [Parameter(Mandatory = $true)]
    [string]$IcdPath,
    [switch]$Required,
    [ValidateRange(15, 600)]
    [int]$TimeoutSeconds = 90,
    [ValidateRange(1, 20)]
    [int]$Repetitions = 1,
    [switch]$AotProbe,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows)
{
    throw 'Use run-avalonia-vulkan-smoke-linux.sh on Linux.'
}
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'avalonia-vulkan-smoke-identity.ps1')
$publishRoot = if ([string]::IsNullOrWhiteSpace($OutputPath))
{
    $suffix = if ($AotProbe) { 'win-x64-aot' } else { 'win-x64' }
    Join-Path $repoRoot "artifacts/avalonia-vulkan-smoke/$suffix"
}
else
{
    [System.IO.Path]::GetFullPath($OutputPath)
}
& (Join-Path $PSScriptRoot 'publish-avalonia-vulkan-smoke.ps1') `
    -Rid win-x64 -OutputPath $publishRoot -AotProbe:$AotProbe
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}
$buildIdentityPath = Join-Path $publishRoot 'build-identity.json'
if (-not (Test-Path $buildIdentityPath))
{
    throw "Vulkan smoke build identity was not produced: $buildIdentityPath"
}
$buildIdentity = Get-Content $buildIdentityPath -Raw | ConvertFrom-Json
foreach ($field in @(
    'latestSourceWriteUtc',
    'executableLastWriteUtc',
    'buildStartedUtc',
    'buildCompletedUtc'))
{
    $buildIdentity.$field =
        ([DateTimeOffset]$buildIdentity.$field).ToUniversalTime().ToString('O')
}

$loader = [System.IO.Path]::GetFullPath($LoaderPath)
$icd = [System.IO.Path]::GetFullPath($IcdPath)
if (-not (Test-Path $loader))
{
    throw "Vulkan loader not found: $loader"
}
if (-not (Test-Path $icd))
{
    throw "Vulkan ICD manifest not found: $icd"
}

$executable = Join-Path $publishRoot 'OpenUsd.AvaloniaVulkanSmoke.exe'
$artifact = Join-Path $publishRoot 'windows-smoke.json'
$nativeLibrary = Join-Path $publishRoot 'bin/openusd_hdsilk.dll'
if (-not $AotProbe -and -not (Test-Path $nativeLibrary))
{
    $message = "hdSilk runtime unavailable: $nativeLibrary"
    @{ outcome = 'unavailable'; platform = 'windows'; blocker = $message } |
        ConvertTo-Json | Set-Content $artifact
    Write-Error $message -ErrorAction Continue
    exit $(if ($Required) { 1 } else { 0 })
}

$old = @{
    PATH = $env:PATH
    VK_DRIVER_FILES = $env:VK_DRIVER_FILES
    VK_ICD_FILENAMES = $env:VK_ICD_FILENAMES
    OPENUSD_PLUGIN_PATH = $env:OPENUSD_PLUGIN_PATH
    OPENUSD_STAGE_PATH = $env:OPENUSD_STAGE_PATH
    OPENUSD_REQUIRE_VULKAN_PRESENTATION = $env:OPENUSD_REQUIRE_VULKAN_PRESENTATION
    OPENUSD_AVALONIA_VULKAN_PLATFORM = $env:OPENUSD_AVALONIA_VULKAN_PLATFORM
    OPENUSD_AVALONIA_VULKAN_ARTIFACT = $env:OPENUSD_AVALONIA_VULKAN_ARTIFACT
    OPENUSD_AVALONIA_VULKAN_TIMEOUT_SECONDS =
        $env:OPENUSD_AVALONIA_VULKAN_TIMEOUT_SECONDS
    OPENUSD_AVALONIA_VULKAN_CAPABILITY_ONLY =
        $env:OPENUSD_AVALONIA_VULKAN_CAPABILITY_ONLY
    OPENUSD_AVALONIA_VULKAN_SOURCE_SHA256 =
        $env:OPENUSD_AVALONIA_VULKAN_SOURCE_SHA256
    OPENUSD_AVALONIA_VULKAN_SOURCE_FILE_COUNT =
        $env:OPENUSD_AVALONIA_VULKAN_SOURCE_FILE_COUNT
    OPENUSD_AVALONIA_VULKAN_SOURCE_LATEST_WRITE_UTC =
        $env:OPENUSD_AVALONIA_VULKAN_SOURCE_LATEST_WRITE_UTC
    OPENUSD_AVALONIA_VULKAN_EXECUTABLE_SHA256 =
        $env:OPENUSD_AVALONIA_VULKAN_EXECUTABLE_SHA256
    OPENUSD_AVALONIA_VULKAN_EXECUTABLE_LENGTH =
        $env:OPENUSD_AVALONIA_VULKAN_EXECUTABLE_LENGTH
    OPENUSD_AVALONIA_VULKAN_EXECUTABLE_LAST_WRITE_UTC =
        $env:OPENUSD_AVALONIA_VULKAN_EXECUTABLE_LAST_WRITE_UTC
    OPENUSD_AVALONIA_VULKAN_BUILD_COMPLETED_UTC =
        $env:OPENUSD_AVALONIA_VULKAN_BUILD_COMPLETED_UTC
    OPENUSD_AVALONIA_VULKAN_RUN_STARTED_UTC =
        $env:OPENUSD_AVALONIA_VULKAN_RUN_STARTED_UTC
}
$process = $null
$exitCode = 1
try
{
    $env:PATH = (Split-Path $loader) + [IO.Path]::PathSeparator +
        (Join-Path $publishRoot 'bin') + [IO.Path]::PathSeparator +
        (Join-Path $publishRoot 'lib') + [IO.Path]::PathSeparator + $old.PATH
    $env:VK_DRIVER_FILES = $icd
    $env:VK_ICD_FILENAMES = $icd
    $env:OPENUSD_PLUGIN_PATH = Join-Path $publishRoot 'plugin/usd'
    $env:OPENUSD_STAGE_PATH = Join-Path $publishRoot 'avalonia-vulkan-smoke.usda'
    $env:OPENUSD_REQUIRE_VULKAN_PRESENTATION = if ($Required) { '1' } else { '0' }
    $env:OPENUSD_AVALONIA_VULKAN_PLATFORM = 'windows'
    $env:OPENUSD_AVALONIA_VULKAN_TIMEOUT_SECONDS = $TimeoutSeconds
    $env:OPENUSD_AVALONIA_VULKAN_CAPABILITY_ONLY = if ($AotProbe) { '1' } else { '0' }
    $env:OPENUSD_AVALONIA_VULKAN_SOURCE_SHA256 = $buildIdentity.sourceSha256
    $env:OPENUSD_AVALONIA_VULKAN_SOURCE_FILE_COUNT = $buildIdentity.sourceFileCount
    $env:OPENUSD_AVALONIA_VULKAN_SOURCE_LATEST_WRITE_UTC =
        ([DateTimeOffset]$buildIdentity.latestSourceWriteUtc).ToUniversalTime().ToString('O')
    $env:OPENUSD_AVALONIA_VULKAN_EXECUTABLE_SHA256 =
        $buildIdentity.executableSha256
    $env:OPENUSD_AVALONIA_VULKAN_EXECUTABLE_LENGTH =
        $buildIdentity.executableLength
    $env:OPENUSD_AVALONIA_VULKAN_EXECUTABLE_LAST_WRITE_UTC =
        ([DateTimeOffset]$buildIdentity.executableLastWriteUtc).ToUniversalTime().ToString('O')
    $env:OPENUSD_AVALONIA_VULKAN_BUILD_COMPLETED_UTC =
        ([DateTimeOffset]$buildIdentity.buildCompletedUtc).ToUniversalTime().ToString('O')

    $results = @()
    $identityExecutable = Join-Path $publishRoot $buildIdentity.executableFile
    for ($run = 1; $run -le $Repetitions; $run++)
    {
        $currentSource = Get-AvaloniaVulkanSmokeSourceIdentity -RepoRoot $repoRoot
        $currentExecutable = Get-AvaloniaVulkanSmokeExecutableIdentity `
            -ExecutablePath $identityExecutable
        Assert-AvaloniaVulkanSmokeIdentity `
            -Expected $buildIdentity `
            -Source $currentSource `
            -Executable $currentExecutable
        $suffix = if ($Repetitions -eq 1) { '' } else { '-{0:D2}' -f $run }
        $runArtifact = Join-Path $publishRoot "windows-smoke$suffix.json"
        $stdout = Join-Path $publishRoot "windows$suffix.stdout.log"
        $stderr = Join-Path $publishRoot "windows$suffix.stderr.log"
        Remove-Item $runArtifact, $stdout, $stderr -Force -ErrorAction SilentlyContinue
        $env:OPENUSD_AVALONIA_VULKAN_ARTIFACT = $runArtifact
        $env:OPENUSD_AVALONIA_VULKAN_RUN_STARTED_UTC =
            [DateTimeOffset]::UtcNow.ToString('O')

        $process = Start-Process $executable -PassThru `
            -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        if (-not $process.WaitForExit($TimeoutSeconds * 1000))
        {
            Stop-Process -Id $process.Id
            throw "Avalonia Vulkan smoke run $run timed out after $TimeoutSeconds seconds."
        }
        Get-Content $stdout -ErrorAction SilentlyContinue | Write-Host
        Get-Content $stderr -ErrorAction SilentlyContinue | Write-Host
        if (-not (Test-Path $runArtifact))
        {
            throw "Smoke run $run exited without artifact (exit $($process.ExitCode))."
        }
        Get-Content $runArtifact | Write-Host
        $result = Get-Content $runArtifact -Raw | ConvertFrom-Json
        foreach ($identityField in @(
            'sourceSha256',
            'sourceFileCount',
            'latestSourceWriteUtc',
            'executableSha256',
            'executableLength',
            'executableLastWriteUtc',
            'buildCompletedUtc'))
        {
            $identityMatches = if ($identityField.EndsWith('Utc'))
            {
                ([DateTimeOffset]$result.identity.$identityField).ToUniversalTime().Ticks -eq
                    ([DateTimeOffset]$buildIdentity.$identityField).ToUniversalTime().Ticks
            }
            else
            {
                [string]$result.identity.$identityField -ceq
                    [string]$buildIdentity.$identityField
            }
            if (-not $identityMatches)
            {
                throw "Smoke run $run identity mismatch for $identityField."
            }
        }
        $runStarted = [DateTimeOffset]$result.identity.runStartedUtc
        $artifactWritten = [DateTimeOffset]$result.identity.artifactWrittenUtc
        $executableWritten = [DateTimeOffset]$buildIdentity.executableLastWriteUtc
        if ($runStarted -lt $executableWritten -or $artifactWritten -lt $runStarted)
        {
            throw "Smoke run $run produced stale timestamp evidence."
        }
        $results += [pscustomobject]@{
            run = $run
            exitCode = $process.ExitCode
            outcome = $result.outcome
            handleType = $result.handleType
            presentationPath = $result.presentationPath
            frameCount = $result.frameCount
            ringReuseCycles = $result.ringReuseCycles
            liveEditObserved = $result.liveEditObserved
            resizeObserved = $result.resizeObserved
            maxDrawCount = $result.maxDrawCount
            activeGenerations = $result.diagnostics.ActiveGenerations
            activeFrames = $result.diagnostics.ActiveFrames
            initialPixelSha256 = $result.pixelEvidence.initial.sha256
            editedPixelSha256 = $result.pixelEvidence.edited.sha256
            initialNonBackgroundPixels =
                $result.pixelEvidence.initial.nonBackgroundPixels
            editedNonBackgroundPixels =
                $result.pixelEvidence.edited.nonBackgroundPixels
            changedPixels = $result.pixelEvidence.changedPixels
            meanAbsoluteChannelDelta =
                $result.pixelEvidence.meanAbsoluteChannelDelta
            artifactWrittenUtc = $artifactWritten.ToUniversalTime().ToString('O')
        }
        $process = $null
    }

    $passed = @($results | Where-Object {
        $common = $_.exitCode -eq 0 -and
            $_.handleType -eq 'D3D11TextureNtHandle' -and
            $_.ringReuseCycles -ge 2 -and
            $_.resizeObserved -and
            $_.activeGenerations -eq 0 -and
            $_.activeFrames -eq 0
        if ($AotProbe)
        {
            $common -and $_.outcome -eq 'capability-passed'
        }
        else
        {
            $common -and
                $_.outcome -eq 'passed' -and
                $_.liveEditObserved -and
                $_.maxDrawCount -gt 0 -and
                $_.initialPixelSha256.Length -eq 64 -and
                $_.editedPixelSha256.Length -eq 64 -and
                $_.initialPixelSha256 -cne $_.editedPixelSha256 -and
                $_.initialNonBackgroundPixels -gt 0 -and
                $_.editedNonBackgroundPixels -gt 0 -and
                $_.changedPixels -gt 0 -and
                $_.meanAbsoluteChannelDelta -gt 0
        }
    }).Count
    $finalSource = Get-AvaloniaVulkanSmokeSourceIdentity -RepoRoot $repoRoot
    $finalExecutable = Get-AvaloniaVulkanSmokeExecutableIdentity `
        -ExecutablePath $identityExecutable
    Assert-AvaloniaVulkanSmokeIdentity `
        -Expected $buildIdentity `
        -Source $finalSource `
        -Executable $finalExecutable
    $summary = [ordered]@{
        outcome = if ($passed -eq $Repetitions) { 'passed' } else { 'failed' }
        platform = 'windows'
        repetitions = $Repetitions
        passed = $passed
        nativeAot = [bool]$AotProbe
        loader = $loader
        icd = $icd
        sourceSha256 = $buildIdentity.sourceSha256
        sourceFileCount = $buildIdentity.sourceFileCount
        latestSourceWriteUtc = $buildIdentity.latestSourceWriteUtc
        executableSha256 = $buildIdentity.executableSha256
        executableFile = $buildIdentity.executableFile
        executableLength = $buildIdentity.executableLength
        executableLastWriteUtc = $buildIdentity.executableLastWriteUtc
        buildCompletedUtc = $buildIdentity.buildCompletedUtc
        results = $results
    }
    $summaryPath = Join-Path $publishRoot 'windows-smoke-summary.json'
    $summary | ConvertTo-Json -Depth 6 | Set-Content $summaryPath
    Get-Content $summaryPath | Write-Host
    $exitCode = if ($passed -eq $Repetitions) { 0 } else { 1 }
}
finally
{
    if ($process -and -not $process.HasExited)
    {
        Stop-Process -Id $process.Id
    }
    foreach ($name in $old.Keys)
    {
        Set-Item "Env:$name" $old[$name]
    }
}
exit $exitCode
