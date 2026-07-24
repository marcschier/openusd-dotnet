#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'viewer-evidence-contract.ps1')

function Write-TestBitmap
{
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [int]$Width,
        [Parameter(Mandatory)]
        [int]$Height,
        [Parameter(Mandatory)]
        [byte[]]$Pixels)

    $offset = 54
    $stream = [IO.File]::Create($Path)
    try
    {
        $writer = [IO.BinaryWriter]::new($stream)
        try
        {
            $writer.Write([uint16]0x4D42)
            $writer.Write([int]($offset + $Pixels.Length))
            $writer.Write([int]0)
            $writer.Write([int]$offset)
            $writer.Write([int]40)
            $writer.Write($Width)
            $writer.Write(-$Height)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([int]0)
            $writer.Write($Pixels.Length)
            $writer.Write([int]0)
            $writer.Write([int]0)
            $writer.Write([int]0)
            $writer.Write([int]0)
            $writer.Write($Pixels)
        }
        finally
        {
            $writer.Dispose()
        }
    }
    finally
    {
        $stream.Dispose()
    }
}

function Get-StormRecordedBytes
{
    param(
        [Parameter(Mandatory)]
        [byte[]]$Bgra,
        [Parameter(Mandatory)]
        [int]$Width,
        [Parameter(Mandatory)]
        [int]$Height)

    $rgba = [byte[]]::new($Bgra.Length)
    $rowBytes = $Width * 4
    for ($y = 0; $y -lt $Height; $y++)
    {
        for ($x = 0; $x -lt $Width; $x++)
        {
            $source = ($y * $rowBytes) + ($x * 4)
            $destination = (($Height - 1 - $y) * $rowBytes) + ($x * 4)
            $rgba[$destination] = $Bgra[$source + 2]
            $rgba[$destination + 1] = $Bgra[$source + 1]
            $rgba[$destination + 2] = $Bgra[$source]
            $rgba[$destination + 3] = $Bgra[$source + 3]
        }
    }
    $rgba
}

function Assert-Rejected
{
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [scriptblock]$Action)

    try
    {
        & $Action
        throw "Adversarial evidence was accepted: $Name"
    }
    catch
    {
        if ($_.Exception.Message -eq "Adversarial evidence was accepted: $Name")
        {
            throw
        }
    }
}

