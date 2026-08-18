#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

$script:ViewerEvidenceSchemaVersion = 8

function Get-ViewerEvidenceProperty
{
    param(
        [Parameter(Mandatory)]
        [object]$Value,
        [Parameter(Mandatory)]
        [string]$Name,
        [switch]$Optional)

    $property = if ($Value -is [System.Collections.IDictionary])
    {
        if ($Value.Contains($Name)) { $Value[$Name] } else { $null }
    }
    else
    {
        $Value.PSObject.Properties[$Name]?.Value
    }
    if ($null -eq $property -and -not $Optional)
    {
        throw "Viewer evidence is missing required property '$Name'."
    }
    $property
}

function Assert-ViewerCameraStateEvidence
{
    param(
        [Parameter(Mandatory)]
        [object]$State)

    $mode = [string](Get-ViewerEvidenceProperty $State 'cameraMode')
    $expectedMode = switch ($mode)
    {
        'Automatic' { 0; break }
        'Matrices' { 1; break }
        default { throw "Viewer camera mode is invalid: $mode" }
    }
    $payloadText = [string](Get-ViewerEvidenceProperty $State 'cameraPayload')
    try
    {
        $payload = [Convert]::FromBase64String($payloadText)
    }
    catch
    {
        throw 'Viewer camera payload is not canonical base64.'
    }
    $cameraPayloadLength = 524
    $clipPlaneCountOffset = 260
    $clipPlaneOffset = 268
    if ($payload.Length -ne $cameraPayloadLength)
    {
        throw "Viewer camera payload must contain exactly $cameraPayloadLength bytes, got $($payload.Length)."
    }
    $encodedMode = [uint32]$payload[0] -bor
        ([uint32]$payload[1] -shl 8) -bor
        ([uint32]$payload[2] -shl 16) -bor
        ([uint32]$payload[3] -shl 24)
    if ($encodedMode -ne $expectedMode)
    {
        throw 'Viewer camera payload mode does not match cameraMode.'
    }
    $clipPlaneCount = [BitConverter]::ToUInt32($payload, $clipPlaneCountOffset)
    if ($clipPlaneCount -gt 8)
    {
        throw 'Viewer camera payload contains too many clip planes.'
    }
    for ($offset = 4; $offset -lt $clipPlaneCountOffset; $offset += 8)
    {
        $value = [BitConverter]::ToDouble($payload, $offset)
        if (-not [double]::IsFinite($value))
        {
            throw 'Viewer camera payload contains a non-finite matrix value.'
        }
    }
    $clipPlaneEnd = $clipPlaneOffset + ($clipPlaneCount * 4 * 8)
    for ($offset = $clipPlaneOffset; $offset -lt $clipPlaneEnd; $offset += 8)
    {
        $value = [BitConverter]::ToDouble($payload, $offset)
        if (-not [double]::IsFinite($value))
        {
            throw 'Viewer camera payload contains a non-finite clip plane value.'
        }
    }
    $signature = [string](Get-ViewerEvidenceProperty $State 'cameraSignature')
    $actualSignature = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($payload))
    $nativeSignature = [string](
        Get-ViewerEvidenceProperty $State 'nativeCameraSignature')
    if ($signature -notmatch '^[0-9A-Fa-f]{64}$' -or
        $actualSignature -cne $signature.ToUpperInvariant() -or
        $nativeSignature -notmatch '^[0-9A-Fa-f]{16}$')
    {
        throw 'Viewer camera signatures do not match the canonical payload.'
    }
}

function Assert-ViewerCameraEvidence
{
    param(
        [Parameter(Mandatory)]
        [object]$Artifact)

    $states = @((Get-ViewerEvidenceProperty $Artifact 'states'))
    $pixels = @((Get-ViewerEvidenceProperty $Artifact 'pixels'))
    $transitions = @((Get-ViewerEvidenceProperty $Artifact 'cameraTransitions'))
    if ($states.Count -eq 0 -or $pixels.Count -eq 0 -or $transitions.Count -eq 0)
    {
        throw 'Viewer camera evidence is incomplete.'
    }
    foreach ($state in $states)
    {
        Assert-ViewerCameraStateEvidence $state
    }
    $transitionBackends = @($transitions.backend | Select-Object -Unique)
    if ($transitionBackends.Count -ne $transitions.Count)
    {
        throw 'Viewer camera evidence contains duplicate backend transitions.'
    }
    foreach ($backend in @(
        $states.backend |
            Where-Object { $_ -in @('Storm', 'D3D12', 'Vulkan') } |
            Select-Object -Unique))
    {
        if ($backend -notin $transitionBackends)
        {
            throw "Viewer camera evidence is missing the $backend transition."
        }
    }
    foreach ($transition in $transitions)
    {
        $backend = [string]$transition.backend
        $before = @($states | Where-Object {
            $_.backend -eq $backend -and
            $_.phase -eq [string]$transition.automaticBeforePhase
        })
        $explicit = @($states | Where-Object {
            $_.backend -eq $backend -and
            $_.phase -eq [string]$transition.explicitPhase
        })
        $restored = @($states | Where-Object {
            $_.backend -eq $backend -and
            $_.phase -eq [string]$transition.automaticRestoredPhase
        })
        $beforePixel = @($pixels | Where-Object {
            $_.sha256 -eq [string]$transition.automaticPixelSha256 -and
            $_.artifact -eq [string]$transition.automaticPixelArtifact
        })
        $explicitPixel = @($pixels | Where-Object {
            $_.sha256 -eq [string]$transition.explicitPixelSha256 -and
            $_.artifact -eq [string]$transition.explicitPixelArtifact
        })
        $restoredPixel = @($pixels | Where-Object {
            $_.sha256 -eq [string]$transition.restoredPixelSha256 -and
            $_.artifact -eq [string]$transition.restoredPixelArtifact
        })
        if ($before.Count -ne 1 -or
            $explicit.Count -ne 1 -or
            $restored.Count -ne 1 -or
            $beforePixel.Count -ne 1 -or
            $explicitPixel.Count -ne 1 -or
            $restoredPixel.Count -ne 1 -or
            [string]$before[0].cameraMode -ne 'Automatic' -or
            [string]$explicit[0].cameraMode -ne 'Matrices' -or
            [string]$restored[0].cameraMode -ne 'Automatic' -or
            [string]$before[0].cameraSignature -cne
                [string]$transition.automaticCameraSignature -or
            [string]$explicit[0].cameraSignature -cne
                [string]$transition.explicitCameraSignature -or
            [string]$restored[0].cameraSignature -cne
                [string]$transition.restoredCameraSignature -or
            [string]$before[0].cameraPayload -cne [string]$restored[0].cameraPayload -or
            [string]$before[0].cameraSignature -cne
                [string]$restored[0].cameraSignature -or
            [string]$before[0].cameraPayload -ceq [string]$explicit[0].cameraPayload -or
            [string]$before[0].cameraSignature -ceq
                [string]$explicit[0].cameraSignature -or
            [string]$beforePixel[0].backend -cne $backend -or
            [string]$explicitPixel[0].backend -cne $backend -or
            [string]$restoredPixel[0].backend -cne $backend -or
            [string]$beforePixel[0].sha256 -ceq [string]$explicitPixel[0].sha256 -or
            [string]$restoredPixel[0].sha256 -ceq [string]$explicitPixel[0].sha256 -or
            -not [bool]$transition.exactReferencesPreserved -or
            -not [bool]$before[0].exactReferencePreserved -or
            -not [bool]$explicit[0].exactReferencePreserved -or
            -not [bool]$restored[0].exactReferencePreserved)
        {
            throw "Viewer explicit-camera evidence is invalid for $backend."
        }
        if ($backend -eq 'Storm')
        {
            if (-not [bool]$transition.asyncCoalescingValidated -or
                [uint64]$transition.latestRequestedRevision -ne
                    [uint64]$explicit[0].revision -or
                [string]$transition.latestRequestedCameraSignature -cne
                    [string]$explicit[0].nativeCameraSignature -or
                [string]$transition.latestRenderedCameraSignature -cne
                    [string]$explicit[0].nativeCameraSignature)
            {
                throw 'Storm latest requested/rendered camera diagnostics are invalid.'
            }
        }
        elseif ([bool]$transition.asyncCoalescingValidated -or
            [uint64]$transition.latestRequestedRevision -ne 0 -or
            -not [string]::IsNullOrEmpty(
                [string]$transition.latestRequestedCameraSignature) -or
            -not [string]::IsNullOrEmpty(
                [string]$transition.latestRenderedCameraSignature))
        {
            throw "Non-Storm camera evidence contains native child diagnostics for $backend."
        }
    }
}

