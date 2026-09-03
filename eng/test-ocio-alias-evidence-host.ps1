#!/usr/bin/env pwsh
# Copyright (c) marcschier, Licensed under the MIT License.
<#
.SYNOPSIS
    Executes the OpenColorIO alias evidence gate's dotnet host resolution.

.DESCRIPTION
    The gate builds the test project before running it, so it needs a host. Invoking the
    repository-pinned path without testing for it first is not a fallback: under Stop
    semantics a missing path raises a terminating CommandNotFoundException before any
    exit code can be examined, so a runner without the local install failed with an error
    about a path instead of building with the host it did have.

    The production function is extracted from the gate script itself rather than copied,
    so this exercises the real resolution rather than a restatement of it.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gatePath = Join-Path $PSScriptRoot 'test-ocio-alias-evidence.ps1'

$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $gatePath,
    [ref]$tokens,
    [ref]$errors)
if ($errors.Count -ne 0)
{
    throw "The OpenColorIO alias evidence gate does not parse: $($errors[0].Message)"
}

$definition = $ast.FindAll(
    {
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq 'Resolve-DotnetHost'
    },
    $true) | Select-Object -First 1
if ($null -eq $definition)
{
    throw 'The OpenColorIO alias evidence gate no longer defines Resolve-DotnetHost.'
}

# The production function, verbatim.
. ([scriptblock]::Create($definition.Extent.Text))

$scratch = Join-Path $repoRoot "artifacts/ocio-alias-host-$([guid]::NewGuid().ToString('N'))"
$executable = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'dotnet.exe' } else { 'dotnet' }
$failures = [System.Collections.Generic.List[string]]::new()
try
{
    New-Item -ItemType Directory -Path $scratch -Force | Out-Null

    # 1. The pinned host is preferred when it exists.
    $pinnedDirectory = Join-Path $scratch '.dotnet'
    New-Item -ItemType Directory -Path $pinnedDirectory -Force | Out-Null
    $pinned = Join-Path $pinnedDirectory $executable
    Set-Content -LiteralPath $pinned -Value '' -NoNewline
    $resolvedPinned = Resolve-DotnetHost -RepositoryRoot $scratch
    if ($resolvedPinned -ne $pinned)
    {
        $failures.Add("Pinned host was not preferred: got '$resolvedPinned'.")
    }

    # 2. With no pinned install, the PATH host is used rather than a missing path being
    #    invoked.
    Remove-Item -LiteralPath $pinnedDirectory -Recurse -Force
    $pathHost = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $pathHost)
    {
        $failures.Add('No dotnet is on PATH, so the PATH-host case could not be proven.')
    }
    else
    {
        $resolvedPath = Resolve-DotnetHost -RepositoryRoot $scratch
        if ($resolvedPath -ne $pathHost.Source)
        {
            $failures.Add(
                "PATH host was not used: got '$resolvedPath', expected " +
                "'$($pathHost.Source)'.")
        }
        if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf))
        {
            $failures.Add("The resolved PATH host does not exist: '$resolvedPath'.")
        }
    }

    # 3. With neither, the failure names the problem instead of surfacing as a missing
    #    command.
    $alias = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
    $savedPath = $env:PATH
    try
    {
        $env:PATH = $scratch
        $threw = $false
        try
        {
            $null = Resolve-DotnetHost -RepositoryRoot $scratch
        }
        catch
        {
            $threw = $true
            if (-not $_.Exception.Message.Contains(
                    'No dotnet host was found.',
                    [StringComparison]::Ordinal))
            {
                $failures.Add("Unexpected missing-host message: $($_.Exception.Message)")
            }
        }
        if (-not $threw)
        {
            $failures.Add('A missing host did not produce an explicit error.')
        }
    }
    finally
    {
        $env:PATH = $savedPath
        $null = $alias
    }
}
finally
{
    if (Test-Path -LiteralPath $scratch)
    {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
}

if ($failures.Count -ne 0)
{
    throw (
        'OpenColorIO alias evidence host resolution failed: ' +
        ($failures -join '; '))
}

Write-Output 'OpenColorIO alias evidence host resolution passed.'
