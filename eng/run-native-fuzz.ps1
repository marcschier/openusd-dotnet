#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [switch]$SelfTest,
    [ValidateRange(1, 3600)]
    [int]$MaxTotalTime = 60,
    [ValidateRange(1, 300)]
    [int]$TimeoutSeconds = 10,
    [ValidateRange(1024, 16777216)]
    [int]$MaxInputBytes = 1048576
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

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

function Resolve-CMake
{
    $command = Get-Command cmake -ErrorAction SilentlyContinue
    if ($command)
    {
        return $command.Source
    }

    if ($IsWindows)
    {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} `
            'Microsoft Visual Studio\Installer\vswhere.exe'
        if (Test-Path $vswhere)
        {
            $match = & $vswhere `
                -latest `
                -products '*' `
                -find 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe' |
                Select-Object -First 1
            if ($match -and (Test-Path $match))
            {
                return $match
            }
        }
    }

    throw 'CMake is required for the native fuzz source contract.'
}

function Invoke-Checked
{
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$Description
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0)
    {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Test-NativeFuzzContract
{
    $nativeCmakePath = Join-Path $repoRoot 'native/CMakeLists.txt'
    $shimCmakePath = Join-Path $repoRoot 'native/openusd_dotnet/CMakeLists.txt'
    $harnessPath = Join-Path $repoRoot `
        'native/openusd_dotnet/tests/fuzz/stage_layer_fuzzer.cpp'
    $seedPath = Join-Path $repoRoot 'test-assets/fuzz-seeds/stage-layer/minimal.usda'
    $workflowPath = Join-Path $repoRoot '.github/workflows/native.yml'
    $nativeCmake = Get-Content $nativeCmakePath -Raw
    $shimCmake = Get-Content $shimCmakePath -Raw
    $harness = Get-Content $harnessPath -Raw
    $workflow = Get-Content $workflowPath -Raw
    $runner = Get-Content $PSCommandPath -Raw

    Assert-Contains `
        $nativeCmake `
        'option(OPENUSD_BUILD_NATIVE_FUZZERS "Build Linux Clang libFuzzer targets" OFF)' `
        'Native CMake'
    Assert-Contains `
        $nativeCmake `
        'OPENUSD_BUILD_NATIVE_FUZZERS requires Linux with Clang and libFuzzer.' `
        'Native CMake'
    foreach ($required in @(
        'tests/fuzz/stage_layer_fuzzer.cpp',
        'add_executable(',
        'openusd_stage_layer_fuzzer',
        '-fsanitize=fuzzer-no-link,address,undefined',
        '-fsanitize=fuzzer,address,undefined'))
    {
        Assert-Contains $shimCmake $required 'OpenUSD C ABI CMake'
    }
    foreach ($required in @(
        'LLVMFuzzerTestOneInput',
        'MaxInputSize = 1024U * 1024U',
        'OPENUSD_FUZZ_TEMP_DIR',
        'OPENUSD_FUZZ_REQUIRE_PARSE',
        'OpenUSD plugin registration failed',
        'RequireSuccessfulParse',
        'openusd_stage_open',
        'openusd_stage_get_root_layer',
        'openusd_layer_get_identifier',
        'openusd_stage_get_prim_paths',
        'openusd_stage_release'))
    {
        Assert-Contains $harness $required 'Stage/layer fuzz harness'
    }
    if ((Get-Content $seedPath -First 1) -cne '#usda 1.0')
    {
        throw 'The stage/layer corpus must contain a minimal USDA seed.'
    }
    foreach ($required in @(
        '- name: Run bounded native fuzzers',
        "if: runner.os == 'Linux'",
        './eng/run-native-fuzz.ps1 -MaxTotalTime 60',
        '- name: Upload native fuzz crash artifacts',
        "if: failure() && runner.os == 'Linux'",
        'artifacts/native-fuzz/linux-x64/'))
    {
        Assert-Contains $workflow $required 'Native workflow'
    }
    foreach ($required in @(
        "'-runs=1'",
        'Native fuzzer known-good seed preflight failed'))
    {
        Assert-Contains $runner $required 'Native fuzz runner'
    }
    foreach ($consumer in @('package.yml', 'render.yml'))
    {
        $consumerWorkflow = Get-Content (
            Join-Path $repoRoot ".github/workflows/$consumer") -Raw
        Assert-DoesNotContain `
            $consumerWorkflow `
            'run-native-fuzz.ps1' `
            "$consumer archive-only workflow"
    }

    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile(
        $PSCommandPath,
        [ref]$tokens,
        [ref]$parseErrors) | Out-Null
    if ($parseErrors.Count -ne 0)
    {
        throw "Native fuzz runner has syntax errors: $($parseErrors -join [Environment]::NewLine)"
    }

    $cmake = Resolve-CMake
    $scratchRoot = Join-Path $repoRoot (
        'artifacts/native-fuzz-self-test-' + [Guid]::NewGuid().ToString('N'))
    try
    {
        $sourceRoot = Join-Path $scratchRoot 'source'
        $buildRoot = Join-Path $scratchRoot 'build'
        New-Item -ItemType Directory -Force -Path $sourceRoot | Out-Null
        $shimSource = (Join-Path $repoRoot 'native/openusd_dotnet').Replace('\', '/')
        $harnessSource = $harnessPath.Replace('\', '/')
        $includeSource = (Join-Path $repoRoot 'native/openusd_dotnet/include').Replace('\', '/')
        @"
cmake_minimum_required(VERSION 3.28)
project(OpenUsdNativeFuzzIsolation LANGUAGES CXX)
set(OPENUSD_BUILD_NATIVE_TESTS OFF CACHE BOOL "" FORCE)
set(OPENUSD_BUILD_NATIVE_FUZZERS OFF CACHE BOOL "" FORCE)
add_library(usd_m INTERFACE)
add_library(OpenColorIO::OpenColorIO INTERFACE IMPORTED)
add_library(openusd_native_sanitizers INTERFACE)
add_subdirectory("$shimSource" openusd_dotnet)
if(TARGET openusd_stage_layer_fuzzer)
    message(FATAL_ERROR "Fuzzer target leaked into an ordinary native configure.")
endif()
add_library(openusd_fuzz_harness_syntax OBJECT "$harnessSource")
target_compile_features(openusd_fuzz_harness_syntax PRIVATE cxx_std_17)
target_include_directories(openusd_fuzz_harness_syntax PRIVATE "$includeSource")
if(MSVC)
    target_compile_options(openusd_fuzz_harness_syntax PRIVATE /W4 /WX /permissive-)
else()
    target_compile_options(openusd_fuzz_harness_syntax PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()
"@ | Set-Content (Join-Path $sourceRoot 'CMakeLists.txt')
        Invoke-Checked `
            -FilePath $cmake `
            -Arguments @('-S', $sourceRoot, '-B', $buildRoot) `
            -Description 'Ordinary native CMake isolation configure'
        Invoke-Checked `
            -FilePath $cmake `
            -Arguments @(
                '--build', $buildRoot,
                '--config', 'Release',
                '--target', 'openusd_fuzz_harness_syntax') `
            -Description 'Native fuzz harness syntax build'

        if ($IsWindows)
        {
            $unsupportedBuild = Join-Path $scratchRoot 'unsupported'
            $previousPreference = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            try
            {
                $output = & $cmake `
                    -S (Join-Path $repoRoot 'native') `
                    -B $unsupportedBuild `
                    -DOPENUSD_BUILD_NATIVE_FUZZERS=ON 2>&1 | Out-String
                $exitCode = $LASTEXITCODE
            }
            finally
            {
                $ErrorActionPreference = $previousPreference
            }
            if ($exitCode -eq 0)
            {
                throw 'A Windows configure unexpectedly accepted native fuzzers.'
            }
            Assert-Contains `
                $output `
                'OPENUSD_BUILD_NATIVE_FUZZERS requires Linux with Clang and libFuzzer.' `
                'Unsupported native fuzzer configure'
            $global:LASTEXITCODE = 0
        }
    }
    finally
    {
        Remove-Item $scratchRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Output 'Native fuzz source and CMake isolation contracts passed.'
}

Test-NativeFuzzContract
if ($SelfTest)
{
    return
}

if (-not $IsLinux)
{
    throw 'Native fuzz execution is supported only on Linux.'
}

$cmake = Resolve-CMake
$ninja = (Get-Command ninja -ErrorAction Stop).Source
$clang = (Get-Command clang -ErrorAction Stop).Source
$clangxx = (Get-Command clang++ -ErrorAction Stop).Source
$openUsdRoot = Join-Path $repoRoot 'native/install/linux-x64'
$seedRoot = Join-Path $repoRoot 'test-assets/fuzz-seeds/stage-layer'
$buildRoot = Join-Path $repoRoot 'native/build/fuzz/linux-x64'
$artifactRoot = Join-Path $repoRoot 'artifacts/native-fuzz/linux-x64'
$workRoot = Join-Path $repoRoot (
    'artifacts/native-fuzz/.work-' + [Guid]::NewGuid().ToString('N'))
if (-not (Test-Path $openUsdRoot -PathType Container))
{
    throw "The existing Linux OpenUSD install was not found at $openUsdRoot."
}
if (-not (Test-Path $seedRoot -PathType Container))
{
    throw "The native fuzz seed corpus was not found at $seedRoot."
}

# The configure resolves Vulkan the same way the ordinary native build does, which
# exports VULKAN_SDK before configuring. This step runs on its own, so the locked
# SDK has to be located here too or find_package(Vulkan REQUIRED) fails.
if (-not $env:VULKAN_SDK)
{
    $lock = Get-Content (Join-Path $PSScriptRoot 'openusd.lock.json') -Raw |
        ConvertFrom-Json
    $localVulkanSdk = Join-Path `
        $repoRoot `
        "native/install/vulkan-sdk-$($lock.vulkanSdk.version)"
    if (-not (Test-Path $localVulkanSdk -PathType Container))
    {
        throw "The locked Vulkan SDK was not found at $localVulkanSdk."
    }
    $env:VULKAN_SDK = $localVulkanSdk
}

Remove-Item $artifactRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
$corpusRoot = Join-Path $workRoot 'corpus'
$preflightRoot = Join-Path $workRoot 'preflight'
$tempRoot = Join-Path $workRoot 'temp'
New-Item -ItemType Directory -Force `
    -Path $corpusRoot, $preflightRoot, $tempRoot | Out-Null
Copy-Item (Join-Path $seedRoot '*') $corpusRoot -Recurse -Force
Copy-Item (Join-Path $seedRoot 'minimal.usda') $preflightRoot -Force

$savedEnvironment = @{
    ASAN_OPTIONS = $env:ASAN_OPTIONS
    UBSAN_OPTIONS = $env:UBSAN_OPTIONS
    LSAN_OPTIONS = $env:LSAN_OPTIONS
    LLVM_SYMBOLIZER_PATH = $env:LLVM_SYMBOLIZER_PATH
    LD_LIBRARY_PATH = $env:LD_LIBRARY_PATH
    OPENUSD_FUZZ_PLUGIN_PATH = $env:OPENUSD_FUZZ_PLUGIN_PATH
    OPENUSD_FUZZ_REQUIRE_PARSE = $env:OPENUSD_FUZZ_REQUIRE_PARSE
    OPENUSD_FUZZ_TEMP_DIR = $env:OPENUSD_FUZZ_TEMP_DIR
    PXR_PLUGINPATH_NAME = $env:PXR_PLUGINPATH_NAME
}

try
{
    Invoke-Checked `
        -FilePath $cmake `
        -Arguments @(
            '-S', (Join-Path $repoRoot 'native'),
            '-B', $buildRoot,
            '-G', 'Ninja',
            "-DCMAKE_MAKE_PROGRAM=$ninja",
            "-DCMAKE_C_COMPILER=$clang",
            "-DCMAKE_CXX_COMPILER=$clangxx",
            '-DCMAKE_BUILD_TYPE=RelWithDebInfo',
            "-DCMAKE_PREFIX_PATH=$openUsdRoot",
            '-DOPENUSD_WITH_VULKAN=ON',
            '-DOPENUSD_BUILD_NATIVE_TESTS=OFF',
            '-DOPENUSD_BUILD_NATIVE_FUZZERS=ON') `
        -Description 'Native fuzzer CMake configure'
    Invoke-Checked `
        -FilePath $cmake `
        -Arguments @(
            '--build', $buildRoot,
            '--target', 'openusd_stage_layer_fuzzer',
            '--parallel') `
        -Description 'Native fuzzer build'

    $fuzzer = Join-Path $buildRoot 'fuzz/openusd_stage_layer_fuzzer'
    if (-not (Test-Path $fuzzer -PathType Leaf))
    {
        throw "The native stage/layer fuzzer was not produced at $fuzzer."
    }

    $libraryPath = Join-Path $openUsdRoot 'lib'
    $env:ASAN_OPTIONS = (
        'abort_on_error=1:check_initialization_order=1:detect_leaks=1:' +
        'detect_stack_use_after_return=1:strict_string_checks=1:symbolize=1')
    $env:UBSAN_OPTIONS = 'halt_on_error=1:print_stacktrace=1'
    # OpenUSD never frees its registry, path-token and trace singletons, so leak
    # detection stays on and those owning modules are suppressed instead.
    $suppressions = Join-Path $PSScriptRoot 'native-fuzz-lsan.supp'
    if (-not (Test-Path $suppressions -PathType Leaf))
    {
        throw "The native fuzz leak suppressions were not found at $suppressions."
    }
    $env:LSAN_OPTIONS = "suppressions=$suppressions" + ':print_suppressions=0'
    $symbolizer = Get-Command llvm-symbolizer -ErrorAction SilentlyContinue
    if ($symbolizer)
    {
        $env:LLVM_SYMBOLIZER_PATH = $symbolizer.Source
    }
    $env:LD_LIBRARY_PATH = if ($savedEnvironment.LD_LIBRARY_PATH)
    {
        $libraryPath + [IO.Path]::PathSeparator + $savedEnvironment.LD_LIBRARY_PATH
    }
    else
    {
        $libraryPath
    }
    $env:OPENUSD_FUZZ_PLUGIN_PATH = Join-Path $libraryPath 'usd'
    $env:OPENUSD_FUZZ_TEMP_DIR = $tempRoot
    $env:PXR_PLUGINPATH_NAME = $env:OPENUSD_FUZZ_PLUGIN_PATH

    $artifactPrefix = $artifactRoot + [IO.Path]::DirectorySeparatorChar
    $env:OPENUSD_FUZZ_REQUIRE_PARSE = '1'
    & $fuzzer `
        $preflightRoot `
        '-seed=1337' `
        '-runs=1' `
        "-max_len=$MaxInputBytes" `
        '-rss_limit_mb=4096' `
        "-artifact_prefix=$artifactPrefix"
    if ($LASTEXITCODE -ne 0)
    {
        throw "Native fuzzer known-good seed preflight failed with exit code $LASTEXITCODE."
    }
    Remove-Item Env:OPENUSD_FUZZ_REQUIRE_PARSE -ErrorAction SilentlyContinue

    & $fuzzer `
        $corpusRoot `
        '-seed=1337' `
        "-max_total_time=$MaxTotalTime" `
        "-timeout=$TimeoutSeconds" `
        "-max_len=$MaxInputBytes" `
        '-rss_limit_mb=4096' `
        '-use_value_profile=1' `
        '-print_final_stats=1' `
        "-artifact_prefix=$artifactPrefix"
    if ($LASTEXITCODE -ne 0)
    {
        throw "Native stage/layer fuzzing failed with exit code $LASTEXITCODE."
    }
}
finally
{
    foreach ($entry in $savedEnvironment.GetEnumerator())
    {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
    Remove-Item $workRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output (
    "Native stage/layer fuzzing passed after a bounded $MaxTotalTime-second run.")
