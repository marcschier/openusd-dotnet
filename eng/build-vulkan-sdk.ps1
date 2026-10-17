#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [string]$SourceRoot = (Join-Path $PSScriptRoot '../native/src/vulkan-sdk-1.4.363.0'),
    [string]$BuildRoot = (Join-Path $PSScriptRoot '../native/build/vulkan-sdk-1.4.363.0'),
    [string]$InstallRoot = (Join-Path $PSScriptRoot '../native/install/vulkan-sdk-1.4.363.0'),
    [int]$Jobs = [Environment]::ProcessorCount,
    [switch]$ShaderCompilerOnly,
    [switch]$ReuseExistingDependencies
)

$ErrorActionPreference = 'Stop'
$lock = Get-Content (Join-Path $PSScriptRoot 'openusd.lock.json') -Raw | ConvertFrom-Json
$SourceRoot = [System.IO.Path]::GetFullPath($SourceRoot)
$BuildRoot = [System.IO.Path]::GetFullPath($BuildRoot)
$InstallRoot = [System.IO.Path]::GetFullPath($InstallRoot)
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$compilerPatchLockPath = Join-Path $repositoryRoot $lock.vulkanSdk.sourcePatchLock
if ((Get-FileHash -LiteralPath $compilerPatchLockPath).Hash -cne $lock.vulkanSdk.sourcePatchLockSha256)
{
    throw 'The shader compiler source-repair profile differs from the native lock.'
}
$compilerRepair = Get-Content -LiteralPath $compilerPatchLockPath -Raw | ConvertFrom-Json
$compilerRepairs = @($compilerRepair.components)
if ($compilerRepair.'$schemaVersion' -ne 3 -or $compilerRepairs.Count -eq 0 -or
    @($compilerRepairs.component | Sort-Object -Unique).Count -ne $compilerRepairs.Count)
{
    throw 'Invalid or modified shader compiler source repair.'
}
foreach ($repair in $compilerRepairs)
{
    $files = @($repair.files)
    if ($repair.component -notin @('spirv-tools', 'shaderc') -or
        $repair.sourceCommit -notmatch '^[a-f0-9]{40}$' -or $files.Count -eq 0 -or
        @($files.file | Sort-Object -Unique).Count -ne $files.Count -or
        @($files | Where-Object {
            $_.file -notmatch '^(?:source|libshaderc_util)/(?:[a-z_]+/)*[a-z_]+\.(?:cpp|cc)$' -or
            $_.beforeSha256 -notmatch '^[A-F0-9]{64}$' -or $_.afterSha256 -notmatch '^[A-F0-9]{64}$'
        }).Count -ne 0 -or
        (Get-FileHash -LiteralPath (Join-Path $repositoryRoot $repair.patch)).Hash -cne $repair.patchSha256)
    {
        throw 'Invalid or modified shader compiler source repair.'
    }
}

function Get-CanonicalCompilerSourceHash
{
    param([string]$Path)
    $text = [IO.File]::ReadAllText($Path).Replace("`r`n", "`n")
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text)))
}

if (-not (Get-Command git -ErrorAction SilentlyContinue))
{
    throw 'Git is required.'
}
if (-not (Get-Command python -ErrorAction SilentlyContinue))
{
    throw 'Python is required.'
}
if (-not (Get-Command cmake -ErrorAction SilentlyContinue))
{
    throw 'CMake is required.'
}
if (-not (Get-Command ninja -ErrorAction SilentlyContinue))
{
    throw 'Ninja is required.'
}
if ($IsWindows -and -not (Get-Command cl.exe -ErrorAction SilentlyContinue))
{
    throw 'Run this script from a Visual Studio developer shell so cl.exe is available.'
}

function Sync-Repository
{
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$Commit
    )

    $path = Join-Path $SourceRoot $Name
    if ($ReuseExistingDependencies)
    {
        $actualCommit = & git -C $path rev-parse HEAD
        if ($LASTEXITCODE -ne 0 -or $actualCommit -cne $Commit)
        {
            throw "Reuse-only mode requires the existing locked source: $Name ($Commit)."
        }
        return
    }
    if (-not (Test-Path (Join-Path $path '.git')))
    {
        New-Item -ItemType Directory -Force -Path $SourceRoot | Out-Null
        & git clone --filter=blob:none --no-checkout $Url $path
        if ($LASTEXITCODE -ne 0)
        {
            exit $LASTEXITCODE
        }
    }

    & git -C $path fetch --depth 1 origin $Commit
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
    & git -C $path checkout --detach $Commit
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}

