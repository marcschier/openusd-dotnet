#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid = 'win-x64',
    [string]$StagePath = (Join-Path $PSScriptRoot '../test-assets/minimal.usda'),
    [int]$SmokeSeconds = 0,
    [string]$OutputPath,
    [string]$ExpectedStatus = 'Renderer: Storm / OpenGL; frame rendered',
    [string]$ExpectedStatusPattern,
    [switch]$SharedStageSoak,
    [switch]$RendererSwitchSoak,
    [ValidateRange(1, 10000)]
    [int]$SwitchCount = 100,
    [ValidateRange(1, 86400)]
    [int]$SwitchSoakSeconds = 90,
    [ValidateRange(90, 86400)]
    [int]$SoakSeconds = 90,
    [string]$EvidencePath,
    [string]$EvidenceScenario = 'interactive',
    # The evidence branch normally waits for the viewer to exit by itself, which only
    # works for scenarios that self-complete, such as the switching soak, cleanup retry,
    # retired-kind quarantine and stage-camera smokes. A scenario that only has to prove
    # it rendered and wrote evidence never exits on its own, so waiting for that is an
    # unconditional hang. This switch stops the viewer once its evidence is on disk.
    [switch]$StopWhenEvidenceWritten,
    [string]$EvidenceCameraPath,
    [string]$PickSmokeEvidencePath,
    [string]$NativeRuntimeOverridePath,
    [string]$StageAssetRoot,
    [switch]$SimulateStormContextLoss,
    [string]$IdentityManifestPath,
    [switch]$ReusePublishedOutput
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'shared-stage-soak-identity.ps1')
. (Join-Path $PSScriptRoot 'viewer-source-identity.ps1')
$openUsdRoot = Join-Path $repoRoot "native/install/$Rid"
$shimRoot = Join-Path $repoRoot "native/install/shim/$Rid"
$publishRoot = if ([string]::IsNullOrWhiteSpace($OutputPath))
{
    Join-Path $repoRoot "artifacts/viewer/$Rid"
}
else
{
    [System.IO.Path]::GetFullPath($OutputPath)
}
$viewerProject = Join-Path $repoRoot 'src/OpenUsd.Viewer.App/OpenUsd.Viewer.App.csproj'
$sourceIdentityBefore = $null
$binaryIdentityBefore = $null

function Get-ViewerBinaryIdentity
{
    $entries = @(Get-ChildItem $publishRoot -File -Recurse |
        Where-Object {
            $_.Extension -in @('.dll', '.exe', '.so', '.dylib') -or
            $_.Name -eq 'OpenUsd.Viewer.App'
        } |
        Sort-Object FullName |
        ForEach-Object {
            [ordered]@{
                path = [System.IO.Path]::GetRelativePath($publishRoot, $_.FullName)
                sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
                length = $_.Length
                lastWriteTimeUtc = $_.LastWriteTimeUtc.ToString('O')
            }
        })
    [ordered]@{ files = $entries }
}

$sourceIdentityBefore = if ([string]::IsNullOrWhiteSpace($IdentityManifestPath))
{
    $null
}
else
{
    Get-ViewerSourceIdentity -RepoRoot $repoRoot
}

if (-not (Test-Path $openUsdRoot) -or -not (Test-Path $shimRoot))
{
    throw "Build or stage the native runtime for $Rid before running the viewer."
}
$binTarget = Join-Path $publishRoot 'bin'
$libTarget = Join-Path $publishRoot 'lib'
$pluginPath = Join-Path $publishRoot 'plugin/usd'
$stageSource = [System.IO.Path]::GetFullPath($StagePath)
if (-not (Test-Path $stageSource))
{
    throw "Stage file not found: $stageSource"
}