function Assert-ViewerStageCameraEvidence
{
    param(
        [Parameter(Mandatory)]
        [object]$Artifact)

    $scenario = [string](Get-ViewerEvidenceProperty $Artifact 'scenario')
    $required = $scenario -ceq 'stage-camera-backend-smoke'
    $stageCamera = Get-ViewerEvidenceProperty $Artifact 'stageCamera' -Optional
    if ($required -ne ($null -ne $stageCamera))
    {
        throw 'Viewer authored stage-camera evidence does not match its scenario.'
    }
    if (-not $required)
    {
        return $null
    }

    $states = @((Get-ViewerEvidenceProperty $Artifact 'states'))
    $pixels = @((Get-ViewerEvidenceProperty $Artifact 'pixels'))
    $source = [string](Get-ViewerEvidenceProperty $stageCamera 'source')
    $stageIdentifier = [string](
        Get-ViewerEvidenceProperty $stageCamera 'stageIdentifier')
    $stageSha256 = [string](
        Get-ViewerEvidenceProperty $stageCamera 'stageSha256')
    $cameraPath = [string](Get-ViewerEvidenceProperty $stageCamera 'cameraPath')
    $initialTime = [double](
        Get-ViewerEvidenceProperty $stageCamera 'initialTimeCode')
    $sampledTime = [double](
        Get-ViewerEvidenceProperty $stageCamera 'sampledTimeCode')
    $initialSnapshot = [string](
        Get-ViewerEvidenceProperty $stageCamera 'initialSnapshotSha256')
    $sampledSnapshot = [string](
        Get-ViewerEvidenceProperty $stageCamera 'sampledSnapshotSha256')
    $initialFrames = @(
        (Get-ViewerEvidenceProperty $stageCamera 'initialFrames'))
    $sampledFrames = @(
        (Get-ViewerEvidenceProperty $stageCamera 'sampledFrames'))
    $requiredBackends = @('Storm', 'D3D12', 'Vulkan')
    if ($source -cne 'ViewerSchedulerStageCameraSource' -or
        [string]::IsNullOrWhiteSpace($stageIdentifier) -or
        $stageSha256 -notmatch '^[0-9A-Fa-f]{64}$' -or
        [string]::IsNullOrWhiteSpace($cameraPath) -or
        -not $cameraPath.StartsWith('/') -or
        -not [double]::IsFinite($initialTime) -or
        -not [double]::IsFinite($sampledTime) -or
        $initialTime -eq $sampledTime -or
        $initialSnapshot -notmatch '^[0-9A-Fa-f]{64}$' -or
        $sampledSnapshot -notmatch '^[0-9A-Fa-f]{64}$' -or
        $initialSnapshot -ceq $sampledSnapshot -or
        -not [bool](Get-ViewerEvidenceProperty `
            $stageCamera 'exactStatePreservedAcrossBackends') -or
        $initialFrames.Count -ne 3 -or
        $sampledFrames.Count -ne 3 -or
        @($initialFrames.backend | Select-Object -Unique).Count -ne 3 -or
        @($sampledFrames.backend | Select-Object -Unique).Count -ne 3 -or
        @($requiredBackends | Where-Object { $_ -notin $initialFrames.backend }).Count -ne 0 -or
        @($requiredBackends | Where-Object { $_ -notin $sampledFrames.backend }).Count -ne 0)
    {
        throw 'Viewer authored stage-camera provenance is incomplete.'
    }
    foreach ($state in $states)
    {
        if ([string]$state.stageIdentifier -cne $stageIdentifier -or
            @($state.selection).Count -ne 1 -or
            [string]$state.selection[0] -cne $cameraPath)
        {
            throw 'Viewer stage-camera selection or scheduler stage identity changed.'
        }
    }

    $validateAutomatic = {
        param([Parameter(Mandatory)][object]$Entry)

        $backend = [string](Get-ViewerEvidenceProperty $Entry 'backend')
        $phase = [string](Get-ViewerEvidenceProperty $Entry 'phase')
        $stateMatches = @($states | Where-Object {
            [string]$_.backend -ceq $backend -and
            [string]$_.phase -ceq $phase
        })
        $pixelHash = [string](
            Get-ViewerEvidenceProperty $Entry 'pixelSha256')
        $pixelArtifact = [string](
            Get-ViewerEvidenceProperty $Entry 'pixelArtifact')
        $pixelMatches = @($pixels | Where-Object {
            [string]$_.sha256 -ceq $pixelHash -and
            [string]$_.artifact -ceq $pixelArtifact
        })
        if ($backend -cne 'Storm' -or
            $stateMatches.Count -ne 1 -or
            $pixelMatches.Count -ne 1 -or
            [double](Get-ViewerEvidenceProperty $Entry 'timeCode') -ne $initialTime -or
            [uint64](Get-ViewerEvidenceProperty $Entry 'stateRevision') -ne
                [uint64]$stateMatches[0].revision -or
            [string]$stateMatches[0].cameraMode -cne 'Automatic' -or
            [string](Get-ViewerEvidenceProperty $Entry 'cameraSignature') -cne
                [string]$stateMatches[0].cameraSignature -or
            [string](Get-ViewerEvidenceProperty $Entry 'nativeCameraSignature') -cne
                [string]$stateMatches[0].nativeCameraSignature -or
            [string]$pixelMatches[0].backend -cne $backend)
        {
            throw 'Viewer stage-camera automatic evidence is not state/pixel bound.'
        }
        [pscustomobject]@{
            state = $stateMatches[0]
            pixel = $pixelMatches[0]
        }
    }

    $validateFrames = {
        param(
            [Parameter(Mandatory)]
            [object[]]$Frames,
            [Parameter(Mandatory)]
            [double]$TimeCode)

        $common = $null
        foreach ($frame in $Frames)
        {
            $backend = [string](Get-ViewerEvidenceProperty $frame 'backend')
            $phase = [string](Get-ViewerEvidenceProperty $frame 'phase')
            $stateMatches = @($states | Where-Object {
                [string]$_.backend -ceq $backend -and
                [string]$_.phase -ceq $phase
            })
            $pixelHash = [string](
                Get-ViewerEvidenceProperty $frame 'pixelSha256')
            $pixelArtifact = [string](
                Get-ViewerEvidenceProperty $frame 'pixelArtifact')
            $pixelMatches = @($pixels | Where-Object {
                [string]$_.sha256 -ceq $pixelHash -and
                [string]$_.artifact -ceq $pixelArtifact
            })
            $storm = $backend -ceq 'Storm'
            if ($stateMatches.Count -ne 1 -or
                $pixelMatches.Count -ne 1 -or
                [double](Get-ViewerEvidenceProperty $frame 'timeCode') -ne $TimeCode -or
                [double]$stateMatches[0].timeCode -ne $TimeCode -or
                [uint64](Get-ViewerEvidenceProperty $frame 'stateRevision') -ne
                    [uint64]$stateMatches[0].revision -or
                [string]$stateMatches[0].cameraMode -cne 'Matrices' -or
                [string](Get-ViewerEvidenceProperty $frame 'cameraSignature') -cne
                    [string]$stateMatches[0].cameraSignature -or
                [string](Get-ViewerEvidenceProperty $frame 'nativeCameraSignature') -cne
                    [string]$stateMatches[0].nativeCameraSignature -or
                -not [bool](Get-ViewerEvidenceProperty `
                    $frame 'exactReferencePreserved') -or
                [string]$pixelMatches[0].backend -cne $backend -or
                ($storm -and
                 ([uint64](Get-ViewerEvidenceProperty `
                    $frame 'latestRequestedRevision') -ne
                        [uint64]$stateMatches[0].revision -or
                  [string](Get-ViewerEvidenceProperty `
                    $frame 'latestRequestedCameraSignature') -cne
                        [string]$stateMatches[0].nativeCameraSignature -or
                  [string](Get-ViewerEvidenceProperty `
                    $frame 'latestRenderedCameraSignature') -cne
                        [string]$stateMatches[0].nativeCameraSignature)) -or
                (-not $storm -and
                 ([uint64](Get-ViewerEvidenceProperty `
                    $frame 'latestRequestedRevision') -ne 0 -or
                  -not [string]::IsNullOrEmpty([string](
                    Get-ViewerEvidenceProperty `
                        $frame 'latestRequestedCameraSignature')) -or
                  -not [string]::IsNullOrEmpty([string](
                    Get-ViewerEvidenceProperty `
                        $frame 'latestRenderedCameraSignature')))))
            {
                throw "Viewer authored camera evidence is invalid for $backend/$phase."
            }
            if ($null -eq $common)
            {
                $common = $stateMatches[0]
            }
            elseif ([uint64]$common.revision -ne [uint64]$stateMatches[0].revision -or
                [string]$common.cameraSignature -cne
                    [string]$stateMatches[0].cameraSignature -or
                [string]$common.nativeCameraSignature -cne
                    [string]$stateMatches[0].nativeCameraSignature)
            {
                throw 'Viewer backend switching changed the authored-camera state.'
            }
        }
        $common
    }

    $before = & $validateAutomatic (
        Get-ViewerEvidenceProperty $stageCamera 'automaticBefore')
    $restored = & $validateAutomatic (
        Get-ViewerEvidenceProperty $stageCamera 'automaticRestored')
    $initialState = & $validateFrames $initialFrames $initialTime
    $sampledState = & $validateFrames $sampledFrames $sampledTime
    if ([uint64]$before.state.revision -ge [uint64]$initialState.revision -or
        [uint64]$initialState.revision -ge [uint64]$sampledState.revision -or
        [uint64]$sampledState.revision -ge [uint64]$restored.state.revision -or
        [string]$before.state.cameraSignature -cne
            [string]$restored.state.cameraSignature -or
        [string]$before.state.nativeCameraSignature -cne
            [string]$restored.state.nativeCameraSignature -or
        [string]$initialState.cameraSignature -ceq
            [string]$sampledState.cameraSignature -or
        [string]$initialState.nativeCameraSignature -ceq
            [string]$sampledState.nativeCameraSignature)
    {
        throw 'Viewer stage-camera revisions or camera signatures are stale.'
    }
    $initialStorm = @($initialFrames | Where-Object backend -eq 'Storm')[0]
    $sampledStorm = @($sampledFrames | Where-Object backend -eq 'Storm')[0]
    foreach ($automaticHash in @(
        [string]$before.pixel.sha256,
        [string]$restored.pixel.sha256))
    {
        if ($automaticHash -ceq [string]$initialStorm.pixelSha256 -or
            $automaticHash -ceq [string]$sampledStorm.pixelSha256)
        {
            throw 'Viewer automatic pixels match authored-camera pixels.'
        }
    }
    foreach ($backend in $requiredBackends)
    {
        $initial = @($initialFrames | Where-Object backend -eq $backend)[0]
        $sampled = @($sampledFrames | Where-Object backend -eq $backend)[0]
        if ([string]$initial.pixelSha256 -ceq [string]$sampled.pixelSha256)
        {
            throw "Viewer $backend initial and sampled stage-camera pixels are stale."
        }
    }
    $stageCamera
}

