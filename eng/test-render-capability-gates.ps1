#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
#
# The render workflow has a mix of restored and narrowed graphics proofs. Narrowing is
# the kind of change that rots into a silently green gate, so this contract pins its
# exact shape: skips stay opt-in at call sites, stay confined to the known proofs, still
# record evidence, and still point at documented routes back to full coverage.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$renderWorkflow = Get-Content (
    Join-Path $repoRoot '.github/workflows/render.yml') -Raw
$platformSmoke = Get-Content (
    Join-Path $repoRoot 'eng/run-platform-smoke.ps1') -Raw
$parityCapture = Get-Content (
    Join-Path $repoRoot 'eng/run-parity-capture.ps1') -Raw
$stormParityTests = Get-Content (
    Join-Path $repoRoot 'tests/OpenUsd.Rendering.ConformanceTests/StormSilkParityCaptureDriverTests.cs') -Raw
$linuxSmoke = Get-Content (
    Join-Path $repoRoot 'eng/run-avalonia-vulkan-smoke-linux.sh') -Raw
$testingDoc = Get-Content (Join-Path $repoRoot 'docs/testing.md') -Raw
$supportMatrixDoc = Get-Content (Join-Path $repoRoot 'docs/support-matrix.md') -Raw
$mesaWglLock = Get-Content (
    Join-Path $repoRoot 'eng/mesa-wgl-test-runtime.lock.json') -Raw

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

