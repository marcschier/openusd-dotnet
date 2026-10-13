#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [string]$BuildRoot = (Join-Path $PSScriptRoot '..\native\build\shim\win-x64'),
    [string]$RuntimeRoot = (Join-Path $PSScriptRoot '..\artifacts\windows-native-tests\mesa'),
    [string]$Configuration = 'Release',
    [string]$TestFilter
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows)
{
    throw 'This runner requires Windows.'
}
$build = (Resolve-Path -LiteralPath $BuildRoot).Path
$runtime = [IO.Path]::GetFullPath($RuntimeRoot)
$ctest = (Get-Command ctest -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$testJson = & $ctest --test-dir $build -C $Configuration --show-only=json-v1
if ($LASTEXITCODE -ne 0)
{
    throw 'Could not discover the native CTest executables.'
}
$tests = ($testJson -join "`n" | ConvertFrom-Json).tests
$stormTests = @($tests | Where-Object { $_.name -like 'openusd_storm_*' -and $_.command.Count -gt 0 })
foreach ($required in @('openusd_storm_child_aov_probe', 'openusd_storm_child_aov_resize_probe'))
{
    if ($required -notin $stormTests.name)
    {
        throw "The build tree is missing required Storm AOV test '$required'."
    }
}
$directories = @($stormTests | ForEach-Object {
    $executable = [IO.Path]::GetFullPath($_.command[0])
    $relative = [IO.Path]::GetRelativePath($build, $executable)
    if ([IO.Path]::IsPathRooted($relative) -or $relative.StartsWith('..') -or
        -not (Test-Path -LiteralPath $executable -PathType Leaf))
    {
        throw "Storm probe is missing or outside the selected build tree: $executable"
    }
    Split-Path -Parent $executable
} | Sort-Object -Unique)

$names = @('PATH', 'OPENUSD_MESA_WGL_OPENGL32_PATH', 'OPENUSD_MESA_WGL_OPENGL32_SHA256',
    'OPENUSD_MESA_WGL_ARCHIVE_URL', 'OPENUSD_MESA_WGL_ARCHIVE_SHA256',
    'GALLIUM_DRIVER', 'LIBGL_ALWAYS_SOFTWARE', 'LP_NUM_THREADS', 'MESA_SHADER_CACHE_DISABLE')
$previous = @{}
foreach ($name in $names)
{
    $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$created = [Collections.Generic.List[string]]::new()
$created.Capacity = $directories.Count
$expected = $null
$exitCode = 1
try
{
    $stage = & (Join-Path $PSScriptRoot 'prepare-mesa-wgl-test-runtime.ps1') `
        -Root $runtime -Activate -Preflight
    $source = Join-Path $stage 'opengl32.dll'
    $expected = $env:OPENUSD_MESA_WGL_OPENGL32_SHA256
    foreach ($directory in $directories)
    {
        $destination = Join-Path $directory 'opengl32.dll'
        if (Test-Path -LiteralPath $destination)
        {
            if ((Get-FileHash -LiteralPath $destination).Hash -ne $expected)
            {
                throw "Refusing to replace an existing different OpenGL library: $destination"
            }
        }
        else
        {
            [IO.File]::Copy($source, $destination, $false)
            $created.Add($destination)
            if ((Get-FileHash -LiteralPath $destination).Hash -ne $expected)
            {
                throw "Staged OpenGL library hash mismatch: $destination"
            }
        }
        Write-Host "[windows-native-tests] Mesa WGL: $destination sha256=$expected"
    }
    $arguments = @('--test-dir', $build, '-C', $Configuration, '--no-tests=error', '--output-on-failure')
    if ($TestFilter)
    {
        $arguments += @('-R', $TestFilter)
    }
    & $ctest @arguments
    $exitCode = $LASTEXITCODE
}
finally
{
    try
    {
        foreach ($path in $created)
        {
            if ((Get-FileHash -LiteralPath $path).Hash -ne $expected)
            {
                throw "The staged OpenGL library changed during execution; it was not deleted: $path"
            }
            Remove-Item -LiteralPath $path -Force
        }
    }
    finally
    {
        foreach ($name in $names)
        {
            [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
        }
    }
}
exit $exitCode
