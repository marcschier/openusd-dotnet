#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

[CmdletBinding()]
param(
    [ValidateRange(100, 10000)]
    [int]$SwitchCount = 100,
    [ValidateRange(90, 86400)]
    [int]$SurvivalSeconds = 90,
    [ValidateRange(15, 15)]
    [int]$FreshProcessCount = 15
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'viewer-evidence-contract.ps1')
$outputRoot = Join-Path $repoRoot 'artifacts/storm-native-child'
$runtimeRoot = Join-Path $outputRoot 'runtime'
$runsRoot = Join-Path $outputRoot 'runs'
$runViewer = Join-Path $PSScriptRoot 'run-viewer.ps1'
$stageCameraStagePath = Join-Path $repoRoot 'test-assets/viewer-stage-camera-smoke.usda'
$runtimePrepared = $false

if (Test-Path $outputRoot)
{
    Remove-Item $outputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

function Assert-ViewerRunEvidence
{
    param(
        [Parameter(Mandatory)]
        [string]$RunRoot,
        [Parameter(Mandatory)]
        [string]$EvidencePath,
        [Parameter(Mandatory)]
        [string]$IdentityPath,
        [Parameter(Mandatory)]
        [string]$StatusPath,
        [Parameter(Mandatory)]
        [string]$RunName,
        [Parameter(Mandatory)]
        [string]$EvidenceRoot,
        [string]$StageAssetPath,
        [string[]]$ExpectedBackends,
        [string[]]$ExpectedStatusPatterns)

    if (-not (Test-Path $EvidencePath))
    {
        throw "Missing Viewer evidence artifact: $EvidencePath"
    }
    if (-not (Test-Path $IdentityPath))
    {
        throw "Missing Viewer identity artifact: $IdentityPath"
    }
    $artifact = Get-Content $EvidencePath -Raw | ConvertFrom-Json -Depth 20
    $identity = Get-Content $IdentityPath -Raw | ConvertFrom-Json -Depth 20
    $expectedStormCaptureApi =
        'openusd_storm_child_capture_framebuffer(ABI7,preserved-texture)'
    $expectedStormNavigationDeliveryApi =
        'SendMessageTimeoutW+StormChildWndProc+ABI7Poll+ViewerCameraNavigationUiAdapter'
    $expectedStormNavigationSnapshotApi =
        'openusd_storm_child_get_navigation_input(ABI7,v1)'
    Assert-ViewerEvidenceSchemaVersion -Artifact $artifact -ArtifactKind 'Viewer run'
    if ($artifact.runtimeCompositor -ne 'ANGLE/D3D11 (runtime-observed)' -or
        $artifact.PSObject.Properties.Name -contains 'shellMode' -or
        $artifact.platformHandle -ne 'HWND' -or
        [int]$artifact.stormChildAbiVersion -ne 7 -or
        @($artifact.loadedArtifacts).Count -eq 0 -or
        @($artifact.states).Count -eq 0 -or
        @($artifact.pixels).Count -eq 0 -or
        @($artifact.inputs).Count -eq 0 -or
        @($artifact.cameraTransitions).Count -eq 0 -or
        @($artifact.compositions).Count -eq 0 -or
        @($artifact.windowOwnership).Count -eq 0)
    {
        throw "Viewer evidence contains missing or invalid measured runtime fields."
    }
    $sourceBeforeJson = $identity.sourceBefore.files |
        ConvertTo-Json -Depth 8 -Compress
    $sourceAfterJson = $identity.sourceAfter.files |
        ConvertTo-Json -Depth 8 -Compress
    $binariesBeforeJson = $identity.binariesBefore.files |
        ConvertTo-Json -Depth 8 -Compress
    $binariesAfterJson = $identity.binariesAfter.files |
        ConvertTo-Json -Depth 8 -Compress
    if (-not $identity.sourceUnchanged -or -not $identity.binariesUnchanged -or
        $identity.sourceBefore.sha256 -ne $identity.sourceAfter.sha256 -or
        $sourceBeforeJson -cne $sourceAfterJson -or
        $binariesBeforeJson -cne $binariesAfterJson)
    {
        throw "Viewer source or binary identities changed during the process."
    }

    $publishRoot = [System.IO.Path]::GetFullPath($RunRoot)
    $beforeFiles = @{}
    foreach ($file in @($identity.binariesBefore.files))
    {
        $beforeFiles[$file.path.Replace('/', '\')] = $file
    }
    foreach ($loaded in @($artifact.loadedArtifacts))
    {
        $full = [System.IO.Path]::GetFullPath([string]$loaded)
        if (-not $full.StartsWith($publishRoot, [StringComparison]::OrdinalIgnoreCase))
        {
            throw "Loaded non-system artifact was outside the pre-hashed run root: $full"
        }
        $relative = [System.IO.Path]::GetRelativePath($publishRoot, $full).Replace('/', '\')
        if (-not $beforeFiles.ContainsKey($relative))
        {
            throw "Loaded artifact was not hashed before launch: $relative"
        }
    }
    foreach ($required in @(
        'OpenUsd.Viewer.exe',
        'OpenUsd.Viewer.dll',
        'bin\openusd_storm_child.dll'))
    {
        if (-not $beforeFiles.ContainsKey($required))
        {
            throw "Required pre-hashed runtime artifact is missing: $required"
        }
    }

    $pixelArtifactRecords = Assert-ViewerPixelArtifacts `
        -Pixels @($artifact.pixels) `
        -EvidenceRoot $EvidenceRoot
    $previousHash = $null
    foreach ($pixel in @($artifact.pixels))
    {
        $minimum = [Math]::Max(
            100,
            [long]$pixel.width * [long]$pixel.height / 1000)
        if ([string]::IsNullOrWhiteSpace([string]$pixel.sha256) -or
            ([string]$pixel.sha256).Length -ne 64 -or
            [long]$pixel.nonBackgroundPixels -lt $minimum -or
            $pixel.sha256 -eq $previousHash)
        {
            throw "Viewer pixel evidence was blank, stale, or missing."
        }
        $expectedCaptureApi = if ($pixel.backend -eq 'Storm')
        {
            $expectedStormCaptureApi
        }
        else
        {
            'PrintWindow(PW_CLIENTONLY|PW_RENDERFULLCONTENT)+DwmFlush'
        }
        if ($pixel.captureApi -ne $expectedCaptureApi)
        {
            throw "Viewer $($pixel.backend) used unexpected pixel capture API '$($pixel.captureApi)'."
        }
        $previousHash = $pixel.sha256
    }
    foreach ($navigation in @($artifact.nativeNavigation))
    {
        if ([string]$navigation.deliveryApi -ne
                $expectedStormNavigationDeliveryApi -or
            [string]$navigation.snapshotApi -ne
                $expectedStormNavigationSnapshotApi -or
            [int]$navigation.stormChildAbiVersion -ne 7)
        {
            throw (
                'Viewer native Storm navigation did not use ABI 7 ' +
                'provenance labels.')
        }
    }
    foreach ($input in @($artifact.inputs))
    {
        if ([int]$input.resizeEvents -lt 1 -or
            [int]$input.scalingEvents -lt 2 -or
            [bool]$input.synthesized -or
            [string]::IsNullOrWhiteSpace([string]$input.deliveryApi) -or
            [string]$input.deliveryApi -match '(?i)RaiseEvent|RawInput|synthetic' -or
            [string]$input.deliveryApi -notmatch 'SendMessageTimeoutW' -or
            [string]$input.deliveryApi -notmatch 'EnableMouseInPointer\(false,success=True,error=0\)' -or
            [string]$input.deliveryApi -notmatch 'DiagnosticWM_DPICHANGED' -or
            [int]$input.focusEvents -lt 1 -or
            [int]$input.pointerMoves -lt 1 -or
            [int]$input.pointerButtons -lt 2 -or
            [int]$input.wheelEvents -lt 1 -or
            [int]$input.keyEvents -lt 2 -or
            [double]$input.renderScalingBefore -le 0 -or
            [double]$input.renderScalingObserved -le 0 -or
            [double]$input.renderScalingAfter -le 0 -or
            [int]$input.nativeDpiBefore -lt 1 -or
            [int]$input.nativeDpiObserved -lt 1 -or
            [int]$input.nativeDpiAfter -lt 1 -or
            [int]$input.nativeDpiObserved -eq [int]$input.nativeDpiBefore -or
            [int]$input.nativeDpiAfter -ne [int]$input.nativeDpiBefore -or
            [Math]::Abs(
                [double]$input.renderScalingObserved -
                [double]$input.renderScalingBefore) -lt 0.001 -or
            [Math]::Abs(
                [double]$input.renderScalingAfter -
                [double]$input.renderScalingBefore) -ge 0.001 -or
            (([int]$input.physicalWidthBefore -eq [int]$input.physicalWidthObserved) -and
             ([int]$input.physicalHeightBefore -eq [int]$input.physicalHeightObserved)) -or
            [int]$input.physicalWidthAfter -ne [int]$input.physicalWidthBefore -or
            [int]$input.physicalHeightAfter -ne [int]$input.physicalHeightBefore -or
            @($input.win32Messages).Count -lt 9)
        {
            throw "Viewer input evidence is incomplete for $($input.backend)."
        }
        if ($input.backend -eq 'Storm' -and
            ([long]$input.nativeFocusEvents -lt 1 -or
             [long]$input.nativePointerEvents -lt 3 -or
             [long]$input.nativeWheelEvents -lt 1 -or
             [long]$input.nativeKeyEvents -lt 2))
        {
            throw "Storm native input handlers did not observe the delivered events."
        }
        $expectedTopLevelMessages = @(
            'WM_DPICHANGED(change)',
            'WM_DPICHANGED(restore)',
            'WM_KILLFOCUS',
            'WM_MOUSEMOVE',
            'WM_LBUTTONDOWN',
            'WM_LBUTTONUP',
            'WM_MOUSEWHEEL',
            'WM_KEYDOWN',
            'WM_KEYUP')
        foreach ($name in $expectedTopLevelMessages)
        {
            $matching = @($input.win32Messages | Where-Object {
                $_.target -eq 'ViewerTopLevel' -and $_.message -eq $name
            })
            if ($matching.Count -ne 1)
            {
                throw "Viewer input evidence is missing top-level $name."
            }
        }
        if ($input.backend -eq 'Storm')
        {
            foreach ($name in @(
                'WM_SETFOCUS',
                'WM_MOUSEMOVE',
                'WM_LBUTTONDOWN',
                'WM_LBUTTONUP',
                'WM_MOUSEWHEEL',
                'WM_KEYDOWN',
                'WM_KEYUP'))
            {
                $matching = @($input.win32Messages | Where-Object {
                    $_.target -eq 'StormChild' -and $_.message -eq $name
                })
                if ($matching.Count -ne 1)
                {
                    throw "Viewer input evidence is missing Storm child $name."
                }
            }
        }
        elseif (@($input.win32Messages | Where-Object {
                    $_.target -eq 'StormChild'
                }).Count -ne 0)
        {
            throw "Non-Storm input evidence contains Storm child messages."
        }
        foreach ($message in @($input.win32Messages))
        {
            if ([string]$message.api -ne 'SendMessageTimeoutW' -or
                [string]::IsNullOrWhiteSpace([string]$message.hwnd) -or
                [string]::IsNullOrWhiteSpace([string]$message.apiReturn) -or
                -not [bool]$message.apiSucceeded -or
                [int]$message.lastError -ne 0 -or
                -not [bool]$message.wndProcObserved -or
                -not [bool]$message.handlerObserved -or
                [bool]$message.synthesized)
            {
                throw "Viewer Win32 message was not OS-routed: $($message.target)/$($message.message)."
            }
            $before = $message.before
            $after = $message.after
            $child = [string]$message.target -eq 'StormChild'
            $advanced = switch ([string]$message.message)
            {
                'WM_DPICHANGED(change)' {
                    [int]$after.scalingEvents -gt [int]$before.scalingEvents -and
                    [int]$after.resizeEvents -gt [int]$before.resizeEvents -and
                    [int]$after.dpi -ne [int]$before.dpi -and
                    (([int]$after.physicalWidth -ne [int]$before.physicalWidth) -or
                     ([int]$after.physicalHeight -ne [int]$before.physicalHeight))
                    break
                }
                'WM_DPICHANGED(restore)' {
                    [int]$after.scalingEvents -gt [int]$before.scalingEvents -and
                    [int]$after.resizeEvents -gt [int]$before.resizeEvents -and
                    [int]$after.dpi -ne [int]$before.dpi
                    break
                }
                { $_ -in @('WM_KILLFOCUS', 'WM_SETFOCUS') } {
                    if ($child)
                    {
                        [long]$after.nativeFocusEvents -gt [long]$before.nativeFocusEvents
                    }
                    else
                    {
                        [int]$after.focusEvents -gt [int]$before.focusEvents
                    }
                    break
                }
                'WM_MOUSEMOVE' {
                    if ($child)
                    {
                        [long]$after.nativePointerEvents -gt [long]$before.nativePointerEvents
                    }
                    else
                    {
                        [int]$after.pointerMoves -gt [int]$before.pointerMoves
                    }
                    break
                }
                { $_ -in @('WM_LBUTTONDOWN', 'WM_LBUTTONUP') } {
                    if ($child)
                    {
                        [long]$after.nativePointerEvents -gt [long]$before.nativePointerEvents
                    }
                    else
                    {
                        [int]$after.pointerButtons -gt [int]$before.pointerButtons
                    }
                    break
                }
                'WM_MOUSEWHEEL' {
                    if ($child)
                    {
                        [long]$after.nativeWheelEvents -gt [long]$before.nativeWheelEvents
                    }
                    else
                    {
                        [int]$after.wheelEvents -gt [int]$before.wheelEvents
                    }
                    break
                }
                { $_ -in @('WM_KEYDOWN', 'WM_KEYUP') } {
                    if ($child)
                    {
                        [long]$after.nativeKeyEvents -gt [long]$before.nativeKeyEvents
                    }
                    else
                    {
                        [int]$after.keyEvents -gt [int]$before.keyEvents
                    }
                    break
                }
                default { $false }
            }
            if (-not $advanced)
            {
                throw "Viewer Win32 counters did not advance for $($message.target)/$($message.message)."
            }
        }
        $changedDpi = @($input.win32Messages | Where-Object {
            $_.target -eq 'ViewerTopLevel' -and
            $_.message -eq 'WM_DPICHANGED(change)'
        })[0]
        $restoredDpi = @($input.win32Messages | Where-Object {
            $_.target -eq 'ViewerTopLevel' -and
            $_.message -eq 'WM_DPICHANGED(restore)'
        })[0]
        if ([int]$changedDpi.before.dpi -ne [int]$input.nativeDpiBefore -or
            [int]$changedDpi.after.dpi -ne [int]$input.nativeDpiObserved -or
            [int]$restoredDpi.before.dpi -ne [int]$input.nativeDpiObserved -or
            [int]$restoredDpi.after.dpi -ne [int]$input.nativeDpiAfter)
        {
            throw "Viewer Win32 DPI messages do not match the aggregate transition."
        }
    }
    foreach ($composition in @($artifact.compositions))
    {
        if ($composition.backend -notin @('D3D12', 'Vulkan') -or
            -not $composition.compositionHostVisible -or
            [long]$composition.successfulImports -lt 1 -or
            [long]$composition.successfulPresents -lt 1 -or
            [string]$composition.usedImageHandleType -ne 'D3D11TextureNtHandle' -or
            [string]$composition.synchronizationKind -ne 'KeyedMutex' -or
            [string]$composition.deviceLuid -notmatch '^[0-9A-Fa-f]{16}$' -or
            'D3D11TextureNtHandle' -notin @($composition.supportedImageHandleTypes))
        {
            throw "Viewer compositor evidence was not produced by a successful runtime import."
        }
    }
    foreach ($backend in @($artifact.states.backend | Select-Object -Unique))
    {
        if (@($artifact.windowOwnership | Where-Object backend -eq $backend).Count -eq 0)
        {
            throw "Viewer did not record HWND ownership for $backend."
        }
        if ($backend -in @('D3D12', 'Vulkan') -and
            @($artifact.compositions | Where-Object backend -eq $backend).Count -eq 0)
        {
            throw "Viewer did not record runtime composition imports for $backend."
        }
    }
    foreach ($ownership in @($artifact.windowOwnership))
    {
        $storm = $ownership.backend -eq 'Storm'
        if ([string]::IsNullOrWhiteSpace([string]$ownership.topLevelHwnd) -or
            [long]$ownership.topLevelProcessId -lt 1 -or
            [long]$ownership.topLevelThreadId -lt 1 -or
            [int]$ownership.staleLiveStormCount -ne 0 -or
            [int]$ownership.enumeratedStormCount -gt 1 -or
            [int]$ownership.visibleStormCount -gt 1 -or
            ($storm -and
             ([int]$ownership.enumeratedStormCount -ne 1 -or
              [int]$ownership.visibleStormCount -ne 1 -or
              [int]$ownership.liveKnownStormCount -ne 1 -or
              -not $ownership.stormIsWindow -or
              -not $ownership.stormIsVisible -or
              -not $ownership.stormParentWithinViewer -or
              [string]$ownership.stormClassName -ne 'OpenUsdStormNativeChild' -or
              [long]$ownership.stormProcessId -ne [long]$ownership.topLevelProcessId -or
              [long]$ownership.stormThreadId -ne [long]$ownership.topLevelThreadId -or
              [string]$ownership.expectedStormHwnd -ne [string]$ownership.observedStormHwnd -or
              $ownership.compositionHostVisible)) -or
            (-not $storm -and
             ([int]$ownership.enumeratedStormCount -ne 0 -or
              [int]$ownership.visibleStormCount -ne 0 -or
              [int]$ownership.liveKnownStormCount -ne 0 -or
              -not $ownership.compositionHostVisible)))
        {
            throw "Viewer HWND evidence contains duplicate, stale, or control-count-only ownership."
        }
    }
    foreach ($state in @($artifact.states))
    {
        if (-not $state.exactReferencePreserved -or
            [long]$state.revision -lt 1 -or
            [string]::IsNullOrWhiteSpace([string]$state.stageIdentifier) -or
            [string]::IsNullOrWhiteSpace([string]$state.phase) -or
            [int]$state.schedulerIdentity -eq 0 -or
            [int]$state.renderSourceIdentity -eq 0)
        {
            throw "Viewer state/shared-stage evidence is incomplete."
        }
    }
    Assert-ViewerCameraEvidence -Artifact $artifact
    $stageCamera = Assert-ViewerStageCameraEvidence -Artifact $artifact
    Assert-ViewerNativeNavigationEvidence -Artifact $artifact
    $stageCameraScenario =
        [string]$artifact.scenario -ceq 'stage-camera-backend-smoke'
    if (@($artifact.states.schedulerIdentity | Select-Object -Unique).Count -ne 1 -or
        @($artifact.states.renderSourceIdentity | Select-Object -Unique).Count -ne 1 -or
        @($artifact.states.stageIdentifier | Select-Object -Unique).Count -ne 1 -or
        (-not $stageCameraScenario -and
         @($artifact.states.timeCode | Select-Object -Unique).Count -ne 1) -or
        @($artifact.states.purposes | Select-Object -Unique).Count -ne 1 -or
        @($artifact.states.visibility | Select-Object -Unique).Count -ne 1 -or
        @($artifact.states.drawMode | Select-Object -Unique).Count -ne 1)
    {
        throw "Viewer switching did not preserve the measured shared-stage state."
    }
    if ($stageCameraScenario)
    {
        if ([string]::IsNullOrWhiteSpace($StageAssetPath) -or
            -not (Test-Path $StageAssetPath -PathType Leaf))
        {
            throw 'Viewer stage-camera evidence omitted its bound stage asset.'
        }
        $stageAssetHash = (Get-FileHash $StageAssetPath -Algorithm SHA256).Hash
        $sourceStage = @($identity.sourceBefore.files | Where-Object {
            ([string]$_.path).Replace('\', '/') -ceq
                'test-assets/viewer-stage-camera-smoke.usda'
        })
        $sourceScript = @($identity.sourceBefore.files | Where-Object {
            ([string]$_.path).Replace('\', '/') -ceq
                'eng/run-viewer-stage-camera-smoke.ps1'
        })
        if ($stageAssetHash -cne [string]$stageCamera.stageSha256 -or
            $sourceStage.Count -ne 1 -or
            [string]$sourceStage[0].sha256 -cne $stageAssetHash -or
            $sourceScript.Count -ne 1)
        {
            throw (
                'Viewer stage-camera asset/script provenance is not bound to ' +
                'the unchanged source identity.')
        }
    }
    elseif (-not [string]::IsNullOrWhiteSpace($StageAssetPath))
    {
        throw 'A non-stage-camera run supplied a stage-camera asset.'
    }
    $selectionStates = @($artifact.states | ForEach-Object {
        ConvertTo-Json -InputObject @($_.selection) -Compress
    } | Select-Object -Unique)
    if ($selectionStates.Count -ne 1)
    {
        throw "Viewer switching did not preserve the exact selection state."
    }
    foreach ($backend in $ExpectedBackends)
    {
        if ($backend -notin @($artifact.pixels.backend))
        {
            throw "Viewer evidence did not capture real $backend pixels."
        }
        if ($backend -notin @($artifact.cameraTransitions.backend))
        {
            throw "Viewer evidence did not capture the explicit $backend camera transition."
        }
    }
    if ('Storm' -in $ExpectedBackends -and
        @($artifact.nativeNavigation).Count -eq 0)
    {
        throw "Viewer evidence did not capture native Storm navigation."
    }
    $resources = $artifact.resources
    foreach ($counter in @(
        'childLive',
        'managedStorm',
        'nativeStorm',
        'managedSilk',
        'nativeSilk',
        'managedPages',
        'nativePages',
        'gpuScenes',
        'gpuMeshes'))
    {
        if ([long]$resources.$counter -ne 0)
        {
            throw "Viewer resource counter '$counter' did not return to zero."
        }
    }
    if (-not $resources.contextLossSimulated -and
        [long]$resources.abandonedStorm -ne 0)
    {
        throw "Viewer reported an abandoned Storm engine without simulated context loss."
    }
    $quarantineScenario = $artifact.scenario -eq 'renderer-retired-kind-quarantine'
    $quarantine = $artifact.cleanupQuarantine
    if ($quarantineScenario -ne ($null -ne $quarantine))
    {
        throw "Viewer cleanup-quarantine evidence does not match its scenario."
    }
    if ($quarantineScenario)
    {
        $blocked = $quarantine.blockedWindowOwnership
        if ([string]$quarantine.backend -ne 'Storm' -or
            [int]$quarantine.retiredBefore -ne 0 -or
            [int]$quarantine.retiredWhileBlocked -ne 1 -or
            [int]$quarantine.retiredAfterRecovery -ne 0 -or
            [int]$quarantine.candidateAfterManual -ne [int]$quarantine.candidateBefore -or
            [int]$quarantine.candidateAfterAutomatic -ne [int]$quarantine.candidateBefore -or
            [int]$quarantine.candidateAfterRecovery -ne
                ([int]$quarantine.candidateBefore + 1) -or
            [int]$quarantine.factoryAfterManual -ne [int]$quarantine.factoryBefore -or
            [int]$quarantine.factoryAfterAutomatic -ne [int]$quarantine.factoryBefore -or
            [int]$quarantine.factoryAfterRecovery -ne
                ([int]$quarantine.factoryBefore + 1) -or
            [int]$quarantine.attachAfterManual -ne [int]$quarantine.attachBefore -or
            [int]$quarantine.attachAfterAutomatic -ne [int]$quarantine.attachBefore -or
            [int]$quarantine.attachAfterRecovery -ne
                ([int]$quarantine.attachBefore + 1) -or
            [long]$quarantine.childLiveBefore -ne 1 -or
            [long]$quarantine.childPeakBefore -ne 1 -or
            [long]$quarantine.childLiveWhileBlocked -ne 1 -or
            [long]$quarantine.childPeakWhileBlocked -ne 1 -or
            [long]$quarantine.childLiveAfterRecovery -ne 1 -or
            [long]$quarantine.childPeakAfterRecovery -ne 1 -or
            [string]$quarantine.manualFailure -ne 'CleanupPending' -or
            [string]$quarantine.manualDiagnosticCode -ne
                'manager.backend_cleanup_pending' -or
            -not $quarantine.automaticSucceeded -or
            -not $quarantine.automaticSkippedQuarantinedKind -or
            -not $quarantine.cleanupRecovered -or
            -not $quarantine.reactivatedAfterRecovery -or
            [int]$blocked.enumeratedStormCount -ne 1 -or
            [int]$blocked.visibleStormCount -ne 0 -or
            [int]$blocked.liveKnownStormCount -ne 1 -or
            [int]$blocked.staleLiveStormCount -ne 0 -or
            -not $blocked.stormIsWindow -or
            $blocked.stormIsVisible -or
            -not $blocked.stormParentWithinViewer -or
            [string]$blocked.stormClassName -ne 'OpenUsdStormNativeChild' -or
            -not $blocked.compositionHostVisible)
        {
            throw "Viewer retained-kind quarantine evidence is incomplete."
        }
    }

    $statuses = if (Test-Path $statusPath)
    {
        Get-Content $statusPath -Raw
    }
    else
    {
        ''
    }
    if ($statuses -notmatch
        'Viewer runtime compositor observed: backend=(?:D3D12|Vulkan); image=D3D11TextureNtHandle; sync=KeyedMutex; luid=[0-9A-Fa-f]{16}; imports=\d+; presents=\d+')
    {
        throw "Viewer did not record observed shell and Storm child ABI diagnostics."
    }
    foreach ($backend in @($artifact.cameraTransitions.backend))
    {
        if ($statuses -notmatch
            "Viewer explicit camera evidence: backend=$([regex]::Escape([string]$backend)); .*exactReferences=True;")
        {
            throw "Viewer status did not record explicit-camera evidence for $backend."
        }
    }
    if (@($artifact.nativeNavigation).Count -gt 0 -and
        $statuses -notmatch
            'Viewer native navigation: sequence=\d+->\d+; camera=[0-9A-Fa-f]{64}->[0-9A-Fa-f]{64}; pixel=[0-9A-Fa-f]{64}->[0-9A-Fa-f]{64}; routedDuplicates=0')
    {
        throw "Viewer status did not record native Storm navigation evidence."
    }
    foreach ($pattern in $ExpectedStatusPatterns)
    {
        if ($statuses -notmatch $pattern)
        {
            throw "Viewer status did not contain required measured pattern: $pattern"
        }
    }
    $ownershipStatus = @(
        [regex]::Matches(
            $statuses,
            'Viewer HWND ownership: phase=[^;]+; backend=(Storm|D3D12|Vulkan); .*live=(\d+); visible=(\d+); stale=(\d+);'))
    if ($ownershipStatus.Count -eq 0 -or
        @($ownershipStatus | Where-Object { $_.Groups[4].Value -ne '0' }).Count -ne 0)
    {
        throw "Viewer status did not report measured HWND ownership transitions."
    }
    $pixelPaths = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($record in $pixelArtifactRecords)
    {
        [void]$pixelPaths.Add([string]$record.path)
    }
    $cameraTransitionRecords = @(
        foreach ($transition in @($artifact.cameraTransitions))
        {
            $automaticPath = (Resolve-ViewerEvidencePath `
                -EvidenceRoot $EvidenceRoot `
                -Path ([string]$transition.automaticPixelArtifact)).relativePath
            $explicitPath = (Resolve-ViewerEvidencePath `
                -EvidenceRoot $EvidenceRoot `
                -Path ([string]$transition.explicitPixelArtifact)).relativePath
            $restoredPath = (Resolve-ViewerEvidencePath `
                -EvidenceRoot $EvidenceRoot `
                -Path ([string]$transition.restoredPixelArtifact)).relativePath
            [ordered]@{
                backend = [string]$transition.backend
                automaticCameraSignature =
                    [string]$transition.automaticCameraSignature
                explicitCameraSignature =
                    [string]$transition.explicitCameraSignature
                restoredCameraSignature =
                    [string]$transition.restoredCameraSignature
                automaticPixelSha256 = [string]$transition.automaticPixelSha256
                explicitPixelSha256 = [string]$transition.explicitPixelSha256
                restoredPixelSha256 = [string]$transition.restoredPixelSha256
                automaticPixelPath = $automaticPath
                explicitPixelPath = $explicitPath
                restoredPixelPath = $restoredPath
                exactReferencesPreserved =
                    [bool]$transition.exactReferencesPreserved
                latestRequestedRevision =
                    [uint64]$transition.latestRequestedRevision
                latestRequestedCameraSignature =
                    [string]$transition.latestRequestedCameraSignature
                latestRenderedCameraSignature =
                    [string]$transition.latestRenderedCameraSignature
                asyncCoalescingValidated =
                    [bool]$transition.asyncCoalescingValidated
            }
        })
    $nativeNavigationRecords = @(
        foreach ($navigation in @($artifact.nativeNavigation))
        {
            $beforePath = (Resolve-ViewerEvidencePath `
                -EvidenceRoot $EvidenceRoot `
                -Path ([string]$navigation.pixelBeforeArtifact)).relativePath
            $afterPath = (Resolve-ViewerEvidencePath `
                -EvidenceRoot $EvidenceRoot `
                -Path ([string]$navigation.pixelAfterArtifact)).relativePath
            [ordered]@{
                backend = [string]$navigation.backend
                phase = [string]$navigation.phase
                snapshotApi = [string]$navigation.snapshotApi
                deliveryApi = [string]$navigation.deliveryApi
                stormChildAbiVersion = [int]$navigation.stormChildAbiVersion
                gesture = [string]$navigation.gesture
                sequenceBefore = [uint64]$navigation.sequenceBefore
                sequenceAfter = [uint64]$navigation.sequenceAfter
                avaloniaRoutedEvents = [int]$navigation.avaloniaRoutedEvents
                beforeCameraSignature =
                    [string]$navigation.cameraBeforeSignature
                afterCameraSignature =
                    [string]$navigation.cameraAfterSignature
                beforePixelSha256 = [string]$navigation.pixelBeforeSha256
                afterPixelSha256 = [string]$navigation.pixelAfterSha256
                beforePixelPath = $beforePath
                afterPixelPath = $afterPath
            }
        })
    $stageCameraRecord = $null
    if ($null -ne $stageCamera)
    {
        $stageAssetRecord = Get-ViewerEvidenceFileRecord `
            -EvidenceRoot $EvidenceRoot `
            -Path $StageAssetPath
        $mapAutomatic = {
            param([Parameter(Mandatory)][object]$Entry)

            [ordered]@{
                backend = [string]$Entry.backend
                phase = [string]$Entry.phase
                timeCode = [double]$Entry.timeCode
                stateRevision = [uint64]$Entry.stateRevision
                cameraSignature = [string]$Entry.cameraSignature
                nativeCameraSignature = [string]$Entry.nativeCameraSignature
                pixelSha256 = [string]$Entry.pixelSha256
                pixelPath = (Resolve-ViewerEvidencePath `
                    -EvidenceRoot $EvidenceRoot `
                    -Path ([string]$Entry.pixelArtifact)).relativePath
            }
        }
        $mapFrames = {
            param([Parameter(Mandatory)][object[]]$Frames)

            @(
                foreach ($frame in $Frames)
                {
                    [ordered]@{
                        backend = [string]$frame.backend
                        phase = [string]$frame.phase
                        timeCode = [double]$frame.timeCode
                        stateRevision = [uint64]$frame.stateRevision
                        cameraSignature = [string]$frame.cameraSignature
                        nativeCameraSignature =
                            [string]$frame.nativeCameraSignature
                        pixelSha256 = [string]$frame.pixelSha256
                        pixelPath = (Resolve-ViewerEvidencePath `
                            -EvidenceRoot $EvidenceRoot `
                            -Path ([string]$frame.pixelArtifact)).relativePath
                        exactReferencePreserved =
                            [bool]$frame.exactReferencePreserved
                        latestRequestedRevision =
                            [uint64]$frame.latestRequestedRevision
                        latestRequestedCameraSignature =
                            [string]$frame.latestRequestedCameraSignature
                        latestRenderedCameraSignature =
                            [string]$frame.latestRenderedCameraSignature
                    }
                })
        }
        $stageCameraRecord = [ordered]@{
            source = [string]$stageCamera.source
            stageIdentifier = [string]$stageCamera.stageIdentifier
            stageSha256 = [string]$stageCamera.stageSha256
            stageAssetPath = [string]$stageAssetRecord.path
            stageAssetLength = [int64]$stageAssetRecord.length
            cameraPath = [string]$stageCamera.cameraPath
            initialTimeCode = [double]$stageCamera.initialTimeCode
            sampledTimeCode = [double]$stageCamera.sampledTimeCode
            initialSnapshotSha256 = [string]$stageCamera.initialSnapshotSha256
            sampledSnapshotSha256 = [string]$stageCamera.sampledSnapshotSha256
            automaticBefore = & $mapAutomatic $stageCamera.automaticBefore
            initialFrames = & $mapFrames @($stageCamera.initialFrames)
            sampledFrames = & $mapFrames @($stageCamera.sampledFrames)
            automaticRestored = & $mapAutomatic $stageCamera.automaticRestored
            exactStatePreservedAcrossBackends =
                [bool]$stageCamera.exactStatePreservedAcrossBackends
        }
    }
    $scenarioRoot = Split-Path $EvidencePath -Parent
    $runArtifactRecords = @(Get-ChildItem $scenarioRoot -File -Recurse |
        Sort-Object FullName |
        ForEach-Object {
            $record = Get-ViewerEvidenceFileRecord `
                -EvidenceRoot $EvidenceRoot `
                -Path $_.FullName
            if (-not $pixelPaths.Contains([string]$record.path))
            {
                $record
            }
        })
    $artifactRoot = (Resolve-ViewerEvidencePath `
        -EvidenceRoot $EvidenceRoot `
        -Path $scenarioRoot `
        -Directory).relativePath
    return [ordered]@{
        run = $RunName
        schemaVersion = [int]$artifact.schemaVersion
        scenario = $artifact.scenario
        artifactRoot = $artifactRoot
        sourceHash = $identity.sourceBefore.sha256
        binaryCount = @($identity.binariesBefore.files).Count
        loadedArtifactCount = @($artifact.loadedArtifacts).Count
        stormChildAbiVersion = [int]$artifact.stormChildAbiVersion
        runtimeCompositor = $artifact.runtimeCompositor
        compositionCount = @($artifact.compositions).Count
        ownershipCount = @($artifact.windowOwnership).Count
        stateCount = @($artifact.states).Count
        cameraTransitionCount = $cameraTransitionRecords.Count
        nativeNavigationCount = $nativeNavigationRecords.Count
        pixelCount = @($artifact.pixels).Count
        inputCount = @($artifact.inputs).Count
        cleanupQuarantine = $artifact.cleanupQuarantine
        stageCamera = $stageCameraRecord
        cameraTransitions = $cameraTransitionRecords
        nativeNavigation = $nativeNavigationRecords
        pixelHashes = @($artifact.pixels.sha256)
        artifacts = $runArtifactRecords
        pixelArtifacts = $pixelArtifactRecords
        resources = $artifact.resources
    }
}

function Invoke-ViewerEvidenceRun
{
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [string]$Scenario,
        [switch]$SwitchSoak,
        [int]$RunSwitchCount = 3,
        [int]$RunSeconds = 1,
        [string]$StagePath = (Join-Path $repoRoot 'test-assets/minimal.usda'),
        [string]$CameraPath,
        [hashtable]$Environment = @{},
        [string[]]$ExpectedBackends = @(),
        [string[]]$ExpectedStatusPatterns = @())

    $scenarioRoot = Join-Path $runsRoot $Name
    New-Item -ItemType Directory -Force -Path $scenarioRoot | Out-Null
    $evidencePath = Join-Path $scenarioRoot 'switching-evidence.json'
    $identityPath = Join-Path $scenarioRoot 'identity.json'
    $statusPath = Join-Path $scenarioRoot 'viewer-status.txt'
    $saved = @{}
    try
    {
        foreach ($key in $Environment.Keys)
        {
            $saved[$key] = [Environment]::GetEnvironmentVariable($key)
            [Environment]::SetEnvironmentVariable($key, $Environment[$key])
        }
        $arguments = @(
            '-NoProfile',
            '-File', $runViewer,
            '-Rid', 'win-x64',
            '-StagePath', $StagePath,
            '-OutputPath', $runtimeRoot,
            '-EvidencePath', $evidencePath,
            '-EvidenceScenario', $Scenario,
            '-IdentityManifestPath', $identityPath)
        if (-not [string]::IsNullOrWhiteSpace($CameraPath))
        {
            $arguments += @('-EvidenceCameraPath', $CameraPath)
        }
        if ($script:runtimePrepared)
        {
            $arguments += '-ReusePublishedOutput'
        }
        if ($SwitchSoak)
        {
            $arguments += @(
                '-RendererSwitchSoak',
                '-SwitchCount', $RunSwitchCount,
                '-SwitchSoakSeconds', $RunSeconds)
        }
        else
        {
            $arguments += @('-SmokeSeconds', 600)
        }
        & pwsh @arguments | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0)
        {
            throw "Viewer evidence run '$Name' exited with $LASTEXITCODE."
        }
        $script:runtimePrepared = $true
        foreach ($fileName in @(
            'viewer-status.txt',
            'viewer.log',
            'viewer.stdout.log',
            'viewer.stderr.log'))
        {
            $source = Join-Path $runtimeRoot $fileName
            if (Test-Path $source)
            {
                Copy-Item $source (Join-Path $scenarioRoot $fileName) -Force
            }
        }
        $stageAssetArtifactPath = $null
        if (-not [string]::IsNullOrWhiteSpace($CameraPath))
        {
            $stageAssetRoot = Join-Path $scenarioRoot 'stage'
            New-Item -ItemType Directory -Force -Path $stageAssetRoot | Out-Null
            $stageAssetArtifactPath = Join-Path `
                $stageAssetRoot `
                ([IO.Path]::GetFileName($StagePath))
            Copy-Item $StagePath $stageAssetArtifactPath -Force
        }
    }
    finally
    {
        foreach ($key in $Environment.Keys)
        {
            [Environment]::SetEnvironmentVariable($key, $saved[$key])
        }
    }
    Assert-ViewerRunEvidence `
        -RunRoot $runtimeRoot `
        -EvidencePath $evidencePath `
        -IdentityPath $identityPath `
        -StatusPath $statusPath `
        -RunName $Name `
        -EvidenceRoot $outputRoot `
        -StageAssetPath $stageAssetArtifactPath `
        -ExpectedBackends $ExpectedBackends `
        -ExpectedStatusPatterns $ExpectedStatusPatterns
}

$runs = [System.Collections.Generic.List[object]]::new()
$runs.Add((Invoke-ViewerEvidenceRun `
    -Name 'switch-soak' `
    -Scenario 'switch-soak' `
    -SwitchSoak `
    -RunSwitchCount $SwitchCount `
    -RunSeconds $SurvivalSeconds `
    -ExpectedBackends @('Storm', 'D3D12', 'Vulkan') `
    -ExpectedStatusPatterns @(
        'Viewer live edit observed: revision=\d+->\d+',
        "Viewer switch soak passed: switches=$SwitchCount;",
        'Viewer final resources: child=0;')))

$runs.Add((Invoke-ViewerEvidenceRun `
    -Name 'stage-camera-backend-smoke' `
    -Scenario 'stage-camera-backend-smoke' `
    -StagePath $stageCameraStagePath `
    -CameraPath '/World/CameraRig/Offset/ShotCamera' `
    -Environment @{
        OPENUSD_RENDERER = 'Storm'
    } `
    -ExpectedBackends @('Storm', 'D3D12', 'Vulkan') `
    -ExpectedStatusPatterns @(
        'Viewer stage camera smoke passed: path=/World/CameraRig/Offset/ShotCamera; .*exactState=True',
        'Viewer final resources: child=0;')))

for ($index = 1; $index -le $FreshProcessCount; $index++)
{
    $runs.Add((Invoke-ViewerEvidenceRun `
        -Name "fresh-$index" `
        -Scenario "fresh-$index" `
        -SwitchSoak `
        -RunSwitchCount 3 `
        -RunSeconds 1 `
        -ExpectedBackends @('Storm', 'D3D12', 'Vulkan') `
        -ExpectedStatusPatterns @(
            'Viewer live edit observed: revision=\d+->\d+',
            'Viewer switch soak passed: switches=3;',
            'Viewer final resources: child=0;')))
}

$runs.Add((Invoke-ViewerEvidenceRun `
    -Name 'storm-init-to-d3d12' `
    -Scenario 'storm-init-to-d3d12' `
    -Environment @{
        OPENUSD_FORCE_STORM_FAILURE = '1'
        OPENUSD_RENDERER = 'Auto'
    } `
    -ExpectedBackends @('D3D12') `
    -ExpectedStatusPatterns @(
        'Renderer initialization diagnostic: Storm; VIEWER_BACKEND_FORCED_INITIALIZATION_FAILURE;',
        'Renderer initialization: hdSilk / Direct3D 12')))

$runs.Add((Invoke-ViewerEvidenceRun `
    -Name 'storm-d3d12-to-vulkan' `
    -Scenario 'storm-d3d12-to-vulkan' `
    -Environment @{
        OPENUSD_FORCE_STORM_FAILURE = '1'
        OPENUSD_FORCE_D3D12_FAILURE = '1'
        OPENUSD_RENDERER = 'Auto'
    } `
    -ExpectedBackends @('Vulkan') `
    -ExpectedStatusPatterns @(
        'Renderer initialization diagnostic: Storm; VIEWER_BACKEND_FORCED_INITIALIZATION_FAILURE;',
        'Renderer initialization diagnostic: D3D12; VIEWER_BACKEND_FORCED_INITIALIZATION_FAILURE;',
        'Renderer initialization: hdSilk / Vulkan')))

$runs.Add((Invoke-ViewerEvidenceRun `
    -Name 'device-loss' `
    -Scenario 'device-loss' `
    -Environment @{
        OPENUSD_FORCE_STORM_UNAVAILABLE = '1'
        OPENUSD_RENDERER = 'Auto'
    } `
    -ExpectedBackends @('D3D12', 'Vulkan') `
    -ExpectedStatusPatterns @(
        'VIEWER_BACKEND_FORCED_UNAVAILABLE',
        'device-loss fallback')))

$runs.Add((Invoke-ViewerEvidenceRun `
    -Name 'storm-destroy-cleanup-retry' `
    -Scenario 'storm-destroy-cleanup-retry' `
    -Environment @{
        OPENUSD_FORCE_STORM_DESTROY_FAILURE = '1'
        OPENUSD_VIEWER_SWITCH_TO = 'D3D12'
        OPENUSD_RENDERER = 'Storm'
    } `
    -ExpectedBackends @('D3D12') `
    -ExpectedStatusPatterns @(
        'manager.previous_backend_cleanup_failed',
        'Renderer switch: hdSilk / Direct3D 12;.*retiredCleanup=1',
        'Renderer frame rendered: hdSilk / Direct3D 12;.*retiredCleanup=0',
        'Viewer HWND ownership: phase=initial-camera-automatic-before-after; backend=D3D12;.*live=0; visible=0; stale=0;.*retiredCleanup=0',
        'Viewer final resources: child=0;')))

$runs.Add((Invoke-ViewerEvidenceRun `
    -Name 'renderer-retired-kind-quarantine' `
    -Scenario 'renderer-retired-kind-quarantine' `
    -Environment @{
        OPENUSD_FORCE_STORM_DESTROY_FAILURE_PERSISTENT = '1'
        OPENUSD_RENDERER = 'Storm'
    } `
    -ExpectedBackends @('Storm', 'D3D12') `
    -ExpectedStatusPatterns @(
        'manager.previous_backend_cleanup_failed',
        'manager.backend_cleanup_pending',
        'Viewer retired-kind quarantine passed: kind=Storm; retired=0->1->0;',
        'manual=CleanupPending; automaticSkipped=True',
        'Viewer final resources: child=0;')))

$expectedScenarioCount = $FreshProcessCount + 7
if ($FreshProcessCount -ne 15 -or $runs.Count -ne $expectedScenarioCount)
{
    throw (
        "Viewer aggregate must contain exactly 15 fresh processes and " +
        "$expectedScenarioCount total scenarios.")
}

$sourceHashes = @($runs.sourceHash | Select-Object -Unique)
if ($sourceHashes.Count -ne 1)
{
    throw "Fresh processes did not run against one unchanged relevant source identity."
}
$runSchemaVersions = @($runs.schemaVersion | Select-Object -Unique)
if ($runSchemaVersions.Count -ne 1)
{
    throw "Viewer runs did not use one evidence schema."
}

$aggregate = [ordered]@{
    schemaVersion = [int]$runSchemaVersions[0]
    status = 'passed'
    completedAt = [DateTimeOffset]::UtcNow.ToString('O')
    sourceHash = $sourceHashes[0]
    switchCount = $SwitchCount
    survivalSeconds = $SurvivalSeconds
    freshProcessCount = $FreshProcessCount
    scenarioCount = $runs.Count
    runs = $runs
}
Assert-ViewerAggregateEvidence `
    -Aggregate ([pscustomobject]$aggregate) `
    -ExpectedRunCount $runs.Count `
    -EvidenceRoot $outputRoot
$aggregatePath = Join-Path $outputRoot 'storm-native-child-evidence.json'
$aggregate | ConvertTo-Json -Depth 12 | Set-Content $aggregatePath
$writtenAggregate = Get-Content $aggregatePath -Raw | ConvertFrom-Json -Depth 20
Assert-ViewerAggregateEvidence `
    -Aggregate $writtenAggregate `
    -ExpectedRunCount $runs.Count `
    -EvidenceRoot $outputRoot
Write-Output "Storm native child validation passed: $aggregatePath"
