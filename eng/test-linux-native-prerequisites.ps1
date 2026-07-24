#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$preflightPath = Join-Path $PSScriptRoot 'check-linux-native-prerequisites.ps1'
$packagePath = Join-Path $repoRoot '.github/workflows/package.yml'
$renderPath = Join-Path $repoRoot '.github/workflows/render.yml'
$nativePath = Join-Path $repoRoot '.github/workflows/native.yml'
$lockPath = Join-Path $PSScriptRoot 'openusd.lock.json'

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

function Assert-Matches
{
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if ($Value -notmatch $Pattern)
    {
        throw "$Context does not match '$Pattern'."
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

function Get-WorkflowStep
{
    param(
        [Parameter(Mandatory = $true)][string]$Workflow,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $marker = "- name: $Name"
    $start = $Workflow.IndexOf($marker, [StringComparison]::Ordinal)
    if ($start -lt 0)
    {
        throw "Workflow step '$Name' was not found."
    }
    $next = $Workflow.IndexOf(
        '      - name:',
        $start + $marker.Length,
        [StringComparison]::Ordinal)
    if ($next -lt 0)
    {
        $next = $Workflow.Length
    }
    return $Workflow.Substring($start, $next - $start)
}

$tokens = $null
$parseErrors = $null
[System.Management.Automation.Language.Parser]::ParseFile(
    $preflightPath,
    [ref]$tokens,
    [ref]$parseErrors) | Out-Null
if ($parseErrors.Count -ne 0)
{
    throw "Preflight PowerShell syntax errors: $($parseErrors -join [Environment]::NewLine)"
}

$lock = Get-Content $lockPath -Raw | ConvertFrom-Json
if ($lock.profile.enabled -notcontains 'materialX')
{
    throw 'The locked viewer-standard profile no longer enables MaterialX.'
}
$materialX = @($lock.dependencies | Where-Object name -EQ 'MaterialX')
if ($materialX.Count -ne 1 -or $materialX[0].version -cne '1.39.4')
{
    throw 'The Linux prerequisite contract expects locked MaterialX 1.39.4.'
}

$preflight = Get-Content $preflightPath -Raw
foreach ($command in @('git', 'tar', 'python', 'cmake', 'ninja', 'gcc', 'g++', 'pkg-config'))
{
    Assert-Contains $preflight "'$command'" 'Linux native prerequisite preflight'
}
Assert-Matches $preflight "'x11'\s*,\s*'xt'" 'Linux native prerequisite preflight'
Assert-Contains $preflight 'find_package(X11 REQUIRED)' 'Linux native prerequisite preflight'
Assert-Contains $preflight 'X11_Xt_FOUND' 'Linux native prerequisite preflight'
Assert-Contains $preflight 'X11::X11' 'Linux native prerequisite preflight'
Assert-Contains $preflight 'X11::Xt' 'Linux native prerequisite preflight'
Assert-Contains $preflight 'target_link_libraries(' 'Linux native prerequisite preflight'
Assert-Contains $preflight 'cmake --build' 'Linux native prerequisite preflight'
Assert-Contains $preflight 'libx11-dev libxt-dev' 'Linux native prerequisite preflight'

$packageWorkflow = Get-Content $packagePath -Raw
$renderWorkflow = Get-Content $renderPath -Raw
$nativeWorkflow = Get-Content $nativePath -Raw
foreach ($workflow in @($packageWorkflow, $renderWorkflow, $nativeWorkflow))
{
    Assert-Contains $workflow 'libx11-dev' 'Linux workflow'
    Assert-Contains $workflow './eng/test-linux-native-prerequisites.ps1' 'Linux workflow'
    Assert-Contains $workflow './eng/check-linux-native-prerequisites.ps1' 'Linux workflow'
}

$packageCommonInstall = Get-WorkflowStep `
    $packageWorkflow `
    'Install Linux package and NativeAOT prerequisites'
foreach ($buildOnly in @('build-essential', 'cmake', 'ninja-build', 'pkg-config', 'libxt-dev'))
{
    Assert-DoesNotContain $packageCommonInstall $buildOnly 'Package archive prerequisites'
}

$packageInstall = Get-WorkflowStep `
    $packageWorkflow `
    'Install Linux native build prerequisites'
Assert-Contains $packageInstall "env.LINUX_NATIVE_SOURCE == 'build'" 'Package prerequisite install'
Assert-Contains $packageInstall "steps.native-cache.outputs.cache-hit != 'true'" `
    'Package prerequisite install'
Assert-Contains $packageInstall 'build-essential cmake ninja-build' `
    'Package prerequisite install'
Assert-Contains $packageInstall 'pkg-config libxt-dev' 'Package prerequisite install'

$packagePreflight = Get-WorkflowStep `
    $packageWorkflow `
    'Preflight Linux native build prerequisites'
Assert-Contains $packagePreflight "env.LINUX_NATIVE_SOURCE == 'build'" 'Package preflight'
Assert-Contains $packagePreflight "steps.native-cache.outputs.cache-hit != 'true'" `
    'Package preflight'

$renderCommonInstall = Get-WorkflowStep `
    $renderWorkflow `
    'Install Linux presentation and NativeAOT prerequisites'
foreach ($buildOnly in @('build-essential', 'cmake', 'ninja-build', 'pkg-config', 'libxt-dev'))
{
    Assert-DoesNotContain $renderCommonInstall $buildOnly 'Render archive prerequisites'
}

$renderInstall = Get-WorkflowStep `
    $renderWorkflow `
    'Install Linux native build prerequisites'
Assert-Contains $renderInstall "env.NATIVE_SOURCE == 'build'" 'Render prerequisite install'
Assert-Contains $renderInstall 'build-essential cmake ninja-build' `
    'Render prerequisite install'
Assert-Contains $renderInstall 'pkg-config libxt-dev' 'Render prerequisite install'

$renderPreflight = Get-WorkflowStep `
    $renderWorkflow `
    'Preflight Linux native build prerequisites'
Assert-Contains $renderPreflight "env.NATIVE_SOURCE == 'build'" 'Render preflight'

Write-Output 'Linux native prerequisite preflight source contract passed.'
