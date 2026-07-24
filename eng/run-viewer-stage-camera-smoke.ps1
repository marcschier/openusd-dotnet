#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

[CmdletBinding()]
param(
    [ValidateRange(60, 1800)]
    [int]$SmokeSeconds = 600,
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows)
{
    throw 'The Viewer authored stage-camera smoke requires Windows.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'viewer-evidence-contract.ps1')
$root = if ([string]::IsNullOrWhiteSpace($OutputRoot))
{
    Join-Path $repoRoot 'artifacts/viewer-stage-camera-smoke'
}
else
{
    [IO.Path]::GetFullPath($OutputRoot)
}
$runtimeRoot = Join-Path $root 'runtime'
$evidencePath = Join-Path $root 'switching-evidence.json'
$identityPath = Join-Path $root 'identity.json'
$stagePath = Join-Path $repoRoot 'test-assets/viewer-stage-camera-smoke.usda'
$cameraPath = '/World/CameraRig/Offset/ShotCamera'
$runViewer = Join-Path $PSScriptRoot 'run-viewer.ps1'

Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $root | Out-Null
$oldRenderer = $env:OPENUSD_RENDERER
try
{
    $env:OPENUSD_RENDERER = 'Storm'
    & $runViewer `
        -Rid win-x64 `
        -StagePath $stagePath `
        -SmokeSeconds $SmokeSeconds `
        -OutputPath $runtimeRoot `
        -EvidencePath $evidencePath `
        -EvidenceScenario 'stage-camera-backend-smoke' `
        -EvidenceCameraPath $cameraPath `
        -IdentityManifestPath $identityPath
    if ($LASTEXITCODE -ne 0)
    {
        throw "Viewer stage-camera smoke exited with $LASTEXITCODE."
    }
}
finally
{
    $env:OPENUSD_RENDERER = $oldRenderer
}

$artifact = Get-Content $evidencePath -Raw | ConvertFrom-Json -Depth 30
$identity = Get-Content $identityPath -Raw | ConvertFrom-Json -Depth 20
Assert-ViewerEvidenceSchemaVersion -Artifact $artifact -ArtifactKind 'Viewer run'
$stageCamera = Assert-ViewerStageCameraEvidence -Artifact $artifact
Assert-ViewerCameraEvidence -Artifact $artifact
Assert-ViewerNativeNavigationEvidence -Artifact $artifact
$pixelRecords = Assert-ViewerPixelArtifacts `
    -Pixels @($artifact.pixels) `
    -EvidenceRoot $root

$stageArtifactRoot = Join-Path $root 'stage'
New-Item -ItemType Directory -Force -Path $stageArtifactRoot | Out-Null
$stageArtifact = Join-Path $stageArtifactRoot ([IO.Path]::GetFileName($stagePath))
Copy-Item $stagePath $stageArtifact -Force
$stageRecord = Get-ViewerEvidenceFileRecord `
    -EvidenceRoot $root `
    -Path $stageArtifact
$sourceStage = @($identity.sourceBefore.files | Where-Object {
    ([string]$_.path).Replace('\', '/') -ceq
        'test-assets/viewer-stage-camera-smoke.usda'
})
$sourceScript = @($identity.sourceBefore.files | Where-Object {
    ([string]$_.path).Replace('\', '/') -ceq
        'eng/run-viewer-stage-camera-smoke.ps1'
})
if (-not $identity.sourceUnchanged -or
    -not $identity.binariesUnchanged -or
    $sourceStage.Count -ne 1 -or
    $sourceScript.Count -ne 1 -or
    [string]$sourceStage[0].sha256 -cne [string]$stageCamera.stageSha256 -or
    [string]$stageRecord.sha256 -cne [string]$stageCamera.stageSha256)
{
    throw 'Viewer stage-camera source, script, binary, or stage identity is stale.'
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
        throw "Viewer stage-camera resource '$counter' did not return to zero."
    }
}

$statusPath = Join-Path $runtimeRoot 'viewer-status.txt'
$statuses = Get-Content $statusPath -Raw
if ($statuses -notmatch
        'Viewer stage camera smoke passed: path=/World/CameraRig/Offset/ShotCamera; .*exactState=True' -or
    $statuses -notmatch 'Viewer final resources: child=0;')
{
    throw 'Viewer stage-camera status did not contain the measured pass/resource proof.'
}

$summary = [ordered]@{
    schemaVersion = [int]$artifact.schemaVersion
    scenario = [string]$artifact.scenario
    status = 'passed'
    completedAt = [DateTimeOffset]::UtcNow.ToString('O')
    evidenceSha256 = (Get-FileHash $evidencePath -Algorithm SHA256).Hash
    identitySha256 = (Get-FileHash $identityPath -Algorithm SHA256).Hash
    sourceSha256 = [string]$identity.sourceBefore.sha256
    stage = $stageRecord
    cameraPath = [string]$stageCamera.cameraPath
    initialTimeCode = [double]$stageCamera.initialTimeCode
    sampledTimeCode = [double]$stageCamera.sampledTimeCode
    initialSnapshotSha256 = [string]$stageCamera.initialSnapshotSha256
    sampledSnapshotSha256 = [string]$stageCamera.sampledSnapshotSha256
    initialPixels = @($stageCamera.initialFrames | ForEach-Object {
        [ordered]@{
            backend = [string]$_.backend
            sha256 = [string]$_.pixelSha256
        }
    })
    sampledPixels = @($stageCamera.sampledFrames | ForEach-Object {
        [ordered]@{
            backend = [string]$_.backend
            sha256 = [string]$_.pixelSha256
        }
    })
    automaticPixelSha256 =
        [string]$stageCamera.automaticRestored.pixelSha256
    pixelArtifacts = $pixelRecords
    resources = $resources
}
$summaryPath = Join-Path $root 'stage-camera-smoke-summary.json'
$summary | ConvertTo-Json -Depth 12 | Set-Content $summaryPath
Write-Output (
    'VIEWER_STAGE_CAMERA_SMOKE passed ' +
    "camera=$cameraPath initial=$($stageCamera.initialSnapshotSha256) " +
    "sampled=$($stageCamera.sampledSnapshotSha256) summary=$summaryPath")