# Real-world USD projects are rarely one file: a root layer references sublayers,
# payloads, and textures by relative path. Staging only the root layer silently
# produces an empty stage, so an explicit asset root is copied alongside it and
# the stage keeps its position relative to that root.
$stageAssetSourceRoot = $null
$stageRelativePath = [System.IO.Path]::GetFileName($stageSource)
if (-not [string]::IsNullOrWhiteSpace($StageAssetRoot))
{
    $stageAssetSourceRoot = [System.IO.Path]::GetFullPath($StageAssetRoot)
    if (-not (Test-Path -LiteralPath $stageAssetSourceRoot -PathType Container))
    {
        throw "Stage asset root not found: $stageAssetSourceRoot"
    }

    $rootPrefix = $stageAssetSourceRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    if (-not $stageSource.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase))
    {
        throw "Stage $stageSource is not inside asset root $stageAssetSourceRoot."
    }
    $stageRelativePath = $stageSource.Substring($rootPrefix.Length)
}
$isStageCameraEvidence =
    $EvidenceScenario -ceq 'stage-camera-backend-smoke'
$hasEvidenceCameraPath =
    -not [string]::IsNullOrWhiteSpace($EvidenceCameraPath)
if ($isStageCameraEvidence -ne $hasEvidenceCameraPath -or
    ($hasEvidenceCameraPath -and -not $EvidenceCameraPath.StartsWith('/')))
{
    throw (
        'The stage-camera evidence scenario and an absolute ' +
        '-EvidenceCameraPath must be supplied together.')
}
$stagedStage = Join-Path $publishRoot $stageRelativePath
if ($ReusePublishedOutput)
{
    foreach ($requiredPath in @(
        $publishRoot,
        $binTarget,
        $libTarget,
        (Join-Path $pluginPath 'plugInfo.json'),
        (Join-Path $pluginPath 'hdStorm/resources/plugInfo.json')))
    {
        if (-not (Test-Path $requiredPath))
        {
            throw "The reusable Viewer output is incomplete: $requiredPath"
        }
    }
}
else
{
    if (Test-Path $publishRoot)
    {
        Remove-Item $publishRoot -Recurse -Force
    }

    & dotnet publish $viewerProject -c Release -f net10.0 -r $Rid -o $publishRoot
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }

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
                Where-Object { $_.Name -match '\.dll$|\.dylib$|\.so($|\.)' } |
                Copy-Item -Destination $layout.Target -Force
        }
    }

    New-Item -ItemType Directory -Force -Path $pluginPath | Out-Null
    $pluginSources = @(
        (Join-Path $openUsdRoot 'lib/usd'),
        (Join-Path $openUsdRoot 'plugin/usd'),
        (Join-Path $shimRoot 'plugin/usd'))
    foreach ($pluginSource in $pluginSources)
    {
        if (Test-Path $pluginSource)
        {
            Copy-Item (Join-Path $pluginSource '*') $pluginPath -Recurse -Force
        }
    }
    foreach ($requiredPluginFile in @(
        (Join-Path $pluginPath 'plugInfo.json'),
        (Join-Path $pluginPath 'hdStorm/resources/plugInfo.json')))
    {
        if (-not (Test-Path $requiredPluginFile))
        {
            throw "The staged plugin tree is incomplete: $requiredPluginFile"
        }
    }
}
if (-not [string]::IsNullOrWhiteSpace($NativeRuntimeOverridePath))
{
    $nativeOverride = [System.IO.Path]::GetFullPath($NativeRuntimeOverridePath)
    if (-not (Test-Path $nativeOverride))
    {
        throw "Viewer native runtime override not found: $nativeOverride"
    }
    Get-ChildItem $nativeOverride -File -Filter '*.dll' |
        Copy-Item -Destination $binTarget -Force
}
if ($null -eq $stageAssetSourceRoot)
{
    Copy-Item $stageSource $stagedStage -Force
}
else
{
    # Copy the whole payload so relative references resolve, then verify the
    # root layer landed at its expected relative location.
    Copy-Item `
        (Join-Path $stageAssetSourceRoot '*') `
        $publishRoot `
        -Recurse `
        -Force
    if (-not (Test-Path -LiteralPath $stagedStage -PathType Leaf))
    {
        throw "Staged asset root did not produce the stage at $stagedStage."
    }
}

