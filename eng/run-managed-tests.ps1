#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Alias('Projects')]
    [string[]]$Project,

    [Alias('Frameworks')]
    [string[]]$Framework,

    [Alias('Config')]
    [ValidateNotNullOrEmpty()]
    [string]$Configuration = 'Release',

    [ValidateRange(1, [int]::MaxValue)]
    [int]$MinimumExpectedTests = 1,

    [Alias('TestArgs')]
    [string[]]$TestArguments = @()
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Get-ProjectProperties
{
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string[]]$Property,
        [string]$TargetFramework
    )

    $arguments = @(
        'msbuild',
        $ProjectPath,
        '-nologo',
        "-getProperty:$($Property -join ',')",
        "-p:Configuration=$Configuration"
    )
    if (-not [string]::IsNullOrWhiteSpace($TargetFramework))
    {
        $arguments += "-p:TargetFramework=$TargetFramework"
    }

    $output = & dotnet @arguments 2>&1
    if ($LASTEXITCODE -ne 0)
    {
        throw "MSBuild could not evaluate '$ProjectPath':$([Environment]::NewLine)$($output -join [Environment]::NewLine)"
    }

    if ($Property.Count -eq 1)
    {
        $properties = [pscustomobject]@{}
        $properties | Add-Member `
            -MemberType NoteProperty `
            -Name $Property[0] `
            -Value (($output -join [Environment]::NewLine).Trim())
        return $properties
    }

    try
    {
        return (($output -join [Environment]::NewLine) | ConvertFrom-Json).Properties
    }
    catch
    {
        throw "MSBuild returned invalid property data for '$ProjectPath':$([Environment]::NewLine)$($output -join [Environment]::NewLine)"
    }
}

function Resolve-ProjectPath
{
    param([Parameter(Mandatory = $true)][string]$Path)

    $candidate = if ([System.IO.Path]::IsPathRooted($Path))
    {
        $Path
    }
    else
    {
        Join-Path $repoRoot $Path
    }

    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf))
    {
        throw "Test project does not exist: $Path"
    }

    $resolved = (Resolve-Path -LiteralPath $candidate).Path
    if ([System.IO.Path]::GetExtension($resolved) -ne '.csproj')
    {
        throw "Test project must be a .csproj file: $Path"
    }

    return $resolved
}

function Prepare-PackagedSwiftShaderIcd
{
    param(
        [Parameter(Mandatory = $true)][string]$AssemblyName,
        [Parameter(Mandatory = $true)][string]$TestDll
    )

    if ($AssemblyName -cne 'OpenUsd.Rendering.ConformanceTests' -or
        (-not $IsWindows -and -not $IsLinux) -or
        -not [string]::IsNullOrWhiteSpace($env:VK_DRIVER_FILES) -or
        -not [string]::IsNullOrWhiteSpace($env:VK_ICD_FILENAMES))
    {
        return $null
    }

    $rid = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
    return & (Join-Path $PSScriptRoot 'prepare-vulkan-test-runtime.ps1') `
        -Root (Split-Path -Parent $TestDll) `
        -Rid $rid
}

if ($TestArguments -contains '--minimum-expected-tests' -or
    $TestArguments -match '^--minimum-expected-tests=')
{
    throw 'Use -MinimumExpectedTests instead of passing --minimum-expected-tests as a test argument.'
}

$projectPaths = [System.Collections.Generic.List[string]]::new()
if ($null -ne $Project -and $Project.Count -gt 0)
{
    foreach ($path in $Project)
    {
        $projectPaths.Add((Resolve-ProjectPath $path))
    }
}
else
{
    $testRoot = Join-Path $repoRoot 'tests'
    foreach ($candidate in Get-ChildItem -LiteralPath $testRoot -Recurse -Filter '*.csproj' -File |
        Sort-Object FullName)
    {
        $properties = Get-ProjectProperties `
            -ProjectPath $candidate.FullName `
            -Property @('IsTestProject')
        if ($properties.IsTestProject -eq 'true')
        {
            $projectPaths.Add($candidate.FullName)
        }
    }
}

if ($projectPaths.Count -eq 0)
{
    throw 'No managed test projects were found.'
}

$runs = [System.Collections.Generic.List[object]]::new()
$failures = [System.Collections.Generic.List[string]]::new()
foreach ($projectPath in $projectPaths)
{
    try
    {
        $properties = Get-ProjectProperties `
            -ProjectPath $projectPath `
            -Property @(
                'IsTestProject',
                'TargetFrameworks',
                'TargetFramework',
                'AssemblyName'
            )
        if ($properties.IsTestProject -ne 'true')
        {
            throw "'$projectPath' is not a test project."
        }

        $declaredFrameworks = if (-not [string]::IsNullOrWhiteSpace($properties.TargetFrameworks))
        {
            @($properties.TargetFrameworks -split ';' |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        }
        elseif (-not [string]::IsNullOrWhiteSpace($properties.TargetFramework))
        {
            @($properties.TargetFramework)
        }
        else
        {
            throw "'$projectPath' does not declare a target framework."
        }

        $selectedFrameworks = if ($null -ne $Framework -and $Framework.Count -gt 0)
        {
            foreach ($requestedFramework in $Framework)
            {
                if ($requestedFramework -notin $declaredFrameworks)
                {
                    throw "'$projectPath' does not target '$requestedFramework'."
                }
            }
            @($Framework)
        }
        else
        {
            $declaredFrameworks
        }

        foreach ($targetFramework in $selectedFrameworks)
        {
            $targetProperties = Get-ProjectProperties `
                -ProjectPath $projectPath `
                -Property @('AssemblyName', 'OutputPath', 'TargetPath') `
                -TargetFramework $targetFramework
            if ([string]::IsNullOrWhiteSpace($targetProperties.AssemblyName) -or
                [string]::IsNullOrWhiteSpace($targetProperties.OutputPath) -or
                [string]::IsNullOrWhiteSpace($targetProperties.TargetPath))
            {
                throw "Could not resolve the output DLL for '$projectPath' ($targetFramework)."
            }

            $expectedName = "$($targetProperties.AssemblyName).dll"
            if ([System.IO.Path]::GetFileName($targetProperties.TargetPath) -ne $expectedName)
            {
                throw "Resolved target '$($targetProperties.TargetPath)' does not match assembly '$expectedName'."
            }

            $runs.Add([pscustomobject]@{
                Project = $projectPath
                Framework = $targetFramework
                AssemblyName = $targetProperties.AssemblyName
                OutputPath = $targetProperties.OutputPath
                Dll = $targetProperties.TargetPath
            })
        }
    }
    catch
    {
        $failures.Add($_.Exception.Message)
    }
}

