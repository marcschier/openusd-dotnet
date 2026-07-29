#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
#
# Two Windows render proofs are narrowed because a hosted runner cannot supply the
# graphics capability they need. Narrowing is the kind of change that rots into a
# silently green gate, so this contract pins its exact shape: the skip stays opt-in at
# the call site, stays confined to the two known proofs, still records evidence, and
# still points at the documented route back to full coverage.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$renderWorkflow = Get-Content (
    Join-Path $repoRoot '.github/workflows/render.yml') -Raw
$platformSmoke = Get-Content (
    Join-Path $repoRoot 'eng/run-platform-smoke.ps1') -Raw
$testingDoc = Get-Content (Join-Path $repoRoot 'docs/testing.md') -Raw
$supportMatrixDoc = Get-Content (Join-Path $repoRoot 'docs/support-matrix.md') -Raw

function Assert-Contains
{
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Expected,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if (-not $Value.Contains($Expected, [StringComparison]::Ordinal))
    {
        throw "$Context does not contain '$Expected'."
    }
}

function Assert-DoesNotContain
{
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Unexpected,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if ($Value.Contains($Unexpected, [StringComparison]::Ordinal))
    {
        throw "$Context unexpectedly contains '$Unexpected'."
    }
}

function Assert-OccurrenceCount
{
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Expected,
        [Parameter(Mandatory = $true)][int]$Count,
        [Parameter(Mandatory = $true)][string]$Context
    )

    $actual = ([regex]::Matches($Value, [regex]::Escape($Expected))).Count
    if ($actual -ne $Count)
    {
        throw "$Context contains '$Expected' $actual time(s); expected $Count."
    }
}

# The Vulkan gate reports its capability instead of throwing, and records why it skipped.
Assert-Contains $renderWorkflow '- name: Resolve system Vulkan ICD' 'Render workflow'
Assert-Contains $renderWorkflow '"available=false" >> $env:GITHUB_OUTPUT' 'Render workflow'
Assert-Contains $renderWorkflow '"available=true" >> $env:GITHUB_OUTPUT' 'Render workflow'
Assert-Contains `
    $renderWorkflow `
    'artifacts/render-capability/windows-vulkan.json' `
    'Render workflow'
Assert-Contains $renderWorkflow "status = 'skipped'" 'Render workflow'
Assert-DoesNotContain `
    $renderWorkflow `
    'No system Vulkan ICD was found; required Vulkan evidence cannot run.' `
    'Render workflow'

# The narrowing is bounded. Only the four proofs that genuinely need a Vulkan adapter are
# guarded; the viewer source-identity and evidence contracts need no graphics at all and
# must keep blocking on every host.
Assert-OccurrenceCount `
    $renderWorkflow `
    "if: steps.vulkan-icd.outputs.available == 'true'" `
    4 `
    'Render workflow'
Assert-Contains `
    $renderWorkflow `
    '- name: Verify viewer source identity and evidence contract' `
    'Render workflow'
$identityStep = $renderWorkflow.Substring(
    $renderWorkflow.IndexOf(
        '- name: Verify viewer source identity and evidence contract',
        [StringComparison]::Ordinal))
$identityStep = $identityStep.Substring(
    0,
    $identityStep.IndexOf('- name: Execute ABI v4', [StringComparison]::Ordinal))
Assert-DoesNotContain $identityStep 'steps.vulkan-icd.outputs.available' 'Viewer identity step'
Assert-Contains $identityStep './eng/test-viewer-source-identity.ps1' 'Viewer identity step'
Assert-Contains $identityStep './eng/test-viewer-evidence-contract.ps1' 'Viewer identity step'

# The WGL narrowing is opt-in at exactly one call site.
Assert-OccurrenceCount `
    $renderWorkflow `
    '-AllowUnavailableCapability' `
    1 `
    'Render workflow'
Assert-Contains $renderWorkflow '-Platform windows-wgl' 'Render workflow'

# The smoke runner confines the skip to windows-wgl and to Avalonia's own two markers for
# a host without a WGL-capable OpenGL implementation.
Assert-Contains $platformSmoke '[switch]$AllowUnavailableCapability' 'Platform smoke runner'
Assert-Contains $platformSmoke "`$Platform -eq 'windows-wgl'" 'Platform smoke runner'
Assert-Contains $platformSmoke 'Unable to initialize WGL' 'Platform smoke runner'
Assert-Contains `
    $platformSmoke `
    'Win32PlatformOptions.RenderingMode has a value of "Wgl", but no options were applied' `
    'Platform smoke runner'
Assert-Contains $platformSmoke 'platform-smoke-capability.json' 'Platform smoke runner'
Assert-Contains $platformSmoke "status = 'skipped'" 'Platform smoke runner'

# The route back to full coverage is documented rather than left as tribal knowledge.
Assert-Contains $testingDoc '## Render gate capability limits' 'Testing documentation'
Assert-Contains $testingDoc '### Unblocking the WGL soak' 'Testing documentation'
Assert-Contains $testingDoc '### Unblocking the Vulkan composition gate' 'Testing documentation'
Assert-Contains $testingDoc 'VK_KHR_external_memory_win32' 'Testing documentation'
Assert-Contains $testingDoc 'VK_KHR_external_semaphore_win32' 'Testing documentation'
Assert-Contains $testingDoc 'artifacts/render-capability/windows-vulkan.json' 'Testing documentation'
Assert-Contains `
    $testingDoc `
    'artifacts/platform-smoke/windows-wgl/platform-smoke-capability.json' `
    'Testing documentation'
Assert-Contains `
    $supportMatrixDoc `
    'testing.md#render-gate-capability-limits' `
    'Support matrix documentation'

Write-Output 'Render capability gate source contract passed.'