foreach ($repository in $lock.vulkanSdk.repositories)
{
    if ($ShaderCompilerOnly -and $repository.name -ne 'shaderc-source-manifest')
    {
        continue
    }
    $directoryName = if ($repository.name -eq 'shaderc-source-manifest')
    {
        'shaderc'
    }
    else
    {
        $repository.name
    }

    Sync-Repository -Name $directoryName -Url $repository.url -Commit $repository.commit
}

$shadercManifest = Join-Path $SourceRoot 'shaderc'
$shadercSource = Join-Path $shadercManifest 'src'
$knownGood = Get-Content (Join-Path $shadercManifest 'known_good.json') -Raw | ConvertFrom-Json
$manifestRepository = @($lock.vulkanSdk.repositories |
    Where-Object name -EQ 'shaderc-source-manifest')
if ($manifestRepository.Count -ne 1 -or
    $manifestRepository[0].commit -cne $compilerRepair.sourceManifestCommit)
{
    throw 'The compiler source repair does not match the exact pinned source graph.'
}
foreach ($repair in $compilerRepairs)
{
    $component = @($knownGood.commits | Where-Object name -CEQ $repair.component)
    if ($component.Count -ne 1 -or $component[0].commit -cne $repair.sourceCommit)
    {
        throw 'The compiler source repair does not match the exact pinned source graph.'
    }
}
$shadercSourcesCurrent = Test-Path (Join-Path $shadercSource 'CMakeLists.txt')
if ($shadercSourcesCurrent)
{
    foreach ($component in $knownGood.commits)
    {
        $componentRoot = if ($component.PSObject.Properties.Name -contains 'subdir')
            { Join-Path $shadercSource $component.subdir }
            else { $shadercSource }
        $actualCommit = & git -C $componentRoot rev-parse HEAD
        if ($LASTEXITCODE -ne 0 -or $actualCommit -cne $component.commit)
        {
            $shadercSourcesCurrent = $false
            break
        }
    }
}
if (-not $shadercSourcesCurrent)
{
    if ($ReuseExistingDependencies)
    {
        throw 'Reuse-only mode cannot acquire the locked shaderc compiler sources.'
    }
    Push-Location $shadercManifest
    try
    {
        & python (Join-Path $shadercManifest 'update_shaderc_sources.py')
        if ($LASTEXITCODE -ne 0)
        {
            exit $LASTEXITCODE
        }
    }
    finally
    {
        Pop-Location
    }
}
foreach ($component in $knownGood.commits)
{
    $componentRoot = if ($component.PSObject.Properties.Name -contains 'subdir')
        { Join-Path $shadercSource $component.subdir }
        else { $shadercSource }
    $actualCommit = & git -C $componentRoot rev-parse HEAD
    $sourceChanges = @(& git -C $componentRoot status --porcelain --untracked-files=no)
    $componentRepair = @($compilerRepairs | Where-Object component -CEQ $component.name)
    $reviewedRepair = $componentRepair.Count -eq 1
    foreach ($change in $sourceChanges)
    {
        if ($change -notmatch '^ M ((?:source|libshaderc_util)/(?:[a-z_]+/)*[a-z_]+\.(?:cpp|cc))$' -or
            $componentRepair.Count -ne 1)
        {
            $reviewedRepair = $false
            break
        }
        $path = $Matches[1]
        $entry = @($componentRepair[0].files | Where-Object file -CEQ $path)
        if ($entry.Count -ne 1 -or
            (Get-CanonicalCompilerSourceHash (Join-Path $componentRoot $path)) -cne $entry[0].afterSha256)
        {
            $reviewedRepair = $false
            break
        }
    }
    if ($LASTEXITCODE -ne 0 -or $actualCommit -cne $component.commit -or
        ($sourceChanges.Count -ne 0 -and -not $reviewedRepair))
    {
        throw "The shader compiler source does not match its locked manifest: $($component.name)."
    }
}
$repairedSources = @(
    foreach ($repair in $compilerRepairs)
    {
        $component = @($knownGood.commits | Where-Object name -CEQ $repair.component)[0]
        $componentRoot = if ($component.PSObject.Properties.Name -contains 'subdir')
            { Join-Path $shadercSource $component.subdir }
            else { $shadercSource }
        $patchPath = Join-Path $repositoryRoot $repair.patch
        foreach ($entry in $repair.files)
        {
            $path = Join-Path $componentRoot $entry.file
            if ((Get-CanonicalCompilerSourceHash $path) -ceq $entry.beforeSha256)
            {
                $canonical = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
                [IO.File]::WriteAllText($path, $canonical, [Text.UTF8Encoding]::new($false))
                & git -C $componentRoot apply --check "--include=$($entry.file)" $patchPath
                if ($LASTEXITCODE -ne 0) { throw 'The exact shader compiler repair does not apply.' }
                & git -C $componentRoot apply --whitespace=error-all "--include=$($entry.file)" $patchPath
                if ($LASTEXITCODE -ne 0) { throw 'Applying the exact shader compiler source repair failed.' }
            }
            $hash = Get-CanonicalCompilerSourceHash $path
            if ($hash -cne $entry.afterSha256)
            {
                throw "The shader compiler repair did not produce the reviewed source bytes: $($entry.file)"
            }
            [ordered]@{ component = $repair.component; file = $entry.file; sha256 = $hash }
        }
    }
)

