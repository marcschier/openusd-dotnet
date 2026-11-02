#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$head = 'a' * 40
$merge = 'b' * 40
$oldHead = 'c' * 40
$previousScenario = Get-Variable -Name 'OpenUsdCheckoutReportScenario' -Scope Global -ErrorAction SilentlyContinue
$previousScenarioValue = if ($null -ne $previousScenario) { $previousScenario.Value } else { $null }

function git
{
    if ($args.Count -eq 2 -and $args[0] -eq 'rev-parse' -and $args[1] -eq 'HEAD')
    {
        $global:LASTEXITCODE = 0
        return $global:OpenUsdCheckoutReportScenario.Validated
    }

    if ($args.Count -ge 2 -and $args[0] -eq 'rev-parse' -and
        $args[-1] -like 'refs/remotes/origin/*')
    {
        if ($global:OpenUsdCheckoutReportScenario.BranchAvailable)
        {
            $global:LASTEXITCODE = 0
            return $global:OpenUsdCheckoutReportScenario.BranchHead
        }
        $global:LASTEXITCODE = 128
        return
    }

    if ($args.Count -ge 2 -and $args[0] -eq 'rev-parse' -and $args[-1] -like '*^2')
    {
        if ($global:OpenUsdCheckoutReportScenario.SecondParent)
        {
            $global:LASTEXITCODE = 0
            return $global:OpenUsdCheckoutReportScenario.SecondParent
        }
        $global:LASTEXITCODE = 128
        return
    }

    throw "Unexpected git invocation: $($args -join ' ')"
}

function Get-CheckoutReportSteps
{
    param([Parameter(Mandatory = $true)][string]$Path)

    $lines = [IO.File]::ReadAllLines($Path)
    for ($index = 0; $index -lt $lines.Length; $index++)
    {
        if ($lines[$index] -notmatch '^(\s*)- name: Report validated checkout commit\s*$')
        {
            continue
        }

        $indent = $Matches[1].Length
        while (++$index -lt $lines.Length -and $lines[$index] -notmatch '^\s*run:\s*\|\s*$')
        {
            if ($lines[$index] -match "^\s{$indent}- ")
            {
                throw "Checkout report step in '$Path' has no run block."
            }
        }
        if ($index -ge $lines.Length)
        {
            throw "Checkout report step in '$Path' has no run block."
        }

        $body = [System.Collections.Generic.List[string]]::new()
        $prefix = ' ' * ($indent + 4)
        while (++$index -lt $lines.Length)
        {
            $line = $lines[$index]
            if ([string]::IsNullOrWhiteSpace($line))
            {
                $body.Add('')
                continue
            }
            if (-not $line.StartsWith($prefix, [StringComparison]::Ordinal))
            {
                $index--
                break
            }
            $body.Add($line.Substring($prefix.Length))
        }

        [ScriptBlock]::Create($body -join "`n")
    }
}

$cases = @(
    @{
        Name = 'direct current checkout'; Event = 'workflow_dispatch'; Validated = $head
        BranchHead = $head; BranchAvailable = $true; Fallback = $head; SecondParent = ''
        ExpectedWarning = $false
    },
    @{
        Name = 'current pull-request merge'; Event = 'pull_request'; Validated = $merge
        BranchHead = $head; BranchAvailable = $true; Fallback = $head; SecondParent = $head
        ExpectedWarning = $false; ExpectedMergeNotice = $true
    },
    @{
        Name = 'pull-request merge without a local branch'; Event = 'pull_request'; Validated = $merge
        BranchHead = ''; BranchAvailable = $false; Fallback = $head; SecondParent = $head
        ExpectedWarning = $false; ExpectedMergeNotice = $true; ExpectedFallbackNotice = $true
    },
    @{
        Name = 'fork branch name collision'; Event = 'pull_request'; Validated = $merge
        BranchHead = $oldHead; BranchAvailable = $true; Fallback = $head; SecondParent = $head
        HeadRepository = 'fork/project'; ExpectedWarning = $false
        ExpectedMergeNotice = $true; ExpectedFallbackNotice = $true
    },
    @{
        Name = 'stale pull-request merge'; Event = 'pull_request'; Validated = $merge
        BranchHead = $head; BranchAvailable = $true; Fallback = $oldHead; SecondParent = $oldHead
        ExpectedWarning = $true; ExpectedMergeNotice = $true
    },
    @{
        Name = 'stale direct pull-request checkout'; Event = 'pull_request'; Validated = $oldHead
        BranchHead = $head; BranchAvailable = $true; Fallback = $oldHead; SecondParent = $head
        ExpectedWarning = $true; ForbiddenMergeNotice = $true
    },
    @{
        Name = 'stale workflow-run checkout'; Event = 'workflow_run'; Validated = $merge
        BranchHead = $head; BranchAvailable = $true; Fallback = $merge; SecondParent = $head
        ExpectedWarning = $true
    },
    @{
        Name = 'tag fallback'; Event = 'workflow_dispatch'; Validated = $head
        BranchHead = ''; BranchAvailable = $false; Fallback = $head; SecondParent = ''
        ExpectedWarning = $false
    },
    @{
        Name = 'missing merge parent'; Event = 'pull_request'; Validated = $merge
        BranchHead = $head; BranchAvailable = $true; Fallback = $head; SecondParent = ''
        ExpectedWarning = $true
    }
)