$passed = 0
foreach ($run in $runs)
{
    $label = "$($run.AssemblyName) ($($run.Framework))"
    if (-not (Test-Path -LiteralPath $run.Dll -PathType Leaf))
    {
        $failures.Add("$label is missing its built test DLL: $($run.Dll)")
        continue
    }

    Write-Host "[managed-tests] Running $label"
    $arguments = @(
        $run.Dll,
        '--minimum-expected-tests',
        $MinimumExpectedTests,
        '--no-ansi',
        '--progress',
        'off'
    )
    $arguments += $TestArguments
    try
    {
        $swiftShaderIcd = Prepare-PackagedSwiftShaderIcd `
            -AssemblyName $run.AssemblyName `
            -TestDll $run.Dll
    }
    catch
    {
        $failures.Add("$label could not configure Vulkan conformance: $($_.Exception.Message)")
        continue
    }

    $oldDriverFiles = $env:VK_DRIVER_FILES
    $oldIcdFilenames = $env:VK_ICD_FILENAMES
    $oldRequireSwiftShader = $env:OPENUSD_REQUIRE_SWIFTSHADER
    try
    {
        if (-not [string]::IsNullOrWhiteSpace($swiftShaderIcd))
        {
            Write-Host "[managed-tests] Using packaged SwiftShader ICD: $swiftShaderIcd"
            $env:VK_DRIVER_FILES = $swiftShaderIcd
            $env:VK_ICD_FILENAMES = $swiftShaderIcd
            $env:OPENUSD_REQUIRE_SWIFTSHADER = '1'
        }

        & dotnet @arguments
        $exitCode = $LASTEXITCODE
    }
    finally
    {
        $env:VK_DRIVER_FILES = $oldDriverFiles
        $env:VK_ICD_FILENAMES = $oldIcdFilenames
        $env:OPENUSD_REQUIRE_SWIFTSHADER = $oldRequireSwiftShader
    }

    if ($exitCode -eq 0)
    {
        $passed++
        Write-Host "[managed-tests] Passed $label"
    }
    else
    {
        $failures.Add("$label exited with code $exitCode.")
    }
}

Write-Host "[managed-tests] Summary: $passed passed, $($failures.Count) failed."
if ($failures.Count -gt 0)
{
    foreach ($failure in $failures)
    {
        Write-Error "[managed-tests] $failure" -ErrorAction Continue
    }
    exit 1
}