New-Item -ItemType Directory -Force -Path $BuildRoot,$InstallRoot | Out-Null

function Invoke-CMake
{
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & cmake @Arguments
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}

function Invoke-CMakeBuild
{
    param([Parameter(Mandatory = $true)][string]$Directory)

    & cmake --build $Directory --target install -j $Jobs
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}

if (-not $ShaderCompilerOnly)
{
    $headersBuild = Join-Path $BuildRoot 'Vulkan-Headers'
    Invoke-CMake @(
        '-S', (Join-Path $SourceRoot 'Vulkan-Headers'),
        '-B', $headersBuild,
        '-G', 'Ninja',
        '-DCMAKE_BUILD_TYPE=Release',
        "-DCMAKE_INSTALL_PREFIX=$InstallRoot",
        '-DVULKAN_HEADERS_ENABLE_TESTS=OFF'
    )
    Invoke-CMakeBuild $headersBuild

    $loaderBuild = Join-Path $BuildRoot 'Vulkan-Loader'
    Invoke-CMake @(
        '-S', (Join-Path $SourceRoot 'Vulkan-Loader'),
        '-B', $loaderBuild,
        '-G', 'Ninja',
        '-DCMAKE_BUILD_TYPE=RelWithDebInfo',
        "-DCMAKE_INSTALL_PREFIX=$InstallRoot",
        "-DVULKAN_HEADERS_INSTALL_DIR=$InstallRoot",
        '-DBUILD_TESTS=OFF',
        '-DUPDATE_DEPS=OFF'
    )
    Invoke-CMakeBuild $loaderBuild
}

$shadercBuild = Join-Path $BuildRoot 'shaderc'
$shadercArguments = @(
    '-S', $shadercSource,
    '-B', $shadercBuild,
    '-G', 'Ninja',
    '-DCMAKE_BUILD_TYPE=Release',
    "-DCMAKE_INSTALL_PREFIX=$InstallRoot",
    '-DSHADERC_SKIP_TESTS=ON',
    '-DSHADERC_ENABLE_SHARED_CRT=ON',
    '-DSHADERC_SKIP_COPYRIGHT_CHECK=ON'
)
if ($ShaderCompilerOnly)
{
    $cmakeVersionOutput = @(& cmake --version)
    if ($LASTEXITCODE -ne 0 -or $cmakeVersionOutput[0] -notmatch '^cmake version (\d+\.\d+\.\d+)')
    {
        throw 'Could not identify CMake diagnostic-option compatibility.'
    }
    $authorCategory = if ([version]$Matches[1] -ge [version]'4.4.0') { 'author' } else { 'dev' }
    if (-not $IsWindows)
    {
        $shadercArguments = @($shadercArguments | Where-Object { $_ -ne '-DSHADERC_ENABLE_SHARED_CRT=ON' })
    }
    $shadercArguments += @(
        '-DSHADERC_SKIP_EXECUTABLES=ON',
        '-DSHADERC_SKIP_EXAMPLES=ON',
        '-DSHADERC_ENABLE_HLSL=OFF',
        '-DCMAKE_POSITION_INDEPENDENT_CODE=ON',
        "-DCMAKE_PROJECT_TOP_LEVEL_INCLUDES=$(Join-Path $PSScriptRoot 'cmake/ShaderCompiler.cmake')",
        "-Werror=$authorCategory",
        '-Werror=deprecated')
    if ($IsMacOS)
    {
        $shadercArguments += @(
            '-DCMAKE_OSX_ARCHITECTURES=arm64',
            "-DCMAKE_OSX_DEPLOYMENT_TARGET=$($lock.profile.platforms.'osx-arm64'.deploymentTarget)")
    }
}
Invoke-CMake $shadercArguments
if ($ShaderCompilerOnly)
{
    & cmake --build $shadercBuild --target shaderc_combined -j $Jobs
    if ($LASTEXITCODE -ne 0)
    {
        throw 'Building the locked CPU shader compiler failed.'
    }
    $libraryName = if ($IsWindows) { 'shaderc_combined.lib' } else { 'libshaderc_combined.a' }
    $library = Join-Path $shadercBuild "libshaderc/$libraryName"
    if (-not (Test-Path -LiteralPath $library -PathType Leaf))
    {
        throw "The locked static shader compiler is missing: $library"
    }
    New-Item -ItemType Directory -Force -Path `
        (Join-Path $InstallRoot 'include'), (Join-Path $InstallRoot 'lib') | Out-Null
    Copy-Item -LiteralPath (Join-Path $shadercSource 'libshaderc/include/shaderc') `
        -Destination (Join-Path $InstallRoot 'include') -Recurse -Force
    $installedLibrary = Join-Path $InstallRoot "lib/$libraryName"
    Copy-Item -LiteralPath $library -Destination $installedLibrary -Force
    $manifestRepository = $lock.vulkanSdk.repositories |
        Where-Object name -EQ 'shaderc-source-manifest'
    [ordered]@{
        schemaVersion = 1
        kind = 'cpu-only-static-shaderc'
        sourceManifestCommit = $manifestRepository.commit
        sourceManifestSha256 = (Get-FileHash (Join-Path $shadercManifest 'known_good.json')).Hash
        buildScriptSha256 = (Get-FileHash $PSCommandPath).Hash
        compilerPolicySha256 = (Get-FileHash (Join-Path $PSScriptRoot 'cmake/ShaderCompiler.cmake')).Hash
        sourcePatchLockSha256 = (Get-FileHash $compilerPatchLockPath).Hash
        repairedSources = $repairedSources
        components = @($knownGood.commits | Select-Object name, commit)
        library = "lib/$libraryName"
        librarySha256 = (Get-FileHash $installedLibrary).Hash
    } | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $InstallRoot 'shaderc-provenance.json') -Encoding utf8NoBOM
    Write-Output $InstallRoot
    return
}
Invoke-CMakeBuild $shadercBuild