$workflows = @(
    @{ Path = [IO.Path]::Combine($repoRoot, '.github', 'workflows', 'package.yml'); Count = 1 },
    @{ Path = [IO.Path]::Combine($repoRoot, '.github', 'workflows', 'viewer-distribution.yml'); Count = 2 }
)
$failures = [System.Collections.Generic.List[string]]::new()
$oldBranch = $env:VALIDATED_BRANCH
$oldFallback = $env:CHECKOUT_FALLBACK_SHA
$oldEvent = $env:GITHUB_EVENT_NAME
$oldHeadRepository = $env:VALIDATED_PULL_REQUEST_REPOSITORY
$oldRepository = $env:GITHUB_REPOSITORY
Push-Location $repoRoot
try
{
    foreach ($workflow in $workflows)
    {
        $steps = @(Get-CheckoutReportSteps -Path $workflow.Path)
        if ($steps.Count -ne $workflow.Count)
        {
            throw "Expected $($workflow.Count) checkout report steps in '$($workflow.Path)', got $($steps.Count)."
        }
        foreach ($step in $steps)
        {
            foreach ($case in $cases)
            {
                $global:OpenUsdCheckoutReportScenario = $case
                $env:VALIDATED_BRANCH = 'topic'
                $env:CHECKOUT_FALLBACK_SHA = $case.Fallback
                $env:GITHUB_EVENT_NAME = $case.Event
                $env:GITHUB_REPOSITORY = 'base/project'
                $env:VALIDATED_PULL_REQUEST_REPOSITORY = if ($case.HeadRepository)
                {
                    $case.HeadRepository
                }
                else
                {
                    'base/project'
                }
                $global:LASTEXITCODE = 0
                try
                {
                    $output = @(& $step 6>&1)
                }
                catch
                {
                    throw "$($workflow.Path): $($case.Name): $($_.Exception.Message)`n$($_.ScriptStackTrace)"
                }
                $text = ($output | ForEach-Object { "$_" }) -join "`n"
                $warning = $text.Contains('WORKFLOW_CHECKOUT_STALE', [StringComparison]::Ordinal)
                $mergeNotice = $text.Contains('pull request merge', [StringComparison]::OrdinalIgnoreCase)
                $fallbackNotice = $text.Contains('triggering branch revision', [StringComparison]::Ordinal)
                if ($warning -ne $case.ExpectedWarning -or $global:LASTEXITCODE -ne 0 -or
                    -not $text.Contains('WORKFLOW_CHECKOUT_VALIDATION', [StringComparison]::Ordinal) -or
                    -not $text.Contains($case.Validated, [StringComparison]::Ordinal) -or
                    ($case.ExpectedMergeNotice -and -not $mergeNotice) -or
                    ($case.ForbiddenMergeNotice -and $mergeNotice) -or
                    ($case.ExpectedFallbackNotice -and -not $fallbackNotice))
                {
                    $failures.Add(
                        "$($workflow.Path): $($case.Name): expected warning=$($case.ExpectedWarning), " +
                        "exit=0; got warning=$warning, merge=$mergeNotice, " +
                        "fallback=$fallbackNotice, exit=$global:LASTEXITCODE.`n$text")
                }
            }
        }
    }
}
finally
{
    Pop-Location
    $env:VALIDATED_BRANCH = $oldBranch
    $env:CHECKOUT_FALLBACK_SHA = $oldFallback
    $env:GITHUB_EVENT_NAME = $oldEvent
    $env:VALIDATED_PULL_REQUEST_REPOSITORY = $oldHeadRepository
    $env:GITHUB_REPOSITORY = $oldRepository
    if ($null -eq $previousScenario)
    {
        Remove-Variable -Name 'OpenUsdCheckoutReportScenario' -Scope Global -ErrorAction SilentlyContinue
    }
    else
    {
        $global:OpenUsdCheckoutReportScenario = $previousScenarioValue
    }
}

if ($failures.Count -gt 0)
{
    throw ($failures -join "`n")
}

Write-Host '[checkout-report-test] All checkout-report scenarios passed.'