$binaryIdentityBefore = Get-ViewerBinaryIdentity
if (-not [string]::IsNullOrWhiteSpace($IdentityManifestPath))
{
    $identityPath = [System.IO.Path]::GetFullPath($IdentityManifestPath)
    New-Item -ItemType Directory -Force `
        -Path ([System.IO.Path]::GetDirectoryName($identityPath)) | Out-Null
    [ordered]@{
        sourceBefore = $sourceIdentityBefore
        binariesBefore = $binaryIdentityBefore
    } | ConvertTo-Json -Depth 8 | Set-Content $identityPath
}

$executable = Join-Path $publishRoot 'OpenUsd.Viewer.App'
if ($IsWindows)
{
    $executable += '.exe'
}

$statusFile = Join-Path $publishRoot 'viewer-status.txt'
$logFile = Join-Path $publishRoot 'viewer.log'
$stdoutFile = Join-Path $publishRoot 'viewer.stdout.log'
$stderrFile = Join-Path $publishRoot 'viewer.stderr.log'
Remove-Item $statusFile, $logFile, $stdoutFile, $stderrFile `
    -Force `
    -ErrorAction SilentlyContinue
$oldPath = $env:PATH
$oldLdLibraryPath = $env:LD_LIBRARY_PATH
$oldDyldLibraryPath = $env:DYLD_LIBRARY_PATH
$oldPluginPath = $env:OPENUSD_PLUGIN_PATH
$oldStagePath = $env:OPENUSD_STAGE_PATH
$oldStatusFile = $env:OPENUSD_STATUS_FILE
$oldLogFile = $env:OPENUSD_LOG_FILE
$oldSharedStageSoak = $env:OPENUSD_SHARED_STAGE_SOAK
$oldSoakSeconds = $env:OPENUSD_SOAK_SECONDS
$oldSoakArtifact = $env:OPENUSD_SOAK_ARTIFACT
$oldSoakSourceHash = $env:OPENUSD_SOAK_SOURCE_HASH
$oldSoakExecutableHash = $env:OPENUSD_SOAK_EXECUTABLE_HASH
$oldSoakExecutableTimestamp = $env:OPENUSD_SOAK_EXECUTABLE_TIMESTAMP_UTC
$oldSwitchSoak = $env:OPENUSD_VIEWER_SWITCH_SOAK
$oldSwitchSoakSeconds = $env:OPENUSD_VIEWER_SWITCH_SOAK_SECONDS
$oldLiveEdit = $env:OPENUSD_VIEWER_LIVE_EDIT
$oldNativeContextLoss = $env:OPENUSD_NATIVE_STORM_CONTEXT_LOSS
$oldRenderer = $env:OPENUSD_RENDERER
$oldPlatform = $env:OPENUSD_VIEWER_PLATFORM
$oldEvidencePath = $env:OPENUSD_VIEWER_EVIDENCE_PATH
$oldEvidenceScenario = $env:OPENUSD_VIEWER_EVIDENCE_SCENARIO
$oldStageCameraPath = $env:OPENUSD_VIEWER_STAGE_CAMERA_PATH
$oldPickSmokePath = $env:OPENUSD_VIEWER_PICK_SMOKE_PATH
$process = $null

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

try
{
    $env:PATH = $binTarget + [System.IO.Path]::PathSeparator +
        $libTarget + [System.IO.Path]::PathSeparator +
        $publishRoot + [System.IO.Path]::PathSeparator + $oldPath
    $env:LD_LIBRARY_PATH = $libTarget + [System.IO.Path]::PathSeparator +
        $binTarget + [System.IO.Path]::PathSeparator + $oldLdLibraryPath
    $env:DYLD_LIBRARY_PATH = $libTarget + [System.IO.Path]::PathSeparator +
        $binTarget + [System.IO.Path]::PathSeparator + $oldDyldLibraryPath
    $env:OPENUSD_PLUGIN_PATH = $pluginPath
    $env:OPENUSD_STAGE_PATH = $stagedStage
    $env:OPENUSD_STATUS_FILE = $statusFile
    $env:OPENUSD_LOG_FILE = $logFile
    $env:OPENUSD_VIEWER_PLATFORM = $oldPlatform
    $env:OPENUSD_VIEWER_EVIDENCE_PATH = if (
        [string]::IsNullOrWhiteSpace($EvidencePath))
    {
        $null
    }
    else
    {
        [System.IO.Path]::GetFullPath($EvidencePath)
    }
    $env:OPENUSD_VIEWER_EVIDENCE_SCENARIO = $EvidenceScenario
    $env:OPENUSD_VIEWER_STAGE_CAMERA_PATH = if ($hasEvidenceCameraPath)
    {
        $EvidenceCameraPath
    }
    else
    {
        $null
    }
    $env:OPENUSD_VIEWER_PICK_SMOKE_PATH = if (
        [string]::IsNullOrWhiteSpace($PickSmokeEvidencePath))
    {
        $null
    }
    else
    {
        [System.IO.Path]::GetFullPath($PickSmokeEvidencePath)
    }
    if ($SharedStageSoak)
    {
        $env:OPENUSD_SHARED_STAGE_SOAK = '1'
        $env:OPENUSD_SOAK_SECONDS = $SoakSeconds.ToString(
            [System.Globalization.CultureInfo]::InvariantCulture)
        $env:OPENUSD_SOAK_ARTIFACT = Join-Path $publishRoot 'shared-stage-soak.json'
        Set-OpenUsdSoakIdentityEnvironment $repoRoot $executable

        $process = Start-Process $executable -PassThru `
            -ArgumentList @('--shared-stage-soak', '--soak-seconds', $SoakSeconds,
                '--soak-artifact', $env:OPENUSD_SOAK_ARTIFACT) `
            -RedirectStandardOutput $stdoutFile `
            -RedirectStandardError $stderrFile
        $deadlineSeconds = if ($SmokeSeconds -gt 0)
        {
            $SmokeSeconds
        }
        else
        {
            $SoakSeconds + 300
        }
        $deadline = [DateTime]::UtcNow.AddSeconds($deadlineSeconds)
        $passedStatus = $null
        while ([DateTime]::UtcNow -lt $deadline)
        {
            $process.Refresh()
            if (Test-Path $statusFile)
            {
                $statuses = @(Get-Content $statusFile)
                $passedStatus = $statuses |
                    Where-Object { $_ -match '^Shared-stage soak passed:' } |
                    Select-Object -Last 1
                $failure = $statuses |
                    Where-Object { $_ -match '^Shared-stage soak failed:' } |
                    Select-Object -Last 1
                if ($null -ne $failure)
                {
                    throw "Viewer shared-stage soak failed: $failure"
                }
            }
            if ($process.HasExited)
            {
                break
            }
            Start-Sleep -Milliseconds 250
        }
        $process.Refresh()
        if (-not $process.HasExited)
        {
            throw "Viewer shared-stage soak exceeded $deadlineSeconds seconds."
        }
        if ($process.ExitCode -ne 0 -or $null -eq $passedStatus)
        {
            throw "Viewer shared-stage soak exited with code $($process.ExitCode) without a pass status."
        }
        if (-not (Test-Path $env:OPENUSD_SOAK_ARTIFACT))
        {
            throw "Viewer shared-stage soak did not write $env:OPENUSD_SOAK_ARTIFACT."
        }
        Assert-OpenUsdSoakArtifact $env:OPENUSD_SOAK_ARTIFACT $true
        Write-Output $passedStatus
        exit 0
    }

    if ($RendererSwitchSoak)
    {
        $env:OPENUSD_VIEWER_SWITCH_SOAK =
            $SwitchCount.ToString([System.Globalization.CultureInfo]::InvariantCulture)
        $env:OPENUSD_VIEWER_SWITCH_SOAK_SECONDS =
            $SwitchSoakSeconds.ToString([System.Globalization.CultureInfo]::InvariantCulture)
        $env:OPENUSD_VIEWER_LIVE_EDIT = '1'
        $env:OPENUSD_NATIVE_STORM_CONTEXT_LOSS =
            if ($SimulateStormContextLoss) { '1' } else { $null }
        $env:OPENUSD_RENDERER = 'Storm'
        $switchArguments = @(
            '--renderer', 'Storm',
            '--renderer-switch-soak', $SwitchCount,
            '--switch-soak-seconds', $SwitchSoakSeconds)
        if ($IsWindows)
        {
            $switchArguments = @('--windows-rendering=angle') + $switchArguments
        }
        $process = Start-Process $executable -PassThru `
            -ArgumentList $switchArguments `
            -RedirectStandardOutput $stdoutFile `
            -RedirectStandardError $stderrFile
        $deadlineSeconds = [Math]::Max(
            $SwitchSoakSeconds + 600,
            $SwitchCount * 30 + 600)
        if (-not $process.WaitForExit($deadlineSeconds * 1000))
        {
            throw "Viewer renderer switch soak exceeded $deadlineSeconds seconds."
        }
        $statuses = if (Test-Path $statusFile) { @(Get-Content $statusFile) } else { @() }
        $passed = $statuses |
            Where-Object { $_ -match "^Viewer switch soak passed: switches=$SwitchCount;" } |
            Select-Object -Last 1
        $expectedAbandoned = if ($SimulateStormContextLoss) { 1 } else { 0 }
        $resources = $statuses |
            Where-Object {
                $_ -match ("^Viewer final resources: child=0;.*managedStorm=0; " +
                    "nativeStorm=0;.*managedSilk=0; nativeSilk=0;.*managedPages=0; " +
                    "nativePages=0;.*gpuScenes=0; gpuMeshes=0; " +
                    "abandonedStorm=$expectedAbandoned$")
            } |
            Select-Object -Last 1
        $failure = $statuses |
            Where-Object {
                $_ -match '^(Renderer frame failed|Viewer diagnostic sequence failed|Viewer renderer shutdown failed)'
            } |
            Select-Object -Last 1
        if ($process.ExitCode -ne 0 -or
            $null -eq $passed -or
            $null -eq $resources -or
            $null -ne $failure)
        {
            throw "Viewer renderer switch soak failed with code $($process.ExitCode)."
        }
        if (-not [string]::IsNullOrWhiteSpace($EvidencePath) -and
            -not (Test-Path $EvidencePath))
        {
            throw "Viewer renderer switch soak did not write evidence: $EvidencePath"
        }
        Write-Output $passed
        Write-Output $resources
        exit 0
    }

    if (-not [string]::IsNullOrWhiteSpace($PickSmokeEvidencePath))
    {
        $process = Start-Process $executable -PassThru `
            -ArgumentList @('--windows-rendering=angle') `
            -RedirectStandardOutput $stdoutFile `
            -RedirectStandardError $stderrFile
        $deadlineSeconds = if ($SmokeSeconds -gt 0) { $SmokeSeconds } else { 180 }
        if (-not $process.WaitForExit($deadlineSeconds * 1000))
        {
            throw "Viewer picking smoke exceeded $deadlineSeconds seconds."
        }
        $pickArtifact = [System.IO.Path]::GetFullPath($PickSmokeEvidencePath)
        $statuses = if (Test-Path $statusFile) { @(Get-Content $statusFile) } else { @() }
        $passed = $statuses |
            Where-Object { $_ -match '^Viewer picking short smoke passed:' } |
            Select-Object -Last 1
        $failure = $statuses |
            Where-Object {
                $_ -match '^(Renderer initialization failed|Renderer frame failed|' +
                    'Viewer diagnostic sequence failed|Viewer renderer shutdown failed)'
            } |
            Select-Object -Last 1
        if ($process.ExitCode -ne 0 -or
            $null -ne $failure -or
            $null -eq $passed -or
            -not (Test-Path $pickArtifact))
        {
            throw "Viewer picking smoke failed with code $($process.ExitCode)."
        }
        Write-Output $passed
        exit 0
    }

    if (-not [string]::IsNullOrWhiteSpace($EvidencePath))
    {
        $evidenceArguments = if ($IsWindows)
        {
            @('--windows-rendering=angle')
        }
        else
        {
            @()
        }
        $process = Start-Process $executable -PassThru `
            -ArgumentList $evidenceArguments `
            -RedirectStandardOutput $stdoutFile `
            -RedirectStandardError $stderrFile
        $deadlineSeconds = if ($SmokeSeconds -gt 0) { $SmokeSeconds } else { 600 }
        $stoppedAfterEvidence = $false
        if ($StopWhenEvidenceWritten)
        {
            $deadline = [DateTime]::UtcNow.AddSeconds($deadlineSeconds)
            while ([DateTime]::UtcNow -lt $deadline)
            {
                $process.Refresh()
                if ($process.HasExited)
                {
                    break
                }
                if (Test-Path $EvidencePath)
                {
                    # Only once it parses, so a partially flushed file is never mistaken
                    # for completed evidence.
                    try
                    {
                        Get-Content $EvidencePath -Raw | ConvertFrom-Json | Out-Null
                        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
                        $process.WaitForExit()
                        $stoppedAfterEvidence = $true
                        break
                    }
                    catch
                    {
                        # Still being written.
                    }
                }
                Start-Sleep -Milliseconds 250
            }
            $process.Refresh()
            if (-not $process.HasExited)
            {
                throw "Viewer evidence scenario exceeded $deadlineSeconds seconds."
            }
        }
        elseif (-not $process.WaitForExit($deadlineSeconds * 1000))
        {
            throw "Viewer evidence scenario exceeded $deadlineSeconds seconds."
        }
        $statuses = if (Test-Path $statusFile) { @(Get-Content $statusFile) } else { @() }
        $failure = $statuses |
            Where-Object {
                $_ -match '^(Renderer initialization failed|Renderer frame failed|' +
                    'Viewer diagnostic sequence failed|Viewer renderer shutdown failed)'
            } |
            Select-Object -Last 1
        if (($process.ExitCode -ne 0 -and -not $stoppedAfterEvidence) -or
            $null -ne $failure -or
            -not (Test-Path $EvidencePath))
        {
            throw "Viewer evidence scenario '$EvidenceScenario' failed with code $($process.ExitCode)."
        }
        Write-Output "Viewer evidence scenario passed: $EvidenceScenario"
        exit 0
    }

    if ($SmokeSeconds -le 0)
    {
        & $executable
        exit $LASTEXITCODE
    }

    $process = Start-Process $executable -PassThru `
        -ArgumentList @('--windows-rendering=angle') `
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
                Where-Object {
                    if ([string]::IsNullOrWhiteSpace($ExpectedStatusPattern))
                    {
                        $_ -eq $ExpectedStatus
                    }
                    else
                    {
                        $_ -match $ExpectedStatusPattern
                    }
                } |
                Select-Object -Last 1
            if ($null -ne $renderedStatus)
            {
                break
            }
            $rendererFailure = $statuses |
                Where-Object { $_ -match '^Renderer (failed|unavailable|lost)' } |
                Select-Object -Last 1
            if ($null -ne $rendererFailure)
            {
                throw "Viewer reported a renderer failure: $rendererFailure"
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
        $expectation = if ([string]::IsNullOrWhiteSpace($ExpectedStatusPattern))
        {
            $ExpectedStatus
        }
        else
        {
            "pattern $ExpectedStatusPattern"
        }
        throw "Viewer did not report '$expectation' within $SmokeSeconds seconds."
    }
    if ($process.HasExited)
    {
        throw "Viewer exited after reporting a frame with code $($process.ExitCode)."
    }

    Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
    $process.WaitForExit()
    Write-Output $renderedStatus
}
catch
{
    Write-ViewerDiagnostics
    throw
}
finally
{
    if ($process -is [System.Diagnostics.Process] -and -not $process.HasExited)
    {
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
        $process.WaitForExit()
    }
    $env:PATH = $oldPath
    $env:LD_LIBRARY_PATH = $oldLdLibraryPath
    $env:DYLD_LIBRARY_PATH = $oldDyldLibraryPath
    $env:OPENUSD_PLUGIN_PATH = $oldPluginPath
    $env:OPENUSD_STAGE_PATH = $oldStagePath
    $env:OPENUSD_STATUS_FILE = $oldStatusFile
    $env:OPENUSD_LOG_FILE = $oldLogFile
    $env:OPENUSD_SHARED_STAGE_SOAK = $oldSharedStageSoak
    $env:OPENUSD_SOAK_SECONDS = $oldSoakSeconds
    $env:OPENUSD_SOAK_ARTIFACT = $oldSoakArtifact
    $env:OPENUSD_SOAK_SOURCE_HASH = $oldSoakSourceHash
    $env:OPENUSD_SOAK_EXECUTABLE_HASH = $oldSoakExecutableHash
    $env:OPENUSD_SOAK_EXECUTABLE_TIMESTAMP_UTC = $oldSoakExecutableTimestamp
    $env:OPENUSD_VIEWER_SWITCH_SOAK = $oldSwitchSoak
    $env:OPENUSD_VIEWER_SWITCH_SOAK_SECONDS = $oldSwitchSoakSeconds
    $env:OPENUSD_VIEWER_LIVE_EDIT = $oldLiveEdit
    $env:OPENUSD_NATIVE_STORM_CONTEXT_LOSS = $oldNativeContextLoss
    $env:OPENUSD_RENDERER = $oldRenderer
    $env:OPENUSD_VIEWER_PLATFORM = $oldPlatform
    $env:OPENUSD_VIEWER_EVIDENCE_PATH = $oldEvidencePath
    $env:OPENUSD_VIEWER_EVIDENCE_SCENARIO = $oldEvidenceScenario
    $env:OPENUSD_VIEWER_STAGE_CAMERA_PATH = $oldStageCameraPath
    $env:OPENUSD_VIEWER_PICK_SMOKE_PATH = $oldPickSmokePath
    if (-not [string]::IsNullOrWhiteSpace($IdentityManifestPath) -and
        $null -ne $binaryIdentityBefore)
    {
        $sourceIdentityAfter = Get-ViewerSourceIdentity -RepoRoot $repoRoot
        $binaryIdentityAfter = Get-ViewerBinaryIdentity
        $sourceUnchanged =
            $sourceIdentityBefore.sha256 -eq $sourceIdentityAfter.sha256
        $binaryUnchanged =
            ($binaryIdentityBefore | ConvertTo-Json -Depth 8 -Compress) -eq
            ($binaryIdentityAfter | ConvertTo-Json -Depth 8 -Compress)
        $identityPath = [System.IO.Path]::GetFullPath($IdentityManifestPath)
        [ordered]@{
            sourceBefore = $sourceIdentityBefore
            sourceAfter = $sourceIdentityAfter
            sourceUnchanged = $sourceUnchanged
            binariesBefore = $binaryIdentityBefore
            binariesAfter = $binaryIdentityAfter
            binariesUnchanged = $binaryUnchanged
        } | ConvertTo-Json -Depth 8 | Set-Content $identityPath
        if (-not $sourceUnchanged -or -not $binaryUnchanged)
        {
            throw "Viewer source or binary identity changed during the run."
        }
    }
}
