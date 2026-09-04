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

# Resolve the repository-pinned host explicitly rather than trusting PATH.
#
# global.json pins the SDK and lists '.dotnet' ahead of '$host$' in its
# paths, so the whole point of the local install is a reproducible
# toolchain. But 'paths' governs SDK resolution only -- it does not change
# which dotnet host actually runs the tests. Invoking a bare 'dotnet' here
# resolved to the machine-wide host, which advertises runtimes the pinned
# install does not have, and that produced per-framework test counts for
# target frameworks the repository cannot actually execute. A test count
# that was never measured is the worst possible output from a test runner.
function Get-RepositoryDotnet
{
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $localName = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'dotnet.exe' } else { 'dotnet' }
    $localPath = Join-Path (Join-Path $RepositoryRoot '.dotnet') $localName
    if (Test-Path -LiteralPath $localPath)
    {
        return $localPath
    }

    $command = Get-Command 'dotnet' -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $command)
    {
        throw 'No dotnet host was found on PATH and the repository has no .dotnet install.'
    }
    return $command.Source
}

$script:DotnetHost = Get-RepositoryDotnet -RepositoryRoot $repoRoot
Write-Host "[managed-tests] host=$script:DotnetHost"

$script:SkippedFrameworks = @()

# Which Microsoft.NETCore.App major versions the pinned host can actually
# launch. Queried once from the host itself rather than assumed.
$script:HostRuntimeMajors = @(
    & $script:DotnetHost --list-runtimes 2>&1 |
        Where-Object { $_ -match '^Microsoft\.NETCore\.App\s+(\d+)\.' } |
        ForEach-Object { [int]$Matches[1] } |
        Sort-Object -Unique)
if ($script:HostRuntimeMajors.Count -eq 0)
{
    throw "'$script:DotnetHost --list-runtimes' reported no Microsoft.NETCore.App runtimes."
}

function Get-FrameworkRuntimeVersion
{
    param([Parameter(Mandatory = $true)][string]$TargetFramework)

    if ($TargetFramework -match '^net(?<major>\d+)\.(?<minor>\d+)$')
    {
        return "$($Matches['major']).$($Matches['minor'])"
    }
    return $TargetFramework
}

function Test-FrameworkExecutable
{
    param([Parameter(Mandatory = $true)][string]$TargetFramework)

    if ($TargetFramework -notmatch '^net(?<major>\d+)\.\d+$')
    {
        # Not a .NET Core style framework; let the run itself decide.
        return $true
    }
    return ([int]$Matches['major']) -in $script:HostRuntimeMajors
}

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

    $output = & $script:DotnetHost @arguments 2>&1
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
        -Rid $rid `
        -Activate
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
            -Property @('IsTestProject', 'OpenUsdRequiresNativeRuntime')
        if ($properties.IsTestProject -eq 'true')
        {
            if ($properties.OpenUsdRequiresNativeRuntime -eq 'true')
            {
                Write-Host (
                    "[managed-tests] SKIPPED $($candidate.BaseName): " +
                    'the project requires a staged OpenUSD native runtime. ' +
                    'Run eng/run-native-managed-tests.ps1 for native-backed managed tests.')
                continue
            }
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
            # Refuse to report a count for a framework the pinned host cannot
            # launch. Skipping is fine -- CI installs 8.0.x, 9.0.x and 10.0.301
            # and runs all three -- but it must be stated, not inferred. A
            # silent skip that still prints a total is indistinguishable from a
            # passing run and has already produced test counts that were never
            # measured.
            if (-not (Test-FrameworkExecutable -TargetFramework $targetFramework))
            {
                $version = Get-FrameworkRuntimeVersion -TargetFramework $targetFramework
                Write-Host (
                    "[managed-tests] SKIPPED $([System.IO.Path]::GetFileNameWithoutExtension($projectPath)) ($targetFramework): " +
                    "the pinned host has no Microsoft.NETCore.App $version runtime, " +
                    'so this framework builds but cannot execute here. ' +
                    'It is executed by CI.')
                $script:SkippedFrameworks += "$([System.IO.Path]::GetFileNameWithoutExtension($projectPath)) ($targetFramework)"
                continue
            }

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
$vulkanEnvironmentNames = @(
    'PATH',
    'LD_LIBRARY_PATH',
    'DYLD_LIBRARY_PATH',
    'VK_DRIVER_FILES',
    'VK_ICD_FILENAMES',
    'OPENUSD_REQUIRE_SWIFTSHADER',
    'OPENUSD_VULKAN_API_VERSION',
    'OPENUSD_VULKAN_DRIVER_PATH',
    'OPENUSD_VULKAN_DRIVER_SHA256',
    'OPENUSD_VULKAN_LOADER_PATH',
    'OPENUSD_VULKAN_LOADER_SHA256',
    'OPENUSD_VULKAN_MANIFEST_PATH'
)
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
    $oldVulkanEnvironment = @{}
    foreach ($name in $vulkanEnvironmentNames)
    {
        $oldVulkanEnvironment[$name] = [Environment]::GetEnvironmentVariable(
            $name,
            'Process')
    }
    try
    {
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

        if (-not [string]::IsNullOrWhiteSpace($swiftShaderIcd))
        {
            Write-Host "[managed-tests] Using packaged SwiftShader ICD: $swiftShaderIcd"
        }

        & $script:DotnetHost @arguments
        $exitCode = $LASTEXITCODE
    }
    finally
    {
        foreach ($name in $vulkanEnvironmentNames)
        {
            if ($null -eq $oldVulkanEnvironment[$name])
            {
                Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue
            }
            else
            {
                [Environment]::SetEnvironmentVariable(
                    $name,
                    $oldVulkanEnvironment[$name],
                    'Process')
            }
        }
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
if ($script:SkippedFrameworks.Count -gt 0)
{
    # State skips in the summary, not only where they happened. A reader who
    # sees only the pass count must not mistake it for full framework coverage.
    Write-Host (
        "[managed-tests] NOT EXECUTED here ($($script:SkippedFrameworks.Count)): " +
        ($script:SkippedFrameworks -join ', '))
    Write-Host (
        '[managed-tests] Those target frameworks build but have no matching ' +
        'runtime in the pinned host. CI installs 8.0.x, 9.0.x and 10.0.301 ' +
        'and runs them there. Do not report a count for them from this run.')
}
if ($failures.Count -gt 0)
{
    foreach ($failure in $failures)
    {
        Write-Error "[managed-tests] $failure" -ErrorAction Continue
    }
    exit 1
}
