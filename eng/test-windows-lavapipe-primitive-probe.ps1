#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if (-not $IsWindows)
{
    throw 'The Windows lavapipe diagnostic fixture requires Windows.'
}

$root = Join-Path ([IO.Path]::GetTempPath()) (
    'openusd-lavapipe-fixture-' + [Guid]::NewGuid().ToString('N'))
$inputRoot = Join-Path $root 'inputs'
$repoRoot = Join-Path $root 'repo'
$eng = Join-Path $repoRoot 'eng'
$runtime = Join-Path $repoRoot 'tests\OpenUsd.Rendering.ConformanceTests\bin\Release\net10.0'
$results = Join-Path $runtime 'TestResults'
$scratchLog = Join-Path $root 'scratch.log'
$registryLog = Join-Path $root 'registry.log'
$probeEnvironmentNames = @(
    'OPENUSD_PRIMITIVE_PROBE_BACKEND',
    'OPENUSD_PRIMITIVE_PROBE_EXPECTED_DEVICE_NAME',
    'OPENUSD_REQUIRE_SWIFTSHADER',
    'MESA_SHADER_CACHE_DISABLE',
    'LP_NUM_THREADS',
    'VK_DRIVER_FILES',
    'VK_ICD_FILENAMES',
    'OPENUSD_VULKAN_LOADER_PATH'
)
$environmentNames = @(
    'OPENUSD_LAVAPIPE_FIXTURE_INPUT',
    'OPENUSD_LAVAPIPE_FIXTURE_SCRATCH_LOG',
    'OPENUSD_LAVAPIPE_FIXTURE_REGISTRY_LOG',
    'OPENUSD_LAVAPIPE_FIXTURE_EVIDENCE',
    'OPENUSD_LAVAPIPE_FIXTURE_MODE'
) + $probeEnvironmentNames
$previousEnvironment = @{}
foreach ($name in $environmentNames)
{
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$previousExitCode = $global:LASTEXITCODE

function curl.exe
{
    $arguments = @($args)
    $index = [Array]::IndexOf($arguments, '--output')
    if ($index -lt 0)
    {
        throw 'The fixture download has no output path.'
    }
    [IO.File]::Copy(
        (Join-Path $env:OPENUSD_LAVAPIPE_FIXTURE_INPUT 'archive.7z'),
        [string]$arguments[$index + 1])
    $global:LASTEXITCODE = 0
}

function tar.exe
{
    $arguments = @($args)
    $index = [Array]::IndexOf($arguments, '-C')
    if ($index -lt 0)
    {
        throw 'The fixture extraction has no destination.'
    }
    $destination = [string]$arguments[$index + 1]
    [IO.File]::AppendAllText($env:OPENUSD_LAVAPIPE_FIXTURE_SCRATCH_LOG, "$destination`n")
    foreach ($name in @('lvp.json', 'vulkan_lvp.dll'))
    {
        [IO.File]::Copy(
            (Join-Path $env:OPENUSD_LAVAPIPE_FIXTURE_INPUT $name),
            (Join-Path $destination $name))
    }
    $global:LASTEXITCODE = 0
}

try
{
    foreach ($directory in @($inputRoot, $eng, $runtime, $results))
    {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'run-windows-lavapipe-primitive-probe.ps1') `
        -Destination $eng
    $archive = Join-Path $inputRoot 'archive.7z'
    $driver = Join-Path $inputRoot 'vulkan_lvp.dll'
    $loader = Join-Path $runtime 'vulkan-1.dll'
    [IO.File]::WriteAllText($archive, 'archive')
    [IO.File]::WriteAllText($driver, 'driver')
    [IO.File]::WriteAllText($loader, 'loader')
    @{
        file_format_version = '1.0.1'
        ICD = @{ api_version = '1.2.3'; library_path = '.\vulkan_lvp.dll' }
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $inputRoot 'lvp.json')
    @{
        package = @{
            archive = 'archive.7z'
            url = 'https://example.invalid/archive'
            archiveSha256 = (Get-FileHash -LiteralPath $archive).Hash
        }
        runtime = @{
            manifest = 'lvp.json'
            driver = 'vulkan_lvp.dll'
            driverSha256 = (Get-FileHash -LiteralPath $driver).Hash
            apiVersion = '1.2.3'
            deviceNameContains = 'llvmpipe'
        }
    } | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $eng 'mesa-lavapipe-test-runtime.lock.json')
    @{
        runtimes = @{ 'win-x64' = @{ loader = 'vulkan-1.dll'; loaderSha256 = (Get-FileHash $loader).Hash } }
    } | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $eng 'vulkan-test-runtime.lock.json')
    $registry = @'
function Register-VulkanTestRuntimeDriver { param($ManifestPath) return [pscustomobject]@{ Name = 'fixture' } }
function Restore-VulkanTestRuntimeDriver {
    param($State)
    [IO.File]::AppendAllText($env:OPENUSD_LAVAPIPE_FIXTURE_REGISTRY_LOG, "restored`n")
    if ($env:OPENUSD_LAVAPIPE_FIXTURE_MODE -in @(
        'registry-failure', 'primary-and-registry-failure', 'both-and-registry-failure')) {
        throw 'synthetic registry restoration failure'
    }
}
'@
    [IO.File]::WriteAllText((Join-Path $eng 'vulkan-test-runtime-registry.ps1'), $registry)
    $runner = @'
param($Project, $Framework, $Configuration, $MinimumExpectedTests, $TestArguments)
$runtime = Join-Path (Split-Path $PSScriptRoot -Parent) 'tests\OpenUsd.Rendering.ConformanceTests\bin\Release\net10.0'
$results = Join-Path $runtime 'TestResults'
$method = 'SingleIndexedDrawPreservesPrimitiveIdsWithoutProvingPtex'
$cases = foreach ($boundary in @(255, 32767, 65535)) {
    @{ methodName = $method; status = 'passed'; displayName = "$method(Vulkan, $boundary)" }
}
$report = @{
    summary = @{ total = 3; passed = 3; failed = 0; skipped = 0; cancelled = 0; timedOut = 0 }
    groups = @(@{ className = 'SilkPrimitiveIdTransportConformanceTests'; tests = @($cases) })
}
$report | ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath (Join-Path $results 'OpenUsd.Rendering.ConformanceTests-windows-net10.0.tunit-report.json')
[IO.File]::WriteAllText(
    (Join-Path $results 'OpenUsd.Rendering.ConformanceTests-windows-net10.0-report.html'), 'fresh-html')
if ($env:OPENUSD_LAVAPIPE_FIXTURE_MODE -in @('both-fail', 'both-and-registry-failure')) {
    New-Item -ItemType Directory -Path (
        Join-Path $env:OPENUSD_LAVAPIPE_FIXTURE_EVIDENCE 'windows-lavapipe-primitive.tunit-report.json') | Out-Null
}
if ($env:OPENUSD_LAVAPIPE_FIXTURE_MODE -in @(
    'primary-failure', 'both-fail', 'primary-and-registry-failure', 'both-and-registry-failure')) {
    throw 'synthetic primary failure'
}
'@
    [IO.File]::WriteAllText((Join-Path $eng 'run-managed-tests.ps1'), $runner)

    $env:OPENUSD_LAVAPIPE_FIXTURE_INPUT = $inputRoot
    $env:OPENUSD_LAVAPIPE_FIXTURE_SCRATCH_LOG = $scratchLog
    $env:OPENUSD_LAVAPIPE_FIXTURE_REGISTRY_LOG = $registryLog
    foreach ($name in $probeEnvironmentNames)
    {
        [Environment]::SetEnvironmentVariable($name, "prior-$name", 'Process')
    }
    $json = Join-Path $results 'OpenUsd.Rendering.ConformanceTests-windows-net10.0.tunit-report.json'
    $html = Join-Path $results 'OpenUsd.Rendering.ConformanceTests-windows-net10.0-report.html'
    foreach ($mode in @(
        'success', 'primary-failure', 'both-fail', 'no-prior', 'existing-evidence',
        'registry-failure', 'primary-and-registry-failure', 'both-and-registry-failure'))
    {
        $evidence = Join-Path $root "evidence-$mode"
        $env:OPENUSD_LAVAPIPE_FIXTURE_MODE = $mode
        $env:OPENUSD_LAVAPIPE_FIXTURE_EVIDENCE = $evidence
        if ($mode -eq 'no-prior')
        {
            Remove-Item -LiteralPath $json, $html -Force
        }
        else
        {
            [IO.File]::WriteAllText($json, "prior-json-$mode")
            [IO.File]::WriteAllText($html, "prior-html-$mode")
        }
        if ($mode -eq 'existing-evidence')
        {
            New-Item -ItemType Directory -Path $evidence | Out-Null
            [IO.File]::WriteAllText(
                (Join-Path $evidence 'windows-lavapipe-primitive.tunit-report.json'), 'existing-evidence')
        }
        $scratchBefore = @(
            if (Test-Path -LiteralPath $scratchLog -PathType Leaf)
            {
                Get-Content -LiteralPath $scratchLog
            }
        )
        $failure = $null
        $previousWarningPreference = $WarningPreference
        try
        {
            if ($mode -eq 'both-and-registry-failure')
            {
                $WarningPreference = 'Stop'
            }
            & (Join-Path $eng 'run-windows-lavapipe-primitive-probe.ps1') -EvidenceRoot $evidence
        }
        catch
        {
            $failure = $_
        }
        finally
        {
            $WarningPreference = $previousWarningPreference
        }
        if ($mode -in @('success', 'no-prior') -and $null -ne $failure)
        {
            throw $failure
        }
        if ($mode -in @('primary-failure', 'both-fail', 'primary-and-registry-failure',
                'both-and-registry-failure') -and
            ($null -eq $failure -or $failure.Exception.ToString() -notlike '*synthetic primary failure*'))
        {
            throw "The primary failure was not preserved: $mode"
        }
        if ($mode -in @('both-fail', 'both-and-registry-failure') -and
            $failure.Exception.ToString() -notlike '*report retention both failed*')
        {
            throw 'The diagnostic and retention failures were not both reported.'
        }
        if ($mode -in @('registry-failure', 'primary-and-registry-failure',
                'both-and-registry-failure') -and
            ($null -eq $failure -or $failure.Exception.ToString() -notlike '*synthetic registry restoration failure*'))
        {
            throw "The registry restoration failure was not reported: $mode"
        }
        if ($mode -in @('primary-and-registry-failure', 'both-and-registry-failure') -and
            $failure.Exception.ToString() -notlike '*diagnostic and cleanup both failed*')
        {
            throw "The diagnostic and cleanup failures were not both reported: $mode"
        }
        if ($mode -in @('primary-and-registry-failure', 'both-and-registry-failure'))
        {
            if ($failure.Exception -isnot [AggregateException] -or
                $failure.Exception.InnerExceptions.Count -ne 2 -or
                $failure.Exception.InnerExceptions[0].ToString() -notlike '*synthetic primary failure*' -or
                $failure.Exception.InnerExceptions[1].ToString() -notlike '*synthetic registry restoration failure*')
            {
                throw "The primary and registry errors were not retained as separate exceptions: $mode"
            }
        }
        if ($mode -eq 'existing-evidence' -and
            ($null -eq $failure -or $failure.Exception.Message -notlike '*evidence already exists*'))
        {
            throw 'Existing evidence was not refused.'
        }
        if ($mode -eq 'no-prior')
        {
            if ((Test-Path -LiteralPath $json) -or (Test-Path -LiteralPath $html))
            {
                throw 'The fixture left new reports in the test output directory.'
            }
        }
        elseif ([IO.File]::ReadAllText($json) -cne "prior-json-$mode" -or
            [IO.File]::ReadAllText($html) -cne "prior-html-$mode")
        {
            throw "The prior reports were not restored: $mode"
        }
        if ($mode -eq 'existing-evidence')
        {
            if ([IO.File]::ReadAllText(
                (Join-Path $evidence 'windows-lavapipe-primitive.tunit-report.json')) -cne 'existing-evidence')
            {
                throw 'The existing diagnostic evidence changed.'
            }
        }
        else
        {
            if ([IO.File]::ReadAllText(
                (Join-Path $evidence 'windows-lavapipe-primitive.report.html')) -cne 'fresh-html')
            {
                throw "The fresh HTML evidence was not retained: $mode"
            }
            if ($mode -notin @('both-fail', 'both-and-registry-failure') -and -not (Test-Path -LiteralPath (
                Join-Path $evidence 'windows-lavapipe-primitive.tunit-report.json') -PathType Leaf))
            {
                throw "The fresh JSON evidence was not retained: $mode"
            }
        }
        foreach ($name in $probeEnvironmentNames)
        {
            if ([Environment]::GetEnvironmentVariable($name, 'Process') -cne "prior-$name")
            {
                throw "The fixture did not restore the probe environment variable: $name"
            }
        }
        $scratchAfter = @(
            if (Test-Path -LiteralPath $scratchLog -PathType Leaf)
            {
                Get-Content -LiteralPath $scratchLog
            }
        )
        $expectedScratchCount = $scratchBefore.Count + [int]($mode -ne 'existing-evidence')
        if ($scratchAfter.Count -ne $expectedScratchCount)
        {
            throw "Unexpected scratch creation for diagnostic mode: $mode"
        }
        if ($mode -ne 'existing-evidence')
        {
            $shouldRetainScratch = $mode -in @(
                'both-fail', 'registry-failure', 'primary-and-registry-failure',
                'both-and-registry-failure')
            if ([bool](Test-Path -LiteralPath $scratchAfter[-1]) -ne $shouldRetainScratch)
            {
                throw "Unexpected scratch retention for diagnostic mode: $mode"
            }
        }
        Write-Host "[lavapipe-fixture] Passed $mode"
    }
    if (@(Get-Content -LiteralPath $registryLog).Count -ne 7)
    {
        throw 'Each executed diagnostic must restore its driver registration.'
    }
}
finally
{
    foreach ($name in $environmentNames)
    {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
    $global:LASTEXITCODE = $previousExitCode
    if (Test-Path -LiteralPath $scratchLog -PathType Leaf)
    {
        $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        foreach ($scratch in Get-Content -LiteralPath $scratchLog)
        {
            $candidate = [IO.Path]::GetFullPath($scratch)
            if ([IO.Path]::GetDirectoryName($candidate) -cne $temporaryRoot.TrimEnd('\') -or
                [IO.Path]::GetFileName($candidate) -cnotmatch '^openusd-lavapipe-primitive-[0-9a-f]{32}$')
            {
                throw "The fixture cannot safely clean its scratch path: $candidate"
            }
            if (Test-Path -LiteralPath $candidate)
            {
                Remove-Item -LiteralPath $candidate -Recurse -Force
            }
        }
    }
    if (Test-Path -LiteralPath $root)
    {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