function Assert-SetEqual
{
    param(
        [Parameter(Mandatory = $true)][string[]]$Actual,
        [Parameter(Mandatory = $true)][string[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Context
    )

    $missing = @($Expected | Where-Object { $_ -notin $Actual })
    $unexpected = @($Actual | Where-Object { $_ -notin $Expected })
    if ($missing.Count -ne 0 -or $unexpected.Count -ne 0)
    {
        throw (
            "$Context mismatch. Missing: $($missing -join ', '); " +
            "Unexpected: $($unexpected -join ', ').")
    }
}

function Get-SingleQuotedArray
{
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$VariableName
    )

    $match = [regex]::Match(
        $Value,
        "\`$$VariableName\s*=\s*@\((?<body>.*?)\)",
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $match.Success)
    {
        throw "Could not find array '$VariableName'."
    }

    return @([regex]::Matches($match.Groups['body'].Value, "'(?<value>[^']+)'") |
        ForEach-Object { $_.Groups['value'].Value })
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

# The WGL soak is mandatory on hosted Windows because the platform runner stages pinned
# Mesa llvmpipe before it starts Avalonia/Storm.
Assert-DoesNotContain $renderWorkflow '-AllowUnavailableCapability' 'Render workflow'
Assert-Contains $renderWorkflow '-Platform windows-wgl' 'Render workflow'
Assert-Contains $parityCapture "`$env:OPENUSD_PARITY_WINDOWS_BACKENDS = 'D3D12'" 'Parity capture runner'
Assert-Contains $stormParityTests 'OPENUSD_PARITY_WINDOWS_BACKENDS' 'Storm parity tests'
Assert-Contains $stormParityTests 'CreateD3D12WarpBackend()' 'Storm parity tests'
$windowsWglTests = Get-SingleQuotedArray $parityCapture 'windowsWglTestNames'
$macosCglTests = Get-SingleQuotedArray $parityCapture 'macosCglTestNames'
$stormParityTestNames = @([regex]::Matches(
        $stormParityTests,
        '(?s)\[Test\].*?public async Task (?<name>[A-Za-z0-9_]+)\(') |
        ForEach-Object { $_.Groups['name'].Value })
$expectedWindowsWglTests = @($stormParityTestNames |
    Where-Object { $_ -notlike '*OnVulkan' -and $_ -notlike '*OnMetal' })
$expectedMacosCglTests = @($stormParityTestNames |
    Where-Object {
        $_ -notlike '*OnVulkan' -and
        $_ -notlike '*OnD3D12' -and
        $_ -notmatch 'D3D12|SilkFrameCapture'
    })
Assert-SetEqual $windowsWglTests $expectedWindowsWglTests 'Windows WGL parity test list'
Assert-SetEqual $macosCglTests $expectedMacosCglTests 'macOS CGL parity test list'
Assert-Contains $parityCapture '-MinimumExpectedTests $windowsWglTestNames.Count' 'Parity capture runner'
Assert-Contains $parityCapture '-MinimumExpectedTests $macosCglTestNames.Count' 'Parity capture runner'
Assert-Contains $parityCapture '-MinimumExpectedTests 28' 'Parity capture runner'
Assert-Contains $platformSmoke 'prepare-mesa-wgl-test-runtime.ps1' 'Platform smoke runner'
Assert-Contains $platformSmoke '-Preflight' 'Platform smoke runner'
Assert-Contains $mesaWglLock 'mesa-llvmpipe-x64-26.1.5.7z' 'Mesa WGL runtime lock'
Assert-Contains $mesaWglLock 'D9ED92A40F982C5D92C5581D501A7D4CADBF311F20EB3AC2C8C4EB0F55065D21' `
    'Mesa WGL runtime lock'

# The Linux import narrowing is opt-in at exactly its two call sites, X11 and Wayland.
Assert-OccurrenceCount `
    $renderWorkflow `
    "OPENUSD_ALLOW_UNAVAILABLE_CAPABILITY: '1'" `
    4 `
    'Render workflow'
Assert-Contains `
    $linuxSmoke `
    'OPENUSD_ALLOW_UNAVAILABLE_CAPABILITY' `
    'Linux Vulkan smoke runner'
Assert-Contains `
    $linuxSmoke `
    'supported image handles: (none)' `
    'Linux Vulkan smoke runner'
Assert-Contains $linuxSmoke '"status": "skipped"' 'Linux Vulkan smoke runner'
# The narrowing must not swallow a passing-but-invalid run: identity is still asserted,
# and a non-capability failure still exits non-zero.
Assert-Contains $linuxSmoke 'assert_identity' 'Linux Vulkan smoke runner'
Assert-Contains `
    $linuxSmoke `
    'Avalonia Vulkan smoke exited with code' `
    'Linux Vulkan smoke runner'

# The restored WGL route and remaining routes back to full coverage are documented
# rather than left as tribal knowledge.
Assert-Contains $testingDoc '## Render gate capability limits' 'Testing documentation'
Assert-Contains $testingDoc '### Unblocking the WGL soak' 'Testing documentation'
Assert-Contains $testingDoc '### Unblocking the Vulkan composition gates' 'Testing documentation'
Assert-Contains $testingDoc 'mesa-wgl-test-runtime.lock.json' 'Testing documentation'
Assert-Contains $testingDoc 'GL_RENDERER' 'Testing documentation'
Assert-Contains $testingDoc 'VK_KHR_external_memory_win32' 'Testing documentation'
Assert-Contains $testingDoc 'VK_KHR_external_semaphore_win32' 'Testing documentation'
Assert-Contains $testingDoc 'CGLChoosePixelFormat failed with CGL error 10002' 'Testing documentation'
Assert-Contains $testingDoc 'supported image handles: (none)' 'Testing documentation'
Assert-Contains $testingDoc 'artifacts/render-capability/windows-vulkan.json' 'Testing documentation'
Assert-Contains $testingDoc 'artifacts/render-capability/macos-cgl.json' 'Testing documentation'

# The macOS CGL parity proof is required when an accelerated offline-capable
# pixel format exists, but hosted headless arm64 runners may have no such format.
Assert-Contains $renderWorkflow '- name: Resolve macOS CGL parity capability' 'Render workflow'
Assert-Contains $renderWorkflow "if: steps.macos-cgl.outputs.available == 'true'" 'Render workflow'
Assert-Contains $renderWorkflow 'artifacts/render-capability/macos-cgl.json' 'Render workflow'
$macosCglScript = Join-Path $repoRoot 'eng/resolve-macos-cgl-capability.ps1'
if (-not (Test-Path -LiteralPath $macosCglScript -PathType Leaf))
{
    throw "Missing macOS CGL capability resolver: $macosCglScript"
}
$macosCgl = Get-Content $macosCglScript -Raw
Assert-Contains $macosCgl 'CglPfaAllowOfflineRenderers' 'macOS CGL capability resolver'
Assert-Contains $macosCgl 'kCGLBadPixelFormat' 'macOS CGL capability resolver'
Assert-Contains $macosCgl "status = 'skipped'" 'macOS CGL capability resolver'

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

Assert-Contains `
    $testingDoc `
    'artifacts/platform-smoke/windows-wgl/mesa-wgl-runtime' `
    'Testing documentation'
Assert-Contains `
    $supportMatrixDoc `
    'testing.md#render-gate-capability-limits' `
    'Support matrix documentation'

Write-Output 'Render capability gate source contract passed.'