function Copy-TestValue
{
    param([Parameter(Mandatory)][object]$Value)

    $Value | ConvertTo-Json -Depth 20 | ConvertFrom-Json -Depth 20
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testRoot = Join-Path $repoRoot 'artifacts/viewer-evidence-contract-test'
$outsidePath = Join-Path $repoRoot 'artifacts/viewer-evidence-contract-outside.bmp'
$strictRunner = Get-Content (
    Join-Path $repoRoot 'eng/run-storm-native-child.ps1') -Raw
$schema8CleanupPhase =
    'Viewer HWND ownership: phase=initial-camera-automatic-before-after; ' +
    'backend=D3D12;.*live=0; visible=0; stale=0;.*retiredCleanup=0'
$legacyCleanupPhase =
    'Viewer HWND ownership: phase=initial-after; backend=D3D12;'
if (-not $strictRunner.Contains($schema8CleanupPhase) -or
    $strictRunner.Contains($legacyCleanupPhase) -or
    -not $strictRunner.Contains("[ValidateRange(15, 15)]") -or
    -not $strictRunner.Contains("-Name 'stage-camera-backend-smoke'") -or
    -not $strictRunner.Contains(
        '$expectedScenarioCount = $FreshProcessCount + 7'))
{
    throw (
        'The strict schema-8 runner does not retain 15 fresh processes, ' +
        '22 scenarios, the stage-camera run, and the cleanup HWND phase.')
}
Remove-Item $testRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $outsidePath -Force -ErrorAction SilentlyContinue
try
{
    $runRoot = Join-Path $testRoot 'runs/run-1'
    New-Item $runRoot -ItemType Directory -Force | Out-Null
    $pixels = [byte[]]@(
        1, 2, 3, 255, 4, 5, 6, 255,
        7, 8, 9, 255, 10, 11, 12, 255)
    $stormPixels = [byte[]]@(
        21, 22, 23, 255, 24, 25, 26, 255,
        27, 28, 29, 255, 30, 31, 32, 255)
    $restoredPixels = [byte[]]@(
        41, 42, 43, 255, 44, 45, 46, 255,
        47, 48, 49, 255, 50, 51, 52, 255)
    $pixelPath = Join-Path $runRoot 'shell.bmp'
    $stormPath = Join-Path $runRoot 'storm.bmp'
    $restoredPath = Join-Path $runRoot 'restored.bmp'
    Write-TestBitmap $pixelPath 2 2 $pixels
    Write-TestBitmap $stormPath 2 2 $stormPixels
    Write-TestBitmap $restoredPath 2 2 $restoredPixels
    Write-TestBitmap $outsidePath 2 2 $pixels
    $pixel = [ordered]@{
        backend = 'D3D12'
        captureApi = 'PrintWindow(PW_CLIENTONLY|PW_RENDERFULLCONTENT)+DwmFlush'
        sha256 = [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData($pixels))
        width = 2
        height = 2
        length = (Get-Item $pixelPath).Length
        artifact = $pixelPath
    }
    $stormRecorded = Get-StormRecordedBytes $stormPixels 2 2
    $stormPixel = [ordered]@{
        backend = 'Storm'
        captureApi = 'openusd_storm_child_capture_framebuffer(ABI7,preserved-texture)'
        sha256 = [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData($stormRecorded))
        width = 2
        height = 2
        fileSize = (Get-Item $stormPath).Length
        artifact = $stormPath
    }
    $restoredPixel = [ordered]@{
        backend = 'D3D12'
        captureApi = 'PrintWindow(PW_CLIENTONLY|PW_RENDERFULLCONTENT)+DwmFlush'
        sha256 = [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData($restoredPixels))
        width = 2
        height = 2
        length = (Get-Item $restoredPath).Length
        artifact = $restoredPath
    }
    $pixelRecords = Assert-ViewerPixelArtifacts `
        -Pixels @($pixel, $stormPixel, $restoredPixel) `
        -EvidenceRoot $testRoot
    $automaticCameraBytes = [byte[]]::new(260)
    $explicitCameraBytes = [byte[]]$automaticCameraBytes.Clone()
    [BitConverter]::GetBytes([uint32]1).CopyTo($explicitCameraBytes, 0)
    [BitConverter]::GetBytes([double]1).CopyTo($explicitCameraBytes, 4)
    $automaticCameraSignature = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($automaticCameraBytes))
    $explicitCameraSignature = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($explicitCameraBytes))
    $automaticState = [ordered]@{
        backend = 'D3D12'
        phase = 'camera-automatic-before'
        revision = 1
        cameraMode = 'Automatic'
        cameraPayload = [Convert]::ToBase64String($automaticCameraBytes)
        cameraSignature = $automaticCameraSignature
        nativeCameraSignature = ('A' * 16) -join ''
        exactReferencePreserved = $true
    }
    $explicitState = Copy-TestValue $automaticState
    $explicitState.phase = 'camera-explicit'
    $explicitState.revision = 2
    $explicitState.cameraMode = 'Matrices'
    $explicitState.cameraPayload = [Convert]::ToBase64String($explicitCameraBytes)
    $explicitState.cameraSignature = $explicitCameraSignature
    $explicitState.nativeCameraSignature = ('B' * 16) -join ''
    $restoredState = Copy-TestValue $automaticState
    $restoredState.phase = 'camera-automatic-restored'
    $restoredState.revision = 3
    $cameraPixels = @(
        (Copy-TestValue $pixel),
        (Copy-TestValue $stormPixel),
        (Copy-TestValue $restoredPixel))
    foreach ($cameraPixel in $cameraPixels)
    {
        $cameraPixel.backend = 'D3D12'
    }
    $cameraArtifact = [ordered]@{
        states = @($automaticState, $explicitState, $restoredState)
        pixels = $cameraPixels
        cameraTransitions = @(
            [ordered]@{
                backend = 'D3D12'
                automaticBeforePhase = $automaticState.phase
                explicitPhase = $explicitState.phase
                automaticRestoredPhase = $restoredState.phase
                automaticCameraSignature = $automaticCameraSignature
                explicitCameraSignature = $explicitCameraSignature
                restoredCameraSignature = $automaticCameraSignature
                automaticPixelSha256 = $cameraPixels[0].sha256
                explicitPixelSha256 = $cameraPixels[1].sha256
                restoredPixelSha256 = $cameraPixels[2].sha256
                automaticPixelArtifact = $cameraPixels[0].artifact
                explicitPixelArtifact = $cameraPixels[1].artifact
                restoredPixelArtifact = $cameraPixels[2].artifact
                exactReferencesPreserved = $true
                latestRequestedRevision = 0
                latestRequestedCameraSignature = ''
                latestRenderedCameraSignature = ''
                asyncCoalescingValidated = $false
            })
    }
    Assert-ViewerCameraEvidence $cameraArtifact
    $tamperedCamera = Copy-TestValue $cameraArtifact
    $tamperedCamera.states[1].cameraSignature = ('0' * 64) -join ''
    Assert-Rejected 'camera payload signature tamper' {
        Assert-ViewerCameraEvidence $tamperedCamera
    }
    $equalCameraSignature = Copy-TestValue $cameraArtifact
    $equalCameraSignature.states[1].cameraSignature =
        $equalCameraSignature.states[0].cameraSignature
    $equalCameraSignature.cameraTransitions[0].explicitCameraSignature =
        $equalCameraSignature.cameraTransitions[0].automaticCameraSignature
    Assert-Rejected 'equal automatic and explicit camera signature' {
        Assert-ViewerCameraEvidence $equalCameraSignature
    }
    $sampledCameraBytes = [byte[]]$explicitCameraBytes.Clone()
    [BitConverter]::GetBytes([double]2).CopyTo($sampledCameraBytes, 12)
    $sampledCameraSignature = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($sampledCameraBytes))
    $stageIdentifier = 'viewer-stage-camera-smoke.usda'
    $stageCameraPath = '/World/CameraRig/Offset/ShotCamera'
    $stageStates = @(
        [ordered]@{
            backend = 'Storm'
            phase = 'stage-camera-automatic-before'
            revision = 1
            stageIdentifier = $stageIdentifier
            timeCode = 0
            selection = @($stageCameraPath)
            cameraMode = 'Automatic'
            cameraSignature = $automaticCameraSignature
            nativeCameraSignature = ('A' * 16) -join ''
        },
        [ordered]@{
            backend = 'Storm'
            phase = 'stage-camera-initial-Storm'
            revision = 2
            stageIdentifier = $stageIdentifier
            timeCode = 0
            selection = @($stageCameraPath)
            cameraMode = 'Matrices'
            cameraSignature = $explicitCameraSignature
            nativeCameraSignature = ('B' * 16) -join ''
        },
        [ordered]@{
            backend = 'D3D12'
            phase = 'stage-camera-initial-D3D12'
            revision = 2
            stageIdentifier = $stageIdentifier
            timeCode = 0
            selection = @($stageCameraPath)
            cameraMode = 'Matrices'
            cameraSignature = $explicitCameraSignature
            nativeCameraSignature = ('B' * 16) -join ''
        },
        [ordered]@{
            backend = 'Vulkan'
            phase = 'stage-camera-initial-Vulkan'
            revision = 2
            stageIdentifier = $stageIdentifier
            timeCode = 0
            selection = @($stageCameraPath)
            cameraMode = 'Matrices'
            cameraSignature = $explicitCameraSignature
            nativeCameraSignature = ('B' * 16) -join ''
        },
        [ordered]@{
            backend = 'Vulkan'
            phase = 'stage-camera-sampled-Vulkan'
            revision = 3
            stageIdentifier = $stageIdentifier
            timeCode = 24
            selection = @($stageCameraPath)
            cameraMode = 'Matrices'
            cameraSignature = $sampledCameraSignature
            nativeCameraSignature = ('C' * 16) -join ''
        },
        [ordered]@{
            backend = 'D3D12'
            phase = 'stage-camera-sampled-D3D12'
            revision = 3
            stageIdentifier = $stageIdentifier
            timeCode = 24
            selection = @($stageCameraPath)
            cameraMode = 'Matrices'
            cameraSignature = $sampledCameraSignature
            nativeCameraSignature = ('C' * 16) -join ''
        },
        [ordered]@{
            backend = 'Storm'
            phase = 'stage-camera-sampled-Storm'
            revision = 3
            stageIdentifier = $stageIdentifier
            timeCode = 24
            selection = @($stageCameraPath)
            cameraMode = 'Matrices'
            cameraSignature = $sampledCameraSignature
            nativeCameraSignature = ('C' * 16) -join ''
        },
        [ordered]@{
            backend = 'Storm'
            phase = 'stage-camera-automatic-restored'
            revision = 4
            stageIdentifier = $stageIdentifier
            timeCode = 0
            selection = @($stageCameraPath)
            cameraMode = 'Automatic'
            cameraSignature = $automaticCameraSignature
            nativeCameraSignature = ('A' * 16) -join ''
        })
    $stagePixels = @(
        [ordered]@{ backend = 'Storm'; sha256 = ('1' * 64); artifact = 'auto-before.bmp' },
        [ordered]@{ backend = 'Storm'; sha256 = ('2' * 64); artifact = 'initial-storm.bmp' },
        [ordered]@{ backend = 'D3D12'; sha256 = ('3' * 64); artifact = 'initial-d3d.bmp' },
        [ordered]@{ backend = 'Vulkan'; sha256 = ('4' * 64); artifact = 'initial-vulkan.bmp' },
        [ordered]@{ backend = 'Vulkan'; sha256 = ('5' * 64); artifact = 'sampled-vulkan.bmp' },
        [ordered]@{ backend = 'D3D12'; sha256 = ('6' * 64); artifact = 'sampled-d3d.bmp' },
        [ordered]@{ backend = 'Storm'; sha256 = ('3' * 64); artifact = 'sampled-storm.bmp' },
        [ordered]@{ backend = 'Storm'; sha256 = ('1' * 64); artifact = 'auto-restored.bmp' })
    $stageFrame = {
        param(
            [Parameter(Mandatory)][object]$State,
            [Parameter(Mandatory)][object]$Pixel)

        $storm = [string]$State.backend -ceq 'Storm'
        [ordered]@{
            backend = [string]$State.backend
            phase = [string]$State.phase
            timeCode = [double]$State.timeCode
            stateRevision = [uint64]$State.revision
            cameraSignature = [string]$State.cameraSignature
            nativeCameraSignature = [string]$State.nativeCameraSignature
            pixelSha256 = [string]$Pixel.sha256
            pixelArtifact = [string]$Pixel.artifact
            exactReferencePreserved = $true
            latestRequestedRevision =
                if ($storm) { [uint64]$State.revision } else { [uint64]0 }
            latestRequestedCameraSignature =
                if ($storm) { [string]$State.nativeCameraSignature } else { '' }
            latestRenderedCameraSignature =
                if ($storm) { [string]$State.nativeCameraSignature } else { '' }
        }
    }
    $stageCameraArtifact = [ordered]@{
        scenario = 'stage-camera-backend-smoke'
        states = $stageStates
        pixels = $stagePixels
        stageCamera = [ordered]@{
            source = 'ViewerSchedulerStageCameraSource'
            stageIdentifier = $stageIdentifier
            stageSha256 = ('7' * 64)
            cameraPath = $stageCameraPath
            initialTimeCode = 0
            sampledTimeCode = 24
            initialSnapshotSha256 = ('8' * 64)
            sampledSnapshotSha256 = ('9' * 64)
            automaticBefore = [ordered]@{
                backend = 'Storm'
                phase = $stageStates[0].phase
                timeCode = 0
                stateRevision = 1
                cameraSignature = $automaticCameraSignature
                nativeCameraSignature = ('A' * 16) -join ''
                pixelSha256 = $stagePixels[0].sha256
                pixelArtifact = $stagePixels[0].artifact
            }
            initialFrames = @(
                (& $stageFrame $stageStates[1] $stagePixels[1]),
                (& $stageFrame $stageStates[2] $stagePixels[2]),
                (& $stageFrame $stageStates[3] $stagePixels[3]))
            sampledFrames = @(
                (& $stageFrame $stageStates[4] $stagePixels[4]),
                (& $stageFrame $stageStates[5] $stagePixels[5]),
                (& $stageFrame $stageStates[6] $stagePixels[6]))
            automaticRestored = [ordered]@{
                backend = 'Storm'
                phase = $stageStates[7].phase
                timeCode = 0
                stateRevision = 4
                cameraSignature = $automaticCameraSignature
                nativeCameraSignature = ('A' * 16) -join ''
                pixelSha256 = $stagePixels[7].sha256
                pixelArtifact = $stagePixels[7].artifact
            }
            exactStatePreservedAcrossBackends = $true
        }
    }
    Assert-ViewerStageCameraEvidence $stageCameraArtifact | Out-Null
    $missingStageCameraPath = Copy-TestValue $stageCameraArtifact
    $missingStageCameraPath.stageCamera.cameraPath = ''
    Assert-Rejected 'missing stage-camera path' {
        Assert-ViewerStageCameraEvidence $missingStageCameraPath
    }
    $missingStageHash = Copy-TestValue $stageCameraArtifact
    $missingStageHash.stageCamera.stageSha256 = ''
    Assert-Rejected 'missing stage-camera hash' {
        Assert-ViewerStageCameraEvidence $missingStageHash
    }
    $missingStagePixel = Copy-TestValue $stageCameraArtifact
    $missingStagePixel.stageCamera.sampledFrames[0].pixelArtifact = 'missing.bmp'
    Assert-Rejected 'unbound stage-camera pixel' {
        Assert-ViewerStageCameraEvidence $missingStagePixel
    }
    $staleStageTime = Copy-TestValue $stageCameraArtifact
    $staleStageTime.states[4].timeCode = 0
    Assert-Rejected 'stale stage-camera time' {
        Assert-ViewerStageCameraEvidence $staleStageTime
    }
    $staleStageState = Copy-TestValue $stageCameraArtifact
    $staleStageState.stageCamera.sampledFrames[0].stateRevision = 2
    Assert-Rejected 'stale stage-camera state' {
        Assert-ViewerStageCameraEvidence $staleStageState
    }
    $navigationMessages = @(
        1..7 | ForEach-Object {
            [ordered]@{
                target = 'StormChild'
                api = 'SendMessageTimeoutW'
                apiSucceeded = $true
                wndProcObserved = $true
                handlerObserved = $true
                synthesized = $false
            }
        })
    $navigationArtifact = [ordered]@{
        platformHandle = 'HWND'
        inputs = @([ordered]@{ backend = 'Storm' })
        states = @(
            [ordered]@{
                backend = 'Storm'
                phase = 'native-navigation-after'
                cameraSignature = $explicitCameraSignature
            })
        pixels = @($pixel, $stormPixel)
        nativeNavigation = @(
            [ordered]@{
                backend = 'Storm'
                phase = 'native-navigation-after'
                deliveryApi =
                    'SendMessageTimeoutW+StormChildWndProc+ABI7Poll+' +
                    'ViewerCameraNavigationUiAdapter'
                snapshotApi = 'openusd_storm_child_get_navigation_input(ABI7,v1)'
                stormChildAbiVersion = 7
                gesture = 'Alt+Left Orbit'
                sequenceBefore = 1
                sequencePressed = 4
                sequenceMoved = 5
                sequenceAfter = 8
                pressedButtons = 'Left'
                pressedModifiers = 'Alt'
                pressedState = 'Focused'
                pointerDeltaX = 80
                pointerDeltaY = 48
                avaloniaRoutedEvents = 0
                cameraBeforeSignature = $automaticCameraSignature
                cameraAfterSignature = $explicitCameraSignature
                pixelBeforeSha256 = $pixel.sha256
                pixelAfterSha256 = $stormPixel.sha256
                pixelBeforeArtifact = $pixel.artifact
                pixelAfterArtifact = $stormPixel.artifact
                cameraChanged = $true
                pixelChanged = $true
                win32Messages = $navigationMessages
            })
    }
    Assert-ViewerNativeNavigationEvidence $navigationArtifact
    $duplicateNavigation = Copy-TestValue $navigationArtifact
    $duplicateNavigation.nativeNavigation[0].avaloniaRoutedEvents = 1
    Assert-Rejected 'duplicate Avalonia native navigation' {
        Assert-ViewerNativeNavigationEvidence $duplicateNavigation
    }

    foreach ($name in @(
        'switching-evidence.json',
        'identity.json',
        'viewer-status.txt',
        'viewer.log'))
    {
        [IO.File]::WriteAllText((Join-Path $runRoot $name), "bound $name")
    }
    $runArtifacts = @(Get-ChildItem $runRoot -File |
        Where-Object Extension -ne '.bmp' |
        Sort-Object FullName |
        ForEach-Object {
            Get-ViewerEvidenceFileRecord -EvidenceRoot $testRoot -Path $_.FullName
        })
    $validRun = [ordered]@{
        schemaVersion = $ViewerEvidenceSchemaVersion
        run = 'run-1'
        artifactRoot = 'runs/run-1'
        stormChildAbiVersion = 7
        runtimeCompositor = 'ANGLE/D3D11 (runtime-observed)'
        pixelCount = 3
        pixelHashes = @($pixel.sha256, $stormPixel.sha256, $restoredPixel.sha256)
        cameraTransitionCount = 1
        nativeNavigationCount = 1
        cameraTransitions = @(
            [ordered]@{
                backend = 'D3D12'
                automaticCameraSignature = ('1' * 64) -join ''
                explicitCameraSignature = ('2' * 64) -join ''
                restoredCameraSignature = ('1' * 64) -join ''
                automaticPixelSha256 = $pixel.sha256
                explicitPixelSha256 = $stormPixel.sha256
                restoredPixelSha256 = $restoredPixel.sha256
                automaticPixelPath = $pixelRecords[0].path
                explicitPixelPath = $pixelRecords[1].path
                restoredPixelPath = $pixelRecords[2].path
            })
        nativeNavigation = @(
            [ordered]@{
                backend = 'Storm'
                phase = 'native-navigation-after'
                deliveryApi =
                    'SendMessageTimeoutW+StormChildWndProc+ABI7Poll+' +
                    'ViewerCameraNavigationUiAdapter'
                snapshotApi =
                    'openusd_storm_child_get_navigation_input(ABI7,v1)'
                stormChildAbiVersion = 7
                avaloniaRoutedEvents = 0
                beforeCameraSignature = ('1' * 64) -join ''
                afterCameraSignature = ('2' * 64) -join ''
                beforePixelSha256 = $pixel.sha256
                afterPixelSha256 = $stormPixel.sha256
                beforePixelPath = $pixelRecords[0].path
                afterPixelPath = $pixelRecords[1].path
            })
        artifacts = $runArtifacts
        pixelArtifacts = $pixelRecords
    }
    $valid = [ordered]@{
        schemaVersion = $ViewerEvidenceSchemaVersion
        status = 'passed'
        scenarioCount = 1
        runs = @($validRun)
    }
    Assert-ViewerAggregateEvidence `
        -Aggregate ([pscustomobject]$valid) `
        -ExpectedRunCount 1 `
        -EvidenceRoot $testRoot
    Assert-ViewerAggregateEvidence `
        -Aggregate (Copy-TestValue $valid) `
        -ExpectedRunCount 1 `
        -EvidenceRoot $testRoot

    $schema7Aggregate = Copy-TestValue $valid
    $schema7Aggregate.schemaVersion = 7
    Assert-Rejected 'schema 7 aggregate' {
        Assert-ViewerAggregateEvidence $schema7Aggregate 1 $testRoot
    }
    $schema7Run = Copy-TestValue $valid
    $schema7Run.runs[0].schemaVersion = 7
    Assert-Rejected 'schema 7 run' {
        Assert-ViewerAggregateEvidence $schema7Run 1 $testRoot
    }

    $nonWindows = Copy-TestValue $valid
    $nonWindows.runs[0].runtimeCompositor = 'X11 (runtime-observed)'
    $nonWindows.runs[0].nativeNavigationCount = 0
    $nonWindows.runs[0].nativeNavigation = @()
    Assert-ViewerAggregateEvidence $nonWindows 1 $testRoot
    $nonWindowsNavigation = Copy-TestValue $valid
    $nonWindowsNavigation.runs[0].runtimeCompositor = 'X11 (runtime-observed)'
    Assert-Rejected 'aggregate non-Windows native navigation' {
        Assert-ViewerAggregateEvidence $nonWindowsNavigation 1 $testRoot
    }

    $missingTopLevelAbi = Copy-TestValue $valid
    $missingTopLevelAbi.runs[0].PSObject.Properties.Remove(
        'stormChildAbiVersion')
    Assert-Rejected 'aggregate missing top-level Storm child ABI' {
        Assert-ViewerAggregateEvidence $missingTopLevelAbi 1 $testRoot
    }
    $abi6TopLevel = Copy-TestValue $valid
    $abi6TopLevel.runs[0].stormChildAbiVersion = 6
    Assert-Rejected 'aggregate ABI6 top-level Storm child ABI' {
        Assert-ViewerAggregateEvidence $abi6TopLevel 1 $testRoot
    }
    $missingCapture = Copy-TestValue $valid
    $missingCapture.runs[0].pixelArtifacts[1].PSObject.Properties.Remove(
        'captureApi')
    Assert-Rejected 'aggregate missing Storm capture API' {
        Assert-ViewerAggregateEvidence $missingCapture 1 $testRoot
    }
    $abi6Capture = Copy-TestValue $valid
    $abi6Capture.runs[0].pixelArtifacts[1].captureApi =
        'openusd_storm_child_capture_framebuffer(ABI6,preserved-texture)'
    Assert-Rejected 'aggregate ABI6 Storm capture API' {
        Assert-ViewerAggregateEvidence $abi6Capture 1 $testRoot
    }
    $missingNavigation = Copy-TestValue $valid
    $missingNavigation.runs[0].nativeNavigationCount = 0
    $missingNavigation.runs[0].nativeNavigation = @()
    Assert-Rejected 'aggregate missing Windows Storm navigation' {
        Assert-ViewerAggregateEvidence $missingNavigation 1 $testRoot
    }
    $missingDelivery = Copy-TestValue $valid
    $missingDelivery.runs[0].nativeNavigation[0].PSObject.Properties.Remove(
        'deliveryApi')
    Assert-Rejected 'aggregate missing Storm navigation delivery API' {
        Assert-ViewerAggregateEvidence $missingDelivery 1 $testRoot
    }
    $abi6Delivery = Copy-TestValue $valid
    $abi6Delivery.runs[0].nativeNavigation[0].deliveryApi =
        'SendMessageTimeoutW+StormChildWndProc+ABI6Poll+' +
        'ViewerCameraNavigationUiAdapter'
    Assert-Rejected 'aggregate ABI6 Storm navigation delivery API' {
        Assert-ViewerAggregateEvidence $abi6Delivery 1 $testRoot
    }
    $missingSnapshot = Copy-TestValue $valid
    $missingSnapshot.runs[0].nativeNavigation[0].PSObject.Properties.Remove(
        'snapshotApi')
    Assert-Rejected 'aggregate missing Storm navigation snapshot API' {
        Assert-ViewerAggregateEvidence $missingSnapshot 1 $testRoot
    }
    $abi6Snapshot = Copy-TestValue $valid
    $abi6Snapshot.runs[0].nativeNavigation[0].snapshotApi =
        'openusd_storm_child_get_navigation_input(ABI6,v1)'
    Assert-Rejected 'aggregate ABI6 Storm navigation snapshot API' {
        Assert-ViewerAggregateEvidence $abi6Snapshot 1 $testRoot
    }
    $missingNestedAbi = Copy-TestValue $valid
    $missingNestedAbi.runs[0].nativeNavigation[0].PSObject.Properties.Remove(
        'stormChildAbiVersion')
    Assert-Rejected 'aggregate missing nested Storm child ABI' {
        Assert-ViewerAggregateEvidence $missingNestedAbi 1 $testRoot
    }
    $abi6Nested = Copy-TestValue $valid
    $abi6Nested.runs[0].nativeNavigation[0].stormChildAbiVersion = 6
    Assert-Rejected 'aggregate ABI6 nested Storm child ABI' {
        Assert-ViewerAggregateEvidence $abi6Nested 1 $testRoot
    }

    $originalPixel = [IO.File]::ReadAllBytes($pixelPath)
    $tamperedPixel = [byte[]]$originalPixel.Clone()
    $tamperedPixel[$tamperedPixel.Length - 1] = $tamperedPixel[-1] -bxor 0xFF
    [IO.File]::WriteAllBytes($pixelPath, $tamperedPixel)
    Assert-Rejected 'modified pixel' {
        Assert-ViewerPixelArtifacts @($pixel) $testRoot
    }
    [IO.File]::WriteAllBytes($pixelPath, $originalPixel)

    Remove-Item $pixelPath
    Assert-Rejected 'deleted pixel' {
        Assert-ViewerPixelArtifacts @($pixel) $testRoot
    }
    [IO.File]::WriteAllBytes($pixelPath, $originalPixel)

    $externalPixel = Copy-TestValue $pixel
    $externalPixel.artifact = $outsidePath
    Assert-Rejected 'external pixel path' {
        Assert-ViewerPixelArtifacts @($externalPixel) $testRoot
    }
    $traversalPixel = Copy-TestValue $pixel
    $traversalPixel.artifact = '../viewer-evidence-contract-outside.bmp'
    Assert-Rejected 'pixel path traversal' {
        Assert-ViewerPixelArtifacts @($traversalPixel) $testRoot
    }
    Assert-Rejected 'duplicate pixel file' {
        Assert-ViewerPixelArtifacts @($pixel, $pixel) $testRoot
    }
    $wrongHash = Copy-TestValue $pixel
    $wrongHash.sha256 = '00' * 32
    Assert-Rejected 'pixel hash tamper' {
        Assert-ViewerPixelArtifacts @($wrongHash) $testRoot
    }
    $wrongSize = Copy-TestValue $pixel
    $wrongSize.length++
    Assert-Rejected 'pixel size tamper' {
        Assert-ViewerPixelArtifacts @($wrongSize) $testRoot
    }
    $wrongDimensions = Copy-TestValue $pixel
    $wrongDimensions.width++
    Assert-Rejected 'pixel dimensions tamper' {
        Assert-ViewerPixelArtifacts @($wrongDimensions) $testRoot
    }

    $logPath = Join-Path $runRoot 'viewer.log'
    $originalLog = [IO.File]::ReadAllBytes($logPath)
    [IO.File]::AppendAllText($logPath, 'tampered')
    Assert-Rejected 'aggregate run artifact tamper' {
        Assert-ViewerAggregateEvidence ([pscustomobject]$valid) 1 $testRoot
    }
    [IO.File]::WriteAllBytes($logPath, $originalLog)

    Remove-Item $logPath
    Assert-Rejected 'aggregate run artifact delete' {
        Assert-ViewerAggregateEvidence ([pscustomobject]$valid) 1 $testRoot
    }
    [IO.File]::WriteAllBytes($logPath, $originalLog)

    $unboundPath = Join-Path $runRoot 'unbound.txt'
    [IO.File]::WriteAllText($unboundPath, 'not in aggregate manifest')
    Assert-Rejected 'aggregate unbound run artifact' {
        Assert-ViewerAggregateEvidence ([pscustomobject]$valid) 1 $testRoot
    }
    Remove-Item $unboundPath

    [IO.File]::WriteAllBytes($pixelPath, $tamperedPixel)
    Assert-Rejected 'aggregate pixel tamper' {
        Assert-ViewerAggregateEvidence ([pscustomobject]$valid) 1 $testRoot
    }
    [IO.File]::WriteAllBytes($pixelPath, $originalPixel)

    Remove-Item $stormPath
    Assert-Rejected 'aggregate pixel delete' {
        Assert-ViewerAggregateEvidence ([pscustomobject]$valid) 1 $testRoot
    }
    [IO.File]::WriteAllBytes(
        $stormPath,
        [byte[]]((Get-ViewerBitmapData $pixelPath).bytes))
    Write-TestBitmap $stormPath 2 2 $stormPixels

    $aggregateTraversal = Copy-TestValue $valid
    $aggregateTraversal.runs[0].pixelArtifacts[0].path =
        '../viewer-evidence-contract-outside.bmp'
    Assert-Rejected 'aggregate path traversal' {
        Assert-ViewerAggregateEvidence $aggregateTraversal 1 $testRoot
    }
    $aggregateDuplicate = Copy-TestValue $valid
    $aggregateDuplicate.runs[0].pixelArtifacts +=
        $aggregateDuplicate.runs[0].pixelArtifacts[0]
    $aggregateDuplicate.runs[0].pixelCount = 4
    $aggregateDuplicate.runs[0].pixelHashes +=
        $aggregateDuplicate.runs[0].pixelHashes[0]
    Assert-Rejected 'aggregate duplicate pixel' {
        Assert-ViewerAggregateEvidence $aggregateDuplicate 1 $testRoot
    }
    $aggregateStaleCamera = Copy-TestValue $valid
    $aggregateStaleCamera.runs[0].cameraTransitions[0].explicitCameraSignature =
        $aggregateStaleCamera.runs[0].cameraTransitions[0].automaticCameraSignature
    Assert-Rejected 'aggregate stale camera signature' {
        Assert-ViewerAggregateEvidence $aggregateStaleCamera 1 $testRoot
    }
    $aggregateUnboundCamera = Copy-TestValue $valid
    $aggregateUnboundCamera.runs[0].cameraTransitions[0].explicitPixelPath =
        'runs/run-1/missing-camera.bmp'
    Assert-Rejected 'aggregate unbound camera pixel' {
        Assert-ViewerAggregateEvidence $aggregateUnboundCamera 1 $testRoot
    }

    $stageAssetPath = Join-Path $runRoot 'viewer-stage-camera-smoke.usda'
    [IO.File]::WriteAllText($stageAssetPath, '#usda 1.0 stage-camera')
    $stageAssetRecord = Get-ViewerEvidenceFileRecord `
        -EvidenceRoot $testRoot `
        -Path $stageAssetPath
    $aggregateStageCamera = [ordered]@{
        source = 'ViewerSchedulerStageCameraSource'
        stageIdentifier = 'viewer-stage-camera-smoke.usda'
        stageSha256 = [string]$stageAssetRecord.sha256
        stageAssetPath = [string]$stageAssetRecord.path
        stageAssetLength = [int64]$stageAssetRecord.length
        cameraPath = '/World/CameraRig/Offset/ShotCamera'
        initialTimeCode = 0
        sampledTimeCode = 24
        initialSnapshotSha256 = ('8' * 64)
        sampledSnapshotSha256 = ('9' * 64)
        automaticBefore = [ordered]@{
            backend = 'Storm'
            phase = 'stage-camera-automatic-before'
            timeCode = 0
            stateRevision = 1
            cameraSignature = $automaticCameraSignature
            nativeCameraSignature = ('A' * 16) -join ''
            pixelSha256 = [string]$pixelRecords[2].pixelSha256
            pixelPath = [string]$pixelRecords[2].path
        }
        initialFrames = @(
            foreach ($backend in @('Storm', 'D3D12', 'Vulkan'))
            {
                [ordered]@{
                    backend = $backend
                    phase = "stage-camera-initial-$backend"
                    timeCode = 0
                    stateRevision = 2
                    cameraSignature = $explicitCameraSignature
                    nativeCameraSignature = ('B' * 16) -join ''
                    pixelSha256 = [string]$pixelRecords[0].pixelSha256
                    pixelPath = [string]$pixelRecords[0].path
                    exactReferencePreserved = $true
                    latestRequestedRevision =
                        if ($backend -eq 'Storm') { 2 } else { 0 }
                    latestRequestedCameraSignature =
                        if ($backend -eq 'Storm') { ('B' * 16) -join '' } else { '' }
                    latestRenderedCameraSignature =
                        if ($backend -eq 'Storm') { ('B' * 16) -join '' } else { '' }
                }
            })
        sampledFrames = @(
            foreach ($backend in @('Storm', 'D3D12', 'Vulkan'))
            {
                [ordered]@{
                    backend = $backend
                    phase = "stage-camera-sampled-$backend"
                    timeCode = 24
                    stateRevision = 3
                    cameraSignature = $sampledCameraSignature
                    nativeCameraSignature = ('C' * 16) -join ''
                    pixelSha256 = [string]$pixelRecords[1].pixelSha256
                    pixelPath = [string]$pixelRecords[1].path
                    exactReferencePreserved = $true
                    latestRequestedRevision =
                        if ($backend -eq 'Storm') { 3 } else { 0 }
                    latestRequestedCameraSignature =
                        if ($backend -eq 'Storm') { ('C' * 16) -join '' } else { '' }
                    latestRenderedCameraSignature =
                        if ($backend -eq 'Storm') { ('C' * 16) -join '' } else { '' }
                }
            })
        automaticRestored = [ordered]@{
            backend = 'Storm'
            phase = 'stage-camera-automatic-restored'
            timeCode = 0
            stateRevision = 4
            cameraSignature = $automaticCameraSignature
            nativeCameraSignature = ('A' * 16) -join ''
            pixelSha256 = [string]$pixelRecords[2].pixelSha256
            pixelPath = [string]$pixelRecords[2].path
        }
        exactStatePreservedAcrossBackends = $true
    }
    $validStage = Copy-TestValue $valid
    $validStage.runs[0] | Add-Member `
        -NotePropertyName scenario `
        -NotePropertyValue 'stage-camera-backend-smoke'
    $validStage.runs[0] | Add-Member `
        -NotePropertyName stageCamera `
        -NotePropertyValue $aggregateStageCamera
    $validStage.runs[0].artifacts += $stageAssetRecord
    Assert-ViewerAggregateEvidence $validStage 1 $testRoot
    $aggregateMissingStagePath = Copy-TestValue $validStage
    $aggregateMissingStagePath.runs[0].stageCamera.cameraPath = ''
    Assert-Rejected 'aggregate missing stage-camera path' {
        Assert-ViewerAggregateEvidence $aggregateMissingStagePath 1 $testRoot
    }
    $aggregateMissingStageHash = Copy-TestValue $validStage
    $aggregateMissingStageHash.runs[0].stageCamera.stageSha256 = ''
    Assert-Rejected 'aggregate missing stage-camera hash' {
        Assert-ViewerAggregateEvidence $aggregateMissingStageHash 1 $testRoot
    }
    $aggregateMissingStagePixel = Copy-TestValue $validStage
    $aggregateMissingStagePixel.runs[0].stageCamera.sampledFrames[0].pixelPath =
        'runs/run-1/missing-stage-camera.bmp'
    Assert-Rejected 'aggregate unbound stage-camera pixel' {
        Assert-ViewerAggregateEvidence $aggregateMissingStagePixel 1 $testRoot
    }
    $originalStageAsset = [IO.File]::ReadAllBytes($stageAssetPath)
    [IO.File]::AppendAllText($stageAssetPath, 'tampered')
    Assert-Rejected 'aggregate stage-camera asset tamper' {
        Assert-ViewerAggregateEvidence $validStage 1 $testRoot
    }
    [IO.File]::WriteAllBytes($stageAssetPath, $originalStageAsset)

    Write-Output (
        "VIEWER_EVIDENCE_CONTRACT_TEST passed schema=$ViewerEvidenceSchemaVersion " +
        'pixelsBound=true artifactsBound=true adversarialTamper=true')
}
finally
{
    Remove-Item $testRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $outsidePath -Force -ErrorAction SilentlyContinue
}