if ($IsWindows)
{
    $shadercDebugBuild = Join-Path $BuildRoot 'shaderc-debug'
    Invoke-CMake @(
        '-S', $shadercSource,
        '-B', $shadercDebugBuild,
        '-G', 'Ninja',
        '-DCMAKE_BUILD_TYPE=Debug',
        '-DCMAKE_DEBUG_POSTFIX=d',
        "-DCMAKE_INSTALL_PREFIX=$InstallRoot",
        '-DSHADERC_SKIP_TESTS=ON',
        '-DSHADERC_ENABLE_SHARED_CRT=ON',
        '-DSHADERC_SKIP_COPYRIGHT_CHECK=ON'
    )
    Invoke-CMakeBuild $shadercDebugBuild
}

$includeDirectory = if ($IsWindows) { 'Include' } else { 'include' }
$libraryDirectory = if ($IsWindows) { 'Lib' } else { 'lib' }
$vmaInstallDirectory = Join-Path $InstallRoot "$includeDirectory/vma"
New-Item -ItemType Directory -Force -Path $vmaInstallDirectory | Out-Null
Copy-Item `
    (Join-Path $SourceRoot 'VulkanMemoryAllocator/include/vk_mem_alloc.h') `
    (Join-Path $vmaInstallDirectory 'vk_mem_alloc.h') `
    -Force

$vulkanIncludeDirectory = Join-Path $InstallRoot "$includeDirectory/vulkan"
Copy-Item `
    (Join-Path $SourceRoot 'Vulkan-Utility-Libraries/include/vulkan/vk_enum_string_helper.h') `
    (Join-Path $vulkanIncludeDirectory 'vk_enum_string_helper.h') `
    -Force

$vulkanHeader = Join-Path $InstallRoot "$includeDirectory/vulkan/vulkan.h"
$vmaHeader = Join-Path $vmaInstallDirectory 'vk_mem_alloc.h'
$enumStringHeader = Join-Path $vulkanIncludeDirectory 'vk_enum_string_helper.h'
$vulkanLibrary = Get-ChildItem (Join-Path $InstallRoot $libraryDirectory) -File |
    Where-Object Name -Match 'vulkan'
$shadercLibrary = Get-ChildItem (Join-Path $InstallRoot $libraryDirectory) -File |
    Where-Object Name -Match 'shaderc_combined'
$shadercDebugLibrary = Get-ChildItem (Join-Path $InstallRoot $libraryDirectory) -File |
    Where-Object Name -Match 'shaderc_combinedd'

$hasRequiredShaderc = $shadercLibrary -and
    (-not $IsWindows -or $shadercDebugLibrary)
if (-not (Test-Path $vulkanHeader) -or
    -not (Test-Path $vmaHeader) -or
    -not (Test-Path $enumStringHeader) -or
    -not $vulkanLibrary -or
    -not $hasRequiredShaderc)
{
    throw "The local Vulkan SDK is incomplete at $InstallRoot."
}

Write-Output $InstallRoot
