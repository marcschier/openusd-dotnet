#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$Configuration = 'Release'
$dotnet = Join-Path $repoRoot $(if ($IsWindows) { '.dotnet\dotnet.exe' } else { '.dotnet/dotnet' })
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf))
{
    $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop |
        Select-Object -First 1).Source
}
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot 'run-parity-capture.ps1'), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw 'The parity capture script does not parse.' }
$resolver = $ast.FindAll({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq 'Resolve-ParityTestHostRoot'
}, $false)
foreach ($function in $resolver)
{
    . ([scriptblock]::Create($function.Extent.Text))
}
$assignment = @($ast.EndBlock.Statements | Where-Object {
    $_ -is [Management.Automation.Language.AssignmentStatementAst] -and
        $_.Left.Extent.Text -ceq '$testHostRoot'
})
if ($assignment.Count -ne 1) { throw 'Expected one authoritative parity test-host root assignment.' }
$project = Join-Path $repoRoot 'tests/OpenUsd.Rendering.ConformanceTests/OpenUsd.Rendering.ConformanceTests.csproj'
$oldPlatform = $env:Platform
try
{
    foreach ($platform in @('', 'x64'))
    {
        $env:Platform = $platform
        $target = @(& $dotnet msbuild $project -nologo "-p:Configuration=$Configuration" `
            '-p:TargetFramework=net10.0' '-getProperty:TargetPath')
        if ($LASTEXITCODE -ne 0 -or $target.Count -ne 1 -or
            -not [IO.Path]::IsPathFullyQualified($target[0]))
        {
            throw 'Could not evaluate the actual managed test assembly path.'
        }
        . ([scriptblock]::Create($assignment[0].Extent.Text))
        $expected = [IO.Path]::GetDirectoryName($target[0])
        if ($testHostRoot -cne $expected)
        {
            throw "Platform='$platform': parity host '$testHostRoot' does not match actual test host '$expected'."
        }
        Write-Output "PASS ParityHostMatchesTargetPath(Platform='$platform')"
    }
}
finally
{
    $env:Platform = $oldPlatform
}

$workflow = [IO.File]::ReadAllText((Join-Path $repoRoot '.github/workflows/render.yml'))
$job = [regex]::Match($workflow,
    '(?ms)^  windows-wgl:\r?\n(?<job>.*?)(?=^  [A-Za-z0-9_-]+:|\z)').Groups['job'].Value
$upload = [regex]::Match($job,
    '(?ms)^      - name: Upload diagnostics\r?\n(?<step>.*?)(?=^      - |\z)').Groups['step'].Value
if (-not $upload.Contains('tests/OpenUsd.Rendering.ConformanceTests/bin/**/TestResults'))
{
    throw 'WindowsParityReportsAreRetained: diagnostic upload must include platform-specific test output.'
}
Write-Output 'PASS WindowsParityReportsAreRetained'
