#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runner = Join-Path $PSScriptRoot 'run-managed-tests.ps1'
$testProject = Join-Path $repoRoot 'tests/OpenUsd.Tests/OpenUsd.Tests.csproj'
$conformanceProject = Join-Path $repoRoot (
    'tests/OpenUsd.Rendering.ConformanceTests/' +
    'OpenUsd.Rendering.ConformanceTests.csproj')
$productionProject = Join-Path $repoRoot 'src/OpenUsd/OpenUsd.csproj'
$pwsh = (Get-Command pwsh -ErrorAction Stop).Source
$failures = [System.Collections.Generic.List[string]]::new()

function ConvertTo-SingleQuotedLiteral
{
    param([Parameter(Mandatory = $true)][string]$Value)

    return "'$($Value.Replace("'", "''"))'"
}

function Invoke-RunnerCase
{
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Invocation,
        [Parameter(Mandatory = $true)][bool]$ShouldPass,
        [string]$ExpectedOutput
    )

    $output = & $pwsh -NoLogo -NoProfile -Command $Invocation 2>&1
    $exitCode = $LASTEXITCODE
    $passed = ($exitCode -eq 0)
    $text = $output -join [Environment]::NewLine
    $outputMatches = [string]::IsNullOrWhiteSpace($ExpectedOutput) -or
        $text -match $ExpectedOutput
    if ($passed -ne $ShouldPass -or -not $outputMatches)
    {
        $failures.Add(
            "$Name expected pass=$ShouldPass and output='$ExpectedOutput' " +
            "but exited $exitCode.`n$text")
        Write-Error "[managed-tests-self-test] Failed: $Name" -ErrorAction Continue
    }
    else
    {
        Write-Host "[managed-tests-self-test] Passed: $Name"
    }
}

$runnerLiteral = ConvertTo-SingleQuotedLiteral $runner
$testProjectLiteral = ConvertTo-SingleQuotedLiteral $testProject
$conformanceProjectLiteral = ConvertTo-SingleQuotedLiteral $conformanceProject
$productionProjectLiteral = ConvertTo-SingleQuotedLiteral $productionProject

$testingDocs = Get-Content (Join-Path $repoRoot 'docs/testing.md') -Raw
if ($testingDocs -match 'dotnet test' -and
    $testingDocs -match 'zero executed tests' -and
    $testingDocs -match 'exit with code 5' -and
    $testingDocs -match 'run-managed-tests\.ps1')
{
    Write-Host '[managed-tests-self-test] Passed: legacy runner behavior documentation'
}
else
{
    $failures.Add('docs/testing.md does not document the legacy zero-test failure and direct runner.')
    Write-Error `
        '[managed-tests-self-test] Failed: legacy runner behavior documentation' `
        -ErrorAction Continue
}

Invoke-RunnerCase `
    -Name 'direct test assembly run' `
    -ShouldPass $true `
    -Invocation "& $runnerLiteral -Project $testProjectLiteral -Framework net10.0 -Configuration Release"
Invoke-RunnerCase `
    -Name 'packaged SwiftShader conformance' `
    -ShouldPass $true `
    -ExpectedOutput '\[managed-tests\] Using packaged SwiftShader ICD:' `
    -Invocation (
        "& $runnerLiteral -Project $conformanceProjectLiteral " +
        "-Framework net10.0 -Configuration Release " +
        "-TestArguments @('--treenode-filter'," +
        "'/*/*/VulkanDeviceTests/CreatesQueueAndBufferWhenVulkanIsAvailable')")
Invoke-RunnerCase `
    -Name 'repeated packaged SwiftShader conformance' `
    -ShouldPass $true `
    -ExpectedOutput '\[managed-tests\] Using packaged SwiftShader ICD:' `
    -Invocation (
        "& $runnerLiteral -Project $conformanceProjectLiteral " +
        "-Framework net10.0 -Configuration Release " +
        "-TestArguments @('--treenode-filter'," +
        "'/*/*/VulkanDeviceTests/CreatesQueueAndBufferWhenVulkanIsAvailable'); " +
        "if (`$LASTEXITCODE -ne 0) { exit `$LASTEXITCODE }; " +
        "& $runnerLiteral -Project $conformanceProjectLiteral " +
        "-Framework net10.0 -Configuration Release " +
        "-TestArguments @('--treenode-filter'," +
        "'/*/*/VulkanDeviceTests/CreatesQueueAndBufferWhenVulkanIsAvailable')")
Invoke-RunnerCase `
    -Name 'non-test project rejection' `
    -ShouldPass $false `
    -Invocation "& $runnerLiteral -Project $productionProjectLiteral -Framework net10.0 -Configuration Release"
Invoke-RunnerCase `
    -Name 'missing binary rejection' `
    -ShouldPass $false `
    -Invocation "& $runnerLiteral -Project $testProjectLiteral -Framework net10.0 -Configuration ManagedRunnerMissing"
Invoke-RunnerCase `
    -Name 'malformed tree filter rejection' `
    -ShouldPass $false `
    -Invocation "& $runnerLiteral -Project $testProjectLiteral -Framework net10.0 -Configuration Release -TestArguments @('--treenode-filter','[')"
Invoke-RunnerCase `
    -Name 'zero matched tests rejection' `
    -ShouldPass $false `
    -Invocation "& $runnerLiteral -Project $testProjectLiteral -Framework net10.0 -Configuration Release -TestArguments @('--treenode-filter','/*/*/DefinitelyMissingManagedRunnerTests/*')"

if ($failures.Count -gt 0)
{
    foreach ($failure in $failures)
    {
        Write-Error $failure -ErrorAction Continue
    }
    exit 1
}

Write-Host '[managed-tests-self-test] All cases passed.'
