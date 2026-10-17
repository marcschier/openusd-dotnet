#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if (-not $IsMacOS -or $env:GITHUB_EVENT_NAME -ne 'workflow_dispatch')
{
    throw 'The optional Metal qualification harness requires manual hosted macOS execution.'
}
if ([string]::IsNullOrEmpty($env:OPENUSD_METAL_QUALIFICATION_ZIP))
{
    throw 'The manual Metal qualification payload is missing.'
}
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sdk = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or $sdk -cne '10.0.401')
{
    throw 'Metal qualification requires the exact repository SDK 10.0.401.'
}
$source = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $source -cne $env:GITHUB_SHA)
{
    throw 'Metal qualification requires the actual manual-dispatch checkout SHA.'
}
& (Join-Path $PSScriptRoot 'native-install-metadata.ps1') -Operation Verify -Rid osx-arm64
& python (Join-Path $PSScriptRoot 'shaders/scripts/metal_sidecar.py') `
    --sidecar (Join-Path $PSScriptRoot 'shaders/checked/mesh.metallib.manifest.json') `
    --library (Join-Path $PSScriptRoot 'shaders/checked/mesh.metallib') `
    --manifest (Join-Path $PSScriptRoot 'shaders/shader-manifest.json') `
    --lock (Join-Path $PSScriptRoot 'shaders/toolchain.lock.json') `
    --repository-root $repoRoot --verify-checked-files
if ($LASTEXITCODE -ne 0)
{
    throw 'The Metal qualification shader pair is not verified.'
}
$work = Join-Path $env:RUNNER_TEMP "openusd-metal-qualification-$($env:GITHUB_RUN_ID)"
& python (Join-Path $PSScriptRoot 'decode-metal-qualification.py') --output-root $work
if ($LASTEXITCODE -ne 0)
{
    throw 'The Metal qualification payload failed validation.'
}
$evidence = Join-Path $repoRoot 'artifacts/metal-main-loop-qualification'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $work 'harness-identity.json') -Destination $evidence
$records = [ordered]@{
    sourceSha = $source
    sdk = $sdk
    nativePipelineRunId = $env:NATIVE_PIPELINE_RUN_ID
    nativeInput = Get-Content (Join-Path $repoRoot 'artifacts/native-input/osx-arm64/source.json') -Raw |
        ConvertFrom-Json
    shaderSha256 = (Get-FileHash (Join-Path $PSScriptRoot 'shaders/checked/mesh.metallib')).Hash
    decoderSha256 = (Get-FileHash (Join-Path $PSScriptRoot 'decode-metal-qualification.py')).Hash
    runnerSha256 = (Get-FileHash $PSCommandPath).Hash
    executions = @()
}
$failures = [Collections.Generic.List[Exception]]::new()
try
{
    foreach ($mode in @('managed', 'aot'))
    {
        $execution = @{ mode = $mode }
        $records.executions += $execution
        try
        {
            $publish = Join-Path $work $mode
            $aot = ($mode -eq 'aot').ToString().ToLowerInvariant()
            & dotnet publish (Join-Path $work 'Harness.csproj') -c Release -r osx-arm64 --self-contained true `
                "-p:QualificationRepositoryRoot=$repoRoot" "-p:PublishAot=$aot" `
                -p:OpenUsdRequireMetalShaderLibrary=true -o $publish
            if ($LASTEXITCODE -ne 0)
            {
                throw "The $mode Metal qualification harness failed to publish."
            }
            foreach ($root in @(
                (Join-Path $repoRoot 'native/install/osx-arm64/lib'),
                (Join-Path $repoRoot 'native/install/shim/osx-arm64/lib')))
            {
                Get-ChildItem -LiteralPath $root -File |
                    Where-Object Extension -EQ '.dylib' | Copy-Item -Destination $publish -Force
            }
            $plugins = Join-Path $publish 'plugin/usd'
            New-Item -ItemType Directory -Path $plugins -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $repoRoot 'native/install/osx-arm64/lib/usd') `
                -Destination (Join-Path $publish 'usd') -Recurse -Force
            foreach ($root in @(
                (Join-Path $repoRoot 'native/install/osx-arm64/plugin/usd'),
                (Join-Path $repoRoot 'native/install/shim/osx-arm64/plugin/usd')))
            {
                if (Test-Path -LiteralPath $root -PathType Container)
                {
                    Get-ChildItem -LiteralPath $root | Copy-Item -Destination $plugins -Recurse -Force
                }
            }
            $hdSilkMetadata = Join-Path $plugins 'hdSilk/resources/plugInfo.json'
            & (Join-Path $repoRoot 'src/OpenUsd.Runtime.Packaging/Set-HdSilkPackageLibraryPath.ps1') `
                -SourcePath $hdSilkMetadata -DestinationPath $hdSilkMetadata `
                -LibraryPath '../../../libopenusd_hdsilk.dylib'
            $descriptor = Get-Content -LiteralPath $hdSilkMetadata -Raw | ConvertFrom-Json
            $pluginRoot = [IO.Path]::GetFullPath(
                (Join-Path (Split-Path $hdSilkMetadata) $descriptor.Plugins[0].Root))
            $pluginLibrary = [IO.Path]::GetFullPath(
                (Join-Path $pluginRoot $descriptor.Plugins[0].LibraryPath))
            if ($pluginLibrary -cne (Join-Path $publish 'libopenusd_hdsilk.dylib') -or
                -not (Test-Path -LiteralPath $pluginLibrary -PathType Leaf))
            {
                throw 'The staged hdSilk manifest does not resolve the exact flat published native library.'
            }
            $executable = Join-Path $publish 'OpenUsd.Metal.MainLoopQualification'
            $output = Join-Path $evidence $mode
            New-Item -ItemType Directory -Path $output -Force | Out-Null
            $libraries = @(
                Get-ChildItem -LiteralPath $publish -File |
                    Where-Object { $_.Extension -eq '.dylib' -or $_.Name -eq 'mesh.metallib' } |
                    ForEach-Object { @{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash } })
            $execution.executableSha256 = (Get-FileHash -LiteralPath $executable).Hash
            $execution.libraries = $libraries
            $execution.pluginMetadataSha256 = (Get-FileHash -LiteralPath $hdSilkMetadata).Hash
            $execution.pluginLibraryPath = [IO.Path]::GetRelativePath($publish, $pluginLibrary)
            $execution.pluginLibrarySha256 = (Get-FileHash -LiteralPath $pluginLibrary).Hash
            $start = [Diagnostics.ProcessStartInfo]::new($executable)
            $start.WorkingDirectory = $publish
            $start.UseShellExecute = $false
            $start.RedirectStandardOutput = $true
            $start.RedirectStandardError = $true
            $start.Environment.Clear()
            foreach ($entry in @{ HOME = $env:HOME; PATH = '/usr/bin:/bin:/usr/sbin:/sbin'; LANG = 'en_US.UTF-8' }.GetEnumerator())
            {
                $start.Environment.Add($entry.Key, $entry.Value)
            }
            foreach ($argument in @($plugins, $output, $source, $mode))
            {
                $start.ArgumentList.Add($argument)
            }
            $process = [Diagnostics.Process]::new()
            $process.StartInfo = $start
            try
            {
                if (-not $process.Start())
                {
                    throw "The $mode Metal qualification process did not start."
                }
                $stdout = $process.StandardOutput.ReadToEndAsync()
                $stderr = $process.StandardError.ReadToEndAsync()
                $timedOut = -not $process.WaitForExit(90000)
                if ($timedOut)
                {
                    $process.Kill($true)
                    $process.WaitForExit()
                }
                $stdout.GetAwaiter().GetResult() | Set-Content (Join-Path $output 'stdout.log')
                $stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $output 'stderr.log')
                if ($timedOut)
                {
                    throw "The $mode Metal qualification exceeded its bounded execution window."
                }
                if ($process.ExitCode -ne 0)
                {
                    Get-Content (Join-Path $output 'stderr.log')
                    throw "Normal $mode Metal main-loop qualification failed with exit code $($process.ExitCode)."
                }
                & python (Join-Path $PSScriptRoot 'verify-metal-qualification.py') `
                    --output-root $output --source-sha $source --mode $mode
                if ($LASTEXITCODE -ne 0)
                {
                    throw "The $mode captured Metal pixels or lifecycle evidence failed independent validation."
                }
                $proof = Get-Content (Join-Path $output 'evidence.json') -Raw | ConvertFrom-Json
                if ($proof.sourceSha -cne $source -or $proof.mode -cne $mode -or
                    $proof.backend -cne 'Metal' -or -not $proof.mainThread -or
                    -not $proof.nonuniform -or -not $proof.decodedPixelsDiffer -or
                    $proof.presentedFrames -lt 6 -or $proof.nativeSessions -ne 0 -or
                    $proof.nativePages -ne 0 -or $proof.activeFrames -ne 0 -or $proof.activeGenerations -ne 0)
                {
                    throw "The $mode Metal main-loop evidence is incomplete or degraded."
                }
                $execution.proof = $proof
            }
            finally
            {
                $process.Dispose()
            }
        }
        catch
        {
            $failures.Add($_.Exception)
            $execution.failure = $_.Exception.ToString()
            Write-Host "NORMAL_METAL_QUALIFICATION_FAILED mode=$mode $($_.Exception)"
        }
    }
}
finally
{
    $records | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $evidence 'execution-identity.json')
    Remove-Item -LiteralPath $work -Recurse -Force
}
if ($failures.Count -ne 0)
{
    throw [AggregateException]::new('Normal managed/NativeAOT Metal qualification failed.', $failures)
}
Write-Host 'METAL_MAIN_LOOP_MANAGED_AND_NATIVEAOT_QUALIFICATION_OK'
