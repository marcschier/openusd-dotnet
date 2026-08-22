#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'run-mcp.ps1'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    $scriptPath,
    [ref]$tokens,
    [ref]$parseErrors)
if ($parseErrors.Count -ne 0)
{
    throw "run-mcp.ps1 has parse errors: $($parseErrors -join '; ')"
}

$definitions = @($ast.FindAll(
    {
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -ceq 'Join-LoaderPath'
    },
    $true))
if ($definitions.Count -ne 1)
{
    throw "Expected one Join-LoaderPath definition, found $($definitions.Count)."
}
Invoke-Expression $definitions[0].Extent.Text

function Assert-Equal
{
    param(
        [AllowNull()][string]$Expected,
        [AllowNull()][string]$Actual,
        [Parameter(Mandatory = $true)][string]$Scenario
    )

    if ($Actual -cne $Expected)
    {
        throw "$Scenario expected '$Expected', got '$Actual'."
    }
}

function Assert-SafeColonPath
{
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Scenario
    )

    if ($Value.StartsWith(':', [StringComparison]::Ordinal) -or
        $Value.EndsWith(':', [StringComparison]::Ordinal) -or
        $Value.Contains('::', [StringComparison]::Ordinal))
    {
        throw "$Scenario produced an unsafe loader path '$Value'."
    }
}

$cases = @(
    @{
        Scenario = 'unset existing path'
        Path = @('/runtime', '/runtime/bin', '/runtime/lib', $null)
        Separator = ':'
        Expected = '/runtime:/runtime/bin:/runtime/lib'
    },
    @{
        Scenario = 'empty existing path'
        Path = @('/runtime', '/runtime/bin', '/runtime/lib', '')
        Separator = ':'
        Expected = '/runtime:/runtime/bin:/runtime/lib'
    },
    @{
        Scenario = 'one existing path'
        Path = @('/runtime', '/runtime/bin', '/runtime/lib', '/user/lib')
        Separator = ':'
        Expected = '/runtime:/runtime/bin:/runtime/lib:/user/lib'
    },
    @{
        Scenario = 'multiple existing paths'
        Path = @('/runtime', '/runtime/bin', '/runtime/lib', '/first:/second')
        Separator = ':'
        Expected = '/runtime:/runtime/bin:/runtime/lib:/first:/second'
    },
    @{
        Scenario = 'empty existing path segments'
        Path = @('/runtime', ':/first::/second:')
        Separator = ':'
        Expected = '/runtime:/first:/second'
    },
    @{
        Scenario = 'duplicates'
        Path = @('/runtime', '/runtime/lib', '/runtime:/user/lib:/runtime/lib')
        Separator = ':'
        Expected = '/runtime:/runtime/lib:/user/lib'
    },
    @{
        Scenario = 'alternate separator'
        Path = @('/runtime', ';/first;;/second;')
        Separator = ';'
        Expected = '/runtime;/first;/second'
    })

foreach ($case in $cases)
{
    $actual = Join-LoaderPath `
        -Path $case.Path `
        -Separator ([char]$case.Separator)
    Assert-Equal $case.Expected $actual $case.Scenario
    if ($case.Separator -ceq ':')
    {
        Assert-SafeColonPath $actual $case.Scenario
    }
}

$scriptText = Get-Content -LiteralPath $scriptPath -Raw
foreach ($name in @('LD_LIBRARY_PATH', 'DYLD_LIBRARY_PATH'))
{
    if ($scriptText -notmatch
        "\`$env:$name\s*=\s*Join-LoaderPath\s+-Path")
    {
        throw "$name is not composed with Join-LoaderPath."
    }
}

Write-Output 'RUN_MCP_LOADER_PATH passed'