function Assert-ViewerNativeNavigationEvidence
{
    param(
        [Parameter(Mandatory)]
        [object]$Artifact)

    $platform = [string](Get-ViewerEvidenceProperty $Artifact 'platformHandle')
    $inputs = @((Get-ViewerEvidenceProperty $Artifact 'inputs'))
    $navigation = @((
        Get-ViewerEvidenceProperty $Artifact 'nativeNavigation' -Optional))
    $requiresNavigation =
        $platform -eq 'HWND' -and
        @($inputs | Where-Object backend -eq 'Storm').Count -gt 0
    if ($requiresNavigation -and $navigation.Count -eq 0)
    {
        throw 'Windows Storm evidence is missing native navigation proof.'
    }
    if ($platform -ne 'HWND' -and $navigation.Count -ne 0)
    {
        throw 'Non-Windows evidence contains Win32 native navigation proof.'
    }
    $states = @((Get-ViewerEvidenceProperty $Artifact 'states'))
    $pixels = @((Get-ViewerEvidenceProperty $Artifact 'pixels'))
    $expectedDeliveryApi =
        'SendMessageTimeoutW+StormChildWndProc+ABI8Poll+' +
        'ViewerCameraNavigationUiAdapter'
    foreach ($entry in $navigation)
    {
        $messages = @((Get-ViewerEvidenceProperty $entry 'win32Messages'))
        $beforeCamera = [string](Get-ViewerEvidenceProperty $entry 'cameraBeforeSignature')
        $afterCamera = [string](Get-ViewerEvidenceProperty $entry 'cameraAfterSignature')
        $beforePixel = [string](Get-ViewerEvidenceProperty $entry 'pixelBeforeSha256')
        $afterPixel = [string](Get-ViewerEvidenceProperty $entry 'pixelAfterSha256')
        $beforeArtifact = [string](Get-ViewerEvidenceProperty $entry 'pixelBeforeArtifact')
        $afterArtifact = [string](Get-ViewerEvidenceProperty $entry 'pixelAfterArtifact')
        if ([string](Get-ViewerEvidenceProperty $entry 'backend') -ne 'Storm' -or
            [string](Get-ViewerEvidenceProperty $entry 'deliveryApi') -ne
                $expectedDeliveryApi -or
            [string](Get-ViewerEvidenceProperty $entry 'snapshotApi') -ne
                'openusd_storm_child_get_navigation_input(ABI8,v2)' -or
            [int](Get-ViewerEvidenceProperty $entry 'stormChildAbiVersion') -ne 8 -or
            [string](Get-ViewerEvidenceProperty $entry 'gesture') -ne
                'Alt+Left Orbit' -or
            [uint64](Get-ViewerEvidenceProperty $entry 'sequencePressed') -le
                [uint64](Get-ViewerEvidenceProperty $entry 'sequenceBefore') -or
            [uint64](Get-ViewerEvidenceProperty $entry 'sequenceMoved') -le
                [uint64](Get-ViewerEvidenceProperty $entry 'sequencePressed') -or
            [uint64](Get-ViewerEvidenceProperty $entry 'sequenceAfter') -le
                [uint64](Get-ViewerEvidenceProperty $entry 'sequenceMoved') -or
            [string](Get-ViewerEvidenceProperty $entry 'pressedButtons') -ne 'Left' -or
            [string](Get-ViewerEvidenceProperty $entry 'pressedModifiers') -ne 'Alt' -or
            [string](Get-ViewerEvidenceProperty $entry 'pressedState') -notmatch
                'Focused' -or
            [int](Get-ViewerEvidenceProperty $entry 'pointerDeltaX') -eq 0 -or
            [int](Get-ViewerEvidenceProperty $entry 'pointerDeltaY') -eq 0 -or
            [int](Get-ViewerEvidenceProperty $entry 'avaloniaRoutedEvents') -ne 0 -or
            $beforeCamera -notmatch '^[0-9A-Fa-f]{64}$' -or
            $afterCamera -notmatch '^[0-9A-Fa-f]{64}$' -or
            $beforeCamera -ceq $afterCamera -or
            $beforePixel -notmatch '^[0-9A-Fa-f]{64}$' -or
            $afterPixel -notmatch '^[0-9A-Fa-f]{64}$' -or
            $beforePixel -ceq $afterPixel -or
            -not [bool](Get-ViewerEvidenceProperty $entry 'cameraChanged') -or
            -not [bool](Get-ViewerEvidenceProperty $entry 'pixelChanged') -or
            $messages.Count -lt 7 -or
            @($messages | Where-Object {
                $_.target -ne 'StormChild' -or
                $_.api -ne 'SendMessageTimeoutW' -or
                -not $_.apiSucceeded -or
                -not $_.wndProcObserved -or
                -not $_.handlerObserved -or
                $_.synthesized
            }).Count -ne 0 -or
            @($states | Where-Object {
                $_.backend -eq 'Storm' -and
                $_.phase -eq [string]$entry.phase -and
                $_.cameraSignature -ceq $afterCamera
            }).Count -ne 1 -or
            @($pixels | Where-Object {
                $_.sha256 -ceq $beforePixel -and
                $_.artifact -ceq $beforeArtifact
            }).Count -ne 1 -or
            @($pixels | Where-Object {
                $_.sha256 -ceq $afterPixel -and
                $_.artifact -ceq $afterArtifact
            }).Count -ne 1)
        {
            throw 'Viewer native Storm navigation evidence is incomplete.'
        }
    }
}

