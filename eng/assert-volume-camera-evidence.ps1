#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Root,
    [Parameter(Mandatory)]
    [ValidateSet('windows', 'linux', 'macos')]
    [string]$Platform,
    [Parameter(Mandatory)]
    [datetime]$NotBeforeUtc
)
$ErrorActionPreference = 'Stop'
$backends = switch ($Platform) {
    'windows' { @('D3D12', 'Vulkan') }
    'linux' { @('Vulkan') }
    'macos' { @('Metal') }
}
$expectedCases = @(
    'analytic-slab-511-side',
    'analytic-slab-512-side',
    'analytic-slab-512-oblique',
    'large-scale-inside',
    'large-scale-threshold',
    'parallel-outside',
    'camera-inside',
    'camera-perspective',
    'camera-near',
    'camera-far',
    'camera-enclosing-clip',
    'scaled-field-local',
    'same-tint-order',
    'opaque-update-reuse',
    'transform-refusal-retry',
    'material-disabled-single',
    'camera-far-origin',
    'camera-nonrigid-view',
    'camera-micro-outside',
    'camera-micro-inside',
    'camera-micro-clipped',
    'perspective-near-small',
    'perspective-near-one',
    'perspective-near-two-asymmetric',
    'oblique-off-center-clip',
    'perspective-finite-large-far',
    'perspective-infinite-far',
    'opaque-one-ulp-before-clear',
    'scene-clip-entry',
    'scene-clip-exit',
    'scene-clip-oblique',
    'scene-clip-excluded',
    'scene-clip-parallel',
    'joint-unequal-tint-reference',
    'joint-uniform-mixed-clipping',
    'joint-replacement-CpuAdmission',
    'joint-replacement-GpuAdmission',
    'joint-replacement-AtlasSubmission',
    'joint-ownership-uniform',
    'joint-ownership-atlas',
    'joint-zero-opacity-field-limit',
    'joint-missing-asset-refusal',
    'joint-retained-atlas-source-removal',
    'joint-unsupported-uniform-map',
    'joint-unsupported-zero-opacity-map',
    'joint-unsupported-sampled-map',
    'joint-unsupported-density-duplicate',
    'joint-unsupported-density-composite',
    'joint-dense-unequal-tint-uniform',
    'joint-dense-unequal-tint-atlas',
    'joint-workload-refusal-retry',
    'joint-nonfinite-density-scalar',
    'joint-nonfinite-density-grid',
    'joint-finite-density-scalar',
    'joint-finite-density-grid',
    'joint-thin-density-scalar-translucent',
    'joint-thin-density-scalar-opaque',
    'joint-thin-density-grid-translucent',
    'joint-thin-density-grid-opaque',
    'joint-tint-exposure-scalar-retained',
    'joint-tint-exposure-scalar-staged',
    'joint-tint-exposure-grid-retained',
    'joint-tint-exposure-grid-staged',
    'joint-tint-exposure-zero-opacity',
    'joint-tint-exposure-material-disabled',
    'joint-high-tint-overlap-scalar',
    'joint-high-tint-overlap-dense-scalar',
    'joint-high-tint-overlap-grid',
    'joint-high-tint-overlap-dense-grid',
    'joint-high-tint-overlap-1e-37-scalar',
    'joint-high-tint-overlap-1e-20-scalar',
    'joint-high-tint-overlap-1e-8-scalar',
    'joint-high-tint-overlap-1e-37-grid',
    'joint-high-tint-overlap-1e-20-grid',
    'joint-high-tint-overlap-1e-8-grid',
    'joint-high-tint-single',
    'joint-high-tint-single-half',
    'joint-high-tint-single-opaque',
    'joint-high-tint-single-reduced-exposure',
    'joint-high-tint-two',
    'joint-high-tint-eight',
    'joint-tint-underflowing-weight',
    'joint-tint-dominant-dark-weight',
    'joint-tint-normal-background',
    'joint-tint-deep-attenuation',
    'joint-tint-sequential-tiny',
    'joint-tint-subnormal-exposure',
    'joint-tint-clipped-tiny-depth',
    'joint-tint-clipped-tiny-overlap',
    'joint-tint-dark-over-bright',
    'joint-signed-background-transmission',
    'joint-color-format-transform-alpha',
    'joint-signal-subnormal-scale-scalar',
    'joint-signal-step-underflow-scalar',
    'joint-signal-opacity-product-scalar',
    'joint-signal-subnormal-scale-grid',
    'joint-signal-step-underflow-grid',
    'joint-signal-opacity-product-grid',
    'joint-signal-subnormal-sample-grid',
    'joint-signal-zero-opacity',
    'joint-signal-material-disabled',
    'joint-signal-retained-resize-scalar',
    'joint-signal-retained-resize-grid',
    'joint-output-allocation-retained',
    'joint-output-frame-submission-retained',
    'joint-output-publication-retained',
    'joint-output-allocation-staged',
    'joint-output-frame-submission-staged',
    'joint-output-publication-staged',
    'joint-direct-retirement-none',
    'joint-retirement-none-retained',
    'joint-retirement-none-staged',
    'joint-direct-retirement-depth',
    'joint-retirement-depth-retained',
    'joint-retirement-depth-staged',
    'joint-direct-retirement-color',
    'joint-retirement-color-retained',
    'joint-retirement-color-staged',
    'joint-direct-retirement-resources',
    'joint-retirement-resources-retained',
    'joint-retirement-resources-staged',
    'joint-direct-retirement-both',
    'joint-retirement-both-retained',
    'joint-retirement-both-staged',
    'joint-failed-frame-cleanup-retained',
    'joint-snapshot-rollback-retained',
    'joint-snapshot-budget-refusal-retained',
    'joint-snapshot-budget-exact-retained',
    'joint-failed-frame-cleanup-staged',
    'joint-snapshot-rollback-staged',
    'joint-snapshot-budget-refusal-staged',
    'joint-snapshot-budget-exact-staged',
    'joint-snapshot-budget-reuse',
    'joint-renderer-disposal-retry'
)
foreach ($backend in $backends) {
    $path = Join-Path (Join-Path $Root "camera-$Platform-$backend") 'receipt.json'
    $file = Get-Item -LiteralPath $path
    if ($file.LastWriteTimeUtc -lt $NotBeforeUtc.ToUniversalTime()) {
        throw "Stale camera receipt: $path"
    }
    $receipt = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($receipt -isnot [pscustomobject]) {
        throw "Incomplete or mismatched camera receipt: $path"
    }
    foreach ($property in @('schemaVersion', 'platform', 'backend', 'status', 'deviceName', 'software', 'cases')) {
        if ($null -eq $receipt.PSObject.Properties[$property]) {
            throw "Incomplete or mismatched camera receipt: $path"
        }
    }
    if ($receipt.schemaVersion -isnot [long] -or $receipt.schemaVersion -ne 1 -or
        $receipt.platform -isnot [string] -or $receipt.platform -cne $Platform -or
        $receipt.backend -isnot [string] -or $receipt.backend -cne $backend -or
        $receipt.status -isnot [string] -or $receipt.status -cne 'passed' -or
        $receipt.deviceName -isnot [string] -or [string]::IsNullOrWhiteSpace($receipt.deviceName) -or
        $receipt.software -isnot [bool]) {
        throw "Incomplete or mismatched camera receipt: $path"
    }
    if ($receipt.cases -isnot [array] -or
        @($receipt.cases | Where-Object { $_ -isnot [string] }).Count -ne 0 -or
        @($receipt.cases).Count -ne $expectedCases.Count -or
        @(Compare-Object $expectedCases @($receipt.cases) -CaseSensitive).Count -ne 0) {
        throw "Missing, duplicate, or unexpected camera cases: $path"
    }
    if ($Platform -eq 'windows' -and $backend -eq 'Vulkan' -and
        ($receipt.software -ne $true -or $receipt.deviceName -notmatch 'SwiftShader')) {
        throw "Windows camera Vulkan evidence did not use SwiftShader: $path"
    }
    if ($Platform -eq 'windows' -and $backend -eq 'D3D12' -and $receipt.software -ne $true) {
        throw "Windows camera D3D12 evidence did not use a software device: $path"
    }
    Write-Host "Camera integration passed: $Platform/$backend ($($receipt.deviceName)); $($expectedCases.Count) cases."
}
