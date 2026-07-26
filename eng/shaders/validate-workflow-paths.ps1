#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [string]$WorkflowPath = (Join-Path $PSScriptRoot '../../.github/workflows/shaders.yml')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$WorkflowPath = [System.IO.Path]::GetFullPath($WorkflowPath)
if (-not (Test-Path $WorkflowPath))
{
    throw "Shader workflow was not found at $WorkflowPath."
}

$content = Get-Content $WorkflowPath -Raw
$matches = [regex]::Matches(
    $content,
    '\./eng/shaders/[a-zA-Z0-9._-]+\.ps1')
$paths = $matches.Value | Sort-Object -Unique
if (-not $paths)
{
    throw 'The shader workflow does not reference any local PowerShell scripts.'
}

foreach ($path in $paths)
{
    $relativePath = $path.Substring(2).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    $fullPath = Join-Path $repoRoot $relativePath
    if (-not (Test-Path $fullPath -PathType Leaf))
    {
        throw "Workflow script path does not exist: $path"
    }
}

$actionMatches = [regex]::Matches(
    $content,
    'uses:\s+[a-zA-Z0-9_.-]+/[a-zA-Z0-9_.-]+@([^\s#]+)')
foreach ($actionMatch in $actionMatches)
{
    $reference = $actionMatch.Groups[1].Value
    if ($reference -notmatch '^[0-9a-f]{40}$')
    {
        throw "Workflow action is not pinned to a commit SHA: $reference"
    }
}
if ($content.Contains('eng/shaders/.cache/build') -or
    $content.Contains('eng/shaders/.tools'))
{
    throw 'Shader workflow must not cache build trees or tools.'
}
if (-not $content.Contains('eng/shaders/.cache/downloads'))
{
    throw 'Shader workflow must cache the pinned download archive directory.'
}
$lineEndingChecks = [regex]::Matches(
    $content,
    'run:\s+\./eng/shaders/test-checked-input-line-endings\.ps1')
if ($lineEndingChecks.Count -ne 3)
{
    throw 'Every shader platform job must validate checked input line endings.'
}
$fullHistoryCheckouts = [regex]::Matches(
    $content,
    '(?ms)uses:\s+actions/checkout@[0-9a-f]{40}\s+# v4\s*\r?\n' +
        '\s+with:\s*\r?\n\s+fetch-depth:\s+0')
if ($fullHistoryCheckouts.Count -ne 3)
{
    throw 'Every shader platform job must use a full-history pinned checkout.'
}

function Get-WorkflowJob
{
    param([Parameter(Mandatory = $true)][string]$Name)

    $pattern = (
        '(?ms)^  ' +
        [regex]::Escape($Name) +
        ':\r?\n.*?(?=^  [a-zA-Z0-9_-]+:\r?\n|\z)')
    $match = [regex]::Match($content, $pattern)
    if (-not $match.Success)
    {
        throw "Shader workflow job was not found: $Name"
    }
    return $match.Value
}

function Assert-StepArtifactScope
{
    param(
        [Parameter(Mandatory = $true)][string]$Job,
        [Parameter(Mandatory = $true)][string]$Script,
        [string]$Scope = 'Spirv',
        [string]$Platform = 'Linux'
    )

    $pattern = (
        '(?ms)^[ \t]*' +
        [regex]::Escape("./eng/shaders/$Script") +
        '.*?(?=^ {6}- name:|\z)')
    $match = [regex]::Match($Job, $pattern)
    if (-not $match.Success)
    {
        throw "$Platform shader workflow does not invoke $Script."
    }
    if ($match.Value -notmatch "-ArtifactScope\s+$Scope")
    {
        throw "$Platform $Script invocation must use -ArtifactScope $Scope."
    }
}

$windowsJob = Get-WorkflowJob -Name 'windows-authoritative'
$linuxJob = Get-WorkflowJob -Name 'linux-validation'
Assert-StepArtifactScope `
    -Job $linuxJob `
    -Script 'build-shaders.ps1'
Assert-StepArtifactScope `
    -Job $linuxJob `
    -Script 'verify-reproducibility.ps1'
if ([regex]::Matches($linuxJob, '-ArtifactScope\s+Spirv').Count -ne 2)
{
    throw 'Linux shader workflow must contain exactly two SPIR-V scope gates.'
}
$macosJob = Get-WorkflowJob -Name 'macos-arm64'
Assert-StepArtifactScope `
    -Job $macosJob `
    -Script 'build-shaders.ps1' `
    -Scope 'Metal' `
    -Platform 'macOS'
Assert-StepArtifactScope `
    -Job $macosJob `
    -Script 'verify-reproducibility.ps1' `
    -Scope 'Metal' `
    -Platform 'macOS'
if ([regex]::Matches($macosJob, '-ArtifactScope\s+Metal').Count -ne 2)
{
    throw 'macOS shader workflow must contain exactly two Metal scope gates.'
}
foreach ($script in @(
    'validate-checked-payload.ps1',
    'test-checked-corruption.ps1'))
{
    if (-not $linuxJob.Contains("./eng/shaders/$script"))
    {
        throw "Linux shader workflow must retain full checked validation: $script"
    }
}
if ($windowsJob -match '-ArtifactScope\s+Spirv')
{
    throw 'Windows authoritative shader generation must retain the full scope.'
}

Write-Host (
    "Validated $($paths.Count) script paths and " +
    "$($actionMatches.Count) pinned actions.")