function Get-ViewerEvidencePathComparison
{
    if ($IsWindows)
    {
        [StringComparison]::OrdinalIgnoreCase
    }
    else
    {
        [StringComparison]::Ordinal
    }
}

function Resolve-ViewerEvidencePath
{
    param(
        [Parameter(Mandatory)]
        [string]$EvidenceRoot,
        [Parameter(Mandatory)]
        [string]$Path,
        [switch]$Directory)

    if ([string]::IsNullOrWhiteSpace($Path))
    {
        throw 'Viewer evidence contains an empty artifact path.'
    }
    $root = [IO.Path]::GetFullPath($EvidenceRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $full = if ([IO.Path]::IsPathFullyQualified($Path))
    {
        [IO.Path]::GetFullPath($Path)
    }
    else
    {
        [IO.Path]::GetFullPath((Join-Path $root $Path))
    }
    $comparison = Get-ViewerEvidencePathComparison
    $prefix = $root + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix, $comparison))
    {
        throw "Viewer evidence artifact is outside the evidence root: $Path"
    }
    if ($Directory)
    {
        if (-not (Test-Path $full -PathType Container))
        {
            throw "Viewer evidence directory is missing: $Path"
        }
    }
    elseif (-not (Test-Path $full -PathType Leaf))
    {
        throw "Viewer evidence artifact is missing: $Path"
    }

    $current = Get-Item $full -Force
    while ($null -ne $current -and
        -not $current.FullName.Equals($root, $comparison))
    {
        if (($current.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)
        {
            throw "Viewer evidence artifact uses a symbolic link or reparse point: $Path"
        }
        $current = $current.Parent
    }

    [ordered]@{
        root = $root
        fullPath = $full
        relativePath = [IO.Path]::GetRelativePath($root, $full).
            Replace('\', '/')
    }
}

function Get-ViewerBitmapData
{
    param(
        [Parameter(Mandatory)]
        [string]$Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 54 -or
        $bytes[0] -ne 0x42 -or
        $bytes[1] -ne 0x4D)
    {
        throw "Viewer pixel artifact is not a supported BMP file: $Path"
    }
    $encodedLength = [BitConverter]::ToInt32($bytes, 2)
    $pixelOffset = [BitConverter]::ToInt32($bytes, 10)
    $dibSize = [BitConverter]::ToInt32($bytes, 14)
    $width = [BitConverter]::ToInt32($bytes, 18)
    $signedHeight = [BitConverter]::ToInt32($bytes, 22)
    $planes = [BitConverter]::ToUInt16($bytes, 26)
    $bitsPerPixel = [BitConverter]::ToUInt16($bytes, 28)
    $compression = [BitConverter]::ToInt32($bytes, 30)
    $encodedPixelLength = [BitConverter]::ToInt32($bytes, 34)
    if ($encodedLength -ne $bytes.Length -or
        $dibSize -lt 40 -or
        $width -lt 1 -or
        $signedHeight -eq 0 -or
        $signedHeight -eq [int]::MinValue -or
        $planes -ne 1 -or
        $bitsPerPixel -ne 32 -or
        $compression -ne 0)
    {
        throw "Viewer pixel artifact has an invalid BMP header: $Path"
    }
    $height = [Math]::Abs($signedHeight)
    $pixelLength = [int64]$width * $height * 4
    if ($pixelLength -gt [int]::MaxValue -or
        $encodedPixelLength -ne $pixelLength -or
        $pixelOffset -lt (14 + $dibSize) -or
        ([int64]$pixelOffset + $pixelLength) -ne $bytes.Length)
    {
        throw "Viewer pixel artifact has inconsistent BMP dimensions or size: $Path"
    }
    $pixels = [byte[]]::new([int]$pixelLength)
    [Array]::Copy($bytes, $pixelOffset, $pixels, 0, $pixels.Length)
    [ordered]@{
        bytes = $bytes
        pixels = $pixels
        width = $width
        height = $height
        topDown = $signedHeight -lt 0
    }
}

function Get-ViewerRecordedPixelHash
{
    param(
        [Parameter(Mandatory)]
        [object]$Bitmap,
        [Parameter(Mandatory)]
        [object]$Pixel)

    $captureApi = [string](Get-ViewerEvidenceProperty $Pixel 'captureApi')
    $pixels = [byte[]]$Bitmap.pixels
    if ($captureApi -notmatch '^openusd_storm_child_capture_framebuffer\(')
    {
        return [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData($pixels))
    }

    $width = [int]$Bitmap.width
    $height = [int]$Bitmap.height
    $rowBytes = $width * 4
    $rgba = [byte[]]::new($pixels.Length)
    for ($y = 0; $y -lt $height; $y++)
    {
        $sourceY = if ($Bitmap.topDown) { $y } else { $height - 1 - $y }
        $destinationY = $height - 1 - $y
        for ($x = 0; $x -lt $width; $x++)
        {
            $source = ($sourceY * $rowBytes) + ($x * 4)
            $destination = ($destinationY * $rowBytes) + ($x * 4)
            $rgba[$destination] = $pixels[$source + 2]
            $rgba[$destination + 1] = $pixels[$source + 1]
            $rgba[$destination + 2] = $pixels[$source]
            $rgba[$destination + 3] = $pixels[$source + 3]
        }
    }
    [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($rgba))
}

function Get-ViewerEvidenceFileRecord
{
    param(
        [Parameter(Mandatory)]
        [string]$EvidenceRoot,
        [Parameter(Mandatory)]
        [string]$Path)

    $resolved = Resolve-ViewerEvidencePath -EvidenceRoot $EvidenceRoot -Path $Path
    $item = Get-Item $resolved.fullPath
    [ordered]@{
        path = $resolved.relativePath
        sha256 = (Get-FileHash $resolved.fullPath -Algorithm SHA256).
            Hash.ToUpperInvariant()
        length = [int64]$item.Length
    }
}

function Get-ViewerPixelArtifactRecord
{
    param(
        [Parameter(Mandatory)]
        [string]$EvidenceRoot,
        [Parameter(Mandatory)]
        [object]$Pixel)

    $artifact = [string](Get-ViewerEvidenceProperty $Pixel 'artifact')
    $resolved = Resolve-ViewerEvidencePath -EvidenceRoot $EvidenceRoot -Path $artifact
    $bitmap = Get-ViewerBitmapData -Path $resolved.fullPath
    $recordedWidth = [int](Get-ViewerEvidenceProperty $Pixel 'width')
    $recordedHeight = [int](Get-ViewerEvidenceProperty $Pixel 'height')
    if ($bitmap.width -ne $recordedWidth -or
        $bitmap.height -ne $recordedHeight)
    {
        throw "Viewer pixel artifact dimensions do not match evidence: $artifact"
    }
    $pixelHash = Get-ViewerRecordedPixelHash -Bitmap $bitmap -Pixel $Pixel
    $recordedHash = [string](Get-ViewerEvidenceProperty $Pixel 'sha256')
    if ($recordedHash -notmatch '^[0-9A-Fa-f]{64}$' -or
        $pixelHash -cne $recordedHash.ToUpperInvariant())
    {
        throw "Viewer pixel artifact hash does not match evidence: $artifact"
    }
    foreach ($lengthName in @('length', 'size', 'fileSize'))
    {
        $recordedLength = Get-ViewerEvidenceProperty $Pixel $lengthName -Optional
        if ($null -ne $recordedLength -and
            [int64]$recordedLength -ne $bitmap.bytes.Length)
        {
            throw "Viewer pixel artifact size does not match evidence: $artifact"
        }
    }
    [ordered]@{
        path = $resolved.relativePath
        sha256 = [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData(
                [byte[]]$bitmap.bytes))
        length = [int64]$bitmap.bytes.Length
        width = [int]$bitmap.width
        height = [int]$bitmap.height
        pixelSha256 = $pixelHash
        backend = [string](Get-ViewerEvidenceProperty $Pixel 'backend')
        captureApi = [string](Get-ViewerEvidenceProperty $Pixel 'captureApi')
    }
}

function Assert-ViewerPixelArtifacts
{
    param(
        [Parameter(Mandatory)]
        [object[]]$Pixels,
        [Parameter(Mandatory)]
        [string]$EvidenceRoot)

    $comparison = if ($IsWindows)
    {
        [StringComparer]::OrdinalIgnoreCase
    }
    else
    {
        [StringComparer]::Ordinal
    }
    $paths = [Collections.Generic.HashSet[string]]::new($comparison)
    $records = foreach ($pixel in $Pixels)
    {
        $record = Get-ViewerPixelArtifactRecord `
            -EvidenceRoot $EvidenceRoot `
            -Pixel $pixel
        if (-not $paths.Add([string]$record.path))
        {
            throw "Viewer evidence references a duplicate pixel artifact: $($record.path)"
        }
        $record
    }
    @($records)
}

function Assert-ViewerEvidenceFileRecord
{
    param(
        [Parameter(Mandatory)]
        [object]$Record,
        [Parameter(Mandatory)]
        [string]$EvidenceRoot,
        [switch]$Pixel)

    $path = [string](Get-ViewerEvidenceProperty $Record 'path')
    $resolved = Resolve-ViewerEvidencePath -EvidenceRoot $EvidenceRoot -Path $path
    $bytes = [IO.File]::ReadAllBytes($resolved.fullPath)
    $actualHash = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($bytes))
    $expectedHash = [string](Get-ViewerEvidenceProperty $Record 'sha256')
    $expectedLength = [int64](Get-ViewerEvidenceProperty $Record 'length')
    if ($expectedHash -notmatch '^[0-9A-Fa-f]{64}$' -or
        $actualHash -cne $expectedHash.ToUpperInvariant() -or
        $bytes.Length -ne $expectedLength)
    {
        throw "Viewer aggregate artifact hash or size does not match: $path"
    }
    if ($Pixel)
    {
        $bitmap = Get-ViewerBitmapData -Path $resolved.fullPath
        if ($bitmap.width -ne [int](Get-ViewerEvidenceProperty $Record 'width') -or
            $bitmap.height -ne [int](Get-ViewerEvidenceProperty $Record 'height'))
        {
            throw "Viewer aggregate pixel dimensions do not match: $path"
        }
        $actualPixelHash = Get-ViewerRecordedPixelHash `
            -Bitmap $bitmap `
            -Pixel $Record
        $expectedPixelHash = [string](
            Get-ViewerEvidenceProperty $Record 'pixelSha256')
        if ($actualPixelHash -cne $expectedPixelHash.ToUpperInvariant())
        {
            throw "Viewer aggregate pixel hash does not match: $path"
        }
    }
    $resolved.relativePath
}

function Assert-ViewerEvidenceSchemaVersion
{
    param(
        [Parameter(Mandatory)]
        [object]$Artifact,
        [Parameter(Mandatory)]
        [string]$ArtifactKind)

    $schemaVersion = Get-ViewerEvidenceProperty $Artifact 'schemaVersion'
    if ([int]$schemaVersion -ne $script:ViewerEvidenceSchemaVersion)
    {
        throw "$ArtifactKind must use Viewer evidence schema $script:ViewerEvidenceSchemaVersion."
    }
}

function Assert-ViewerAggregateEvidence
{
    param(
        [Parameter(Mandatory)]
        [object]$Aggregate,
        [Parameter(Mandatory)]
        [int]$ExpectedRunCount,
        [Parameter(Mandatory)]
        [string]$EvidenceRoot)

    Assert-ViewerEvidenceSchemaVersion -Artifact $Aggregate -ArtifactKind 'Viewer aggregate'
    $runs = @((Get-ViewerEvidenceProperty $Aggregate 'runs'))
    if ([string](Get-ViewerEvidenceProperty $Aggregate 'status') -ne 'passed' -or
        [int](Get-ViewerEvidenceProperty $Aggregate 'scenarioCount') -ne $ExpectedRunCount -or
        $runs.Count -ne $ExpectedRunCount)
    {
        throw 'Viewer aggregate status or run count is invalid.'
    }

    $pathComparer = if ($IsWindows)
    {
        [StringComparer]::OrdinalIgnoreCase
    }
    else
    {
        [StringComparer]::Ordinal
    }
    $allPaths = [Collections.Generic.HashSet[string]]::new($pathComparer)
    $runRoots = [Collections.Generic.HashSet[string]]::new($pathComparer)
    foreach ($run in $runs)
    {
        Assert-ViewerEvidenceSchemaVersion -Artifact $run -ArtifactKind 'Viewer run'
        $runtimeCompositor = [string](
            Get-ViewerEvidenceProperty $run 'runtimeCompositor')
        $windowsRun =
            $runtimeCompositor -ceq 'ANGLE/D3D11 (runtime-observed)'
        $nonWindowsRun = $runtimeCompositor.StartsWith(
            'X11',
            [StringComparison]::Ordinal)
        if (-not $windowsRun -and -not $nonWindowsRun)
        {
            throw 'Viewer aggregate run has an invalid runtime compositor.'
        }
        $artifactRoot = [string](Get-ViewerEvidenceProperty $run 'artifactRoot')
        $resolvedRoot = Resolve-ViewerEvidencePath `
            -EvidenceRoot $EvidenceRoot `
            -Path $artifactRoot `
            -Directory
        if (-not $runRoots.Add($resolvedRoot.relativePath))
        {
            throw "Viewer aggregate contains a duplicate run artifact root: $artifactRoot"
        }
        $artifacts = @((Get-ViewerEvidenceProperty $run 'artifacts'))
        $pixels = @((Get-ViewerEvidenceProperty $run 'pixelArtifacts'))
        if ($artifacts.Count -eq 0 -or $pixels.Count -eq 0)
        {
            throw 'Viewer aggregate run must bind run artifacts and pixel artifacts.'
        }
        $manifestPaths = [Collections.Generic.HashSet[string]]::new($pathComparer)
        foreach ($record in $artifacts)
        {
            $path = Assert-ViewerEvidenceFileRecord `
                -Record $record `
                -EvidenceRoot $EvidenceRoot
            if (-not $manifestPaths.Add($path) -or -not $allPaths.Add($path))
            {
                throw "Viewer aggregate contains a duplicate artifact: $path"
            }
        }
        foreach ($record in $pixels)
        {
            $path = Assert-ViewerEvidenceFileRecord `
                -Record $record `
                -EvidenceRoot $EvidenceRoot `
                -Pixel
            if (-not $manifestPaths.Add($path) -or -not $allPaths.Add($path))
            {
                throw "Viewer aggregate contains a duplicate pixel artifact: $path"
            }
        }
        $actualPaths = @(Get-ChildItem $resolvedRoot.fullPath -File -Recurse |
            ForEach-Object {
                [IO.Path]::GetRelativePath(
                    [IO.Path]::GetFullPath($EvidenceRoot),
                    $_.FullName).Replace('\', '/')
            })
        if ($actualPaths.Count -ne $manifestPaths.Count -or
            @($actualPaths | Where-Object { -not $manifestPaths.Contains($_) }).Count -ne 0)
        {
            throw "Viewer aggregate does not bind every artifact under: $artifactRoot"
        }
        $pixelHashes = @((Get-ViewerEvidenceProperty $run 'pixelHashes'))
        $boundPixelHashes = @($pixels | ForEach-Object {
            [string](Get-ViewerEvidenceProperty $_ 'pixelSha256')
        })
        if ([int](Get-ViewerEvidenceProperty $run 'pixelCount') -ne $pixels.Count -or
            ($pixelHashes | ConvertTo-Json -Compress) -cne
                ($boundPixelHashes | ConvertTo-Json -Compress))
        {
            throw 'Viewer aggregate pixel summary does not match bound pixel artifacts.'
        }
        $cameraTransitions = @(
            (Get-ViewerEvidenceProperty $run 'cameraTransitions'))
        if ($cameraTransitions.Count -eq 0 -or
            [int](Get-ViewerEvidenceProperty $run 'cameraTransitionCount') -ne
                $cameraTransitions.Count)
        {
            throw 'Viewer aggregate camera summary is incomplete.'
        }
        $stormTransitions = @($cameraTransitions | Where-Object {
            [string](Get-ViewerEvidenceProperty $_ 'backend') -ceq 'Storm'
        })
        foreach ($transition in $cameraTransitions)
        {
            foreach ($prefix in @('automatic', 'explicit', 'restored'))
            {
                $signature = [string](
                    Get-ViewerEvidenceProperty $transition "${prefix}CameraSignature")
                $pixelHash = [string](
                    Get-ViewerEvidenceProperty $transition "${prefix}PixelSha256")
                $pixelPath = [string](
                    Get-ViewerEvidenceProperty $transition "${prefix}PixelPath")
                if ($signature -notmatch '^[0-9A-Fa-f]{64}$' -or
                    @($pixels | Where-Object {
                        [string]$_.pixelSha256 -ceq $pixelHash -and
                        [string]$_.path -ceq $pixelPath
                    }).Count -ne 1)
                {
                    throw "Viewer aggregate camera $prefix evidence is not bound."
                }
            }
            if ([string]$transition.automaticCameraSignature -ceq
                    [string]$transition.explicitCameraSignature -or
                [string]$transition.automaticPixelSha256 -ceq
                    [string]$transition.explicitPixelSha256 -or
                [string]$transition.restoredPixelSha256 -ceq
                    [string]$transition.explicitPixelSha256)
            {
                throw 'Viewer aggregate explicit-camera evidence is stale.'
            }
        }
        $runScenario = [string](
            Get-ViewerEvidenceProperty $run 'scenario' -Optional)
        $stageCamera = Get-ViewerEvidenceProperty $run 'stageCamera' -Optional
        $stageCameraRequired =
            $runScenario -ceq 'stage-camera-backend-smoke'
        if ($stageCameraRequired -ne ($null -ne $stageCamera))
        {
            throw 'Viewer aggregate stage-camera summary does not match its scenario.'
        }
        if ($stageCameraRequired)
        {
            $stageAssetPath = [string](
                Get-ViewerEvidenceProperty $stageCamera 'stageAssetPath')
            $stageSha256 = [string](
                Get-ViewerEvidenceProperty $stageCamera 'stageSha256')
            $stageAssetLength = [int64](
                Get-ViewerEvidenceProperty $stageCamera 'stageAssetLength')
            $stageAsset = @($artifacts | Where-Object {
                [string]$_.path -ceq $stageAssetPath -and
                [string]$_.sha256 -ceq $stageSha256 -and
                [int64]$_.length -eq $stageAssetLength
            })
            $initialFrames = @(
                (Get-ViewerEvidenceProperty $stageCamera 'initialFrames'))
            $sampledFrames = @(
                (Get-ViewerEvidenceProperty $stageCamera 'sampledFrames'))
            $automaticBefore =
                Get-ViewerEvidenceProperty $stageCamera 'automaticBefore'
            $automaticRestored =
                Get-ViewerEvidenceProperty $stageCamera 'automaticRestored'
            if ([string](Get-ViewerEvidenceProperty $stageCamera 'source') -cne
                    'ViewerSchedulerStageCameraSource' -or
                [string]::IsNullOrWhiteSpace([string](
                    Get-ViewerEvidenceProperty $stageCamera 'stageIdentifier')) -or
                $stageSha256 -notmatch '^[0-9A-Fa-f]{64}$' -or
                $stageAsset.Count -ne 1 -or
                [string]::IsNullOrWhiteSpace([string](
                    Get-ViewerEvidenceProperty $stageCamera 'cameraPath')) -or
                -not ([string](
                    Get-ViewerEvidenceProperty $stageCamera 'cameraPath')).StartsWith('/') -or
                [string](Get-ViewerEvidenceProperty `
                    $stageCamera 'initialSnapshotSha256') -notmatch
                        '^[0-9A-Fa-f]{64}$' -or
                [string](Get-ViewerEvidenceProperty `
                    $stageCamera 'sampledSnapshotSha256') -notmatch
                        '^[0-9A-Fa-f]{64}$' -or
                [string](Get-ViewerEvidenceProperty `
                    $stageCamera 'initialSnapshotSha256') -ceq
                [string](Get-ViewerEvidenceProperty `
                    $stageCamera 'sampledSnapshotSha256') -or
                -not [bool](Get-ViewerEvidenceProperty `
                    $stageCamera 'exactStatePreservedAcrossBackends') -or
                $initialFrames.Count -ne 3 -or
                $sampledFrames.Count -ne 3)
            {
                throw 'Viewer aggregate stage-camera provenance is incomplete.'
            }
            foreach ($entry in @(
                $automaticBefore,
                $automaticRestored) + $initialFrames + $sampledFrames)
            {
                $pixelHash = [string](
                    Get-ViewerEvidenceProperty $entry 'pixelSha256')
                $pixelPath = [string](
                    Get-ViewerEvidenceProperty $entry 'pixelPath')
                if (@($pixels | Where-Object {
                    [string]$_.pixelSha256 -ceq $pixelHash -and
                    [string]$_.path -ceq $pixelPath
                }).Count -ne 1)
                {
                    throw 'Viewer aggregate stage-camera pixels are not bound.'
                }
            }
            foreach ($backend in @('Storm', 'D3D12', 'Vulkan'))
            {
                $initial = @(
                    $initialFrames | Where-Object backend -eq $backend)
                $sampled = @(
                    $sampledFrames | Where-Object backend -eq $backend)
                if ($initial.Count -ne 1 -or
                    $sampled.Count -ne 1 -or
                    [string]$initial[0].pixelSha256 -ceq
                        [string]$sampled[0].pixelSha256 -or
                    [uint64]$initial[0].stateRevision -ge
                        [uint64]$sampled[0].stateRevision)
                {
                    throw "Viewer aggregate $backend stage-camera samples are stale."
                }
            }
            if ([string]$automaticBefore.cameraSignature -cne
                    [string]$automaticRestored.cameraSignature -or
                [uint64]$automaticBefore.stateRevision -ge
                    [uint64]$initialFrames[0].stateRevision -or
                [uint64]$sampledFrames[0].stateRevision -ge
                    [uint64]$automaticRestored.stateRevision)
            {
                throw 'Viewer aggregate automatic stage-camera restoration is stale.'
            }
        }
        $nativeNavigation = @(
            (Get-ViewerEvidenceProperty $run 'nativeNavigation' -Optional))
        if ([int](Get-ViewerEvidenceProperty $run 'nativeNavigationCount') -ne
                $nativeNavigation.Count)
        {
            throw 'Viewer aggregate native navigation summary is incomplete.'
        }
        $stormPixels = @($pixels | Where-Object {
            [string](Get-ViewerEvidenceProperty $_ 'backend') -ceq 'Storm'
        })
        $stormRun =
            $stormPixels.Count -gt 0 -or
            $stormTransitions.Count -gt 0 -or
            $nativeNavigation.Count -gt 0
        if ($stormRun)
        {
            if ([int](Get-ViewerEvidenceProperty `
                    $run 'stormChildAbiVersion') -ne 8 -or
                $stormPixels.Count -eq 0 -or
                @($stormPixels | Where-Object {
                    [string](Get-ViewerEvidenceProperty $_ 'captureApi') -cne
                        'openusd_storm_child_capture_framebuffer(ABI8,preserved-texture)'
                }).Count -ne 0)
            {
                throw 'Viewer aggregate Storm ABI or pixel capture provenance is invalid.'
            }
        }
        if ($windowsRun -and $stormRun -and $nativeNavigation.Count -eq 0)
        {
            throw 'Viewer aggregate Windows Storm run is missing native navigation proof.'
        }
        if ($nonWindowsRun -and $nativeNavigation.Count -ne 0)
        {
            throw 'Viewer aggregate non-Windows run contains Win32 native navigation proof.'
        }
        foreach ($entry in $nativeNavigation)
        {
            foreach ($prefix in @('before', 'after'))
            {
                $pixelHash = [string](
                    Get-ViewerEvidenceProperty $entry "${prefix}PixelSha256")
                $pixelPath = [string](
                    Get-ViewerEvidenceProperty $entry "${prefix}PixelPath")
                if (@($pixels | Where-Object {
                    [string]$_.pixelSha256 -ceq $pixelHash -and
                    [string]$_.path -ceq $pixelPath
                }).Count -ne 1)
                {
                    throw "Viewer aggregate native navigation $prefix pixel is not bound."
                }
            }
            if ([string](Get-ViewerEvidenceProperty $entry 'backend') -cne
                    'Storm' -or
                [string](Get-ViewerEvidenceProperty $entry 'deliveryApi') -cne
                    'SendMessageTimeoutW+StormChildWndProc+ABI8Poll+' +
                    'ViewerCameraNavigationUiAdapter' -or
                [string](Get-ViewerEvidenceProperty $entry 'snapshotApi') -cne
                    'openusd_storm_child_get_navigation_input(ABI8,v2)' -or
                [int](Get-ViewerEvidenceProperty `
                    $entry 'stormChildAbiVersion') -ne 8 -or
                [string]$entry.beforeCameraSignature -ceq
                    [string]$entry.afterCameraSignature -or
                [string]$entry.beforePixelSha256 -ceq
                    [string]$entry.afterPixelSha256 -or
                [int]$entry.avaloniaRoutedEvents -ne 0)
            {
                throw 'Viewer aggregate native navigation evidence is stale.'
            }
        }
    }
}
