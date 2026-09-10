#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [string]$NativeRuntimeRoot,
    [string]$OutputRoot,
    [ValidateSet('workspace', 'document', 'portable', 'transitions', 'retirement',
        'physics-history', 'property-pages', 'hierarchy-pages', 'asset-relink', 'render-sequence',
        'camera-bookmarks', 'authored-product')]
    [string[]]$Scenario,
    [ValidateSet('D3D12', 'Vulkan')]
    [string]$CaptureRenderer = 'D3D12',
    [ValidateNotNullOrEmpty()]
    [string]$Configuration = 'Release',
    [switch]$SkipBuild,
    [switch]$ListScenarios
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
$scenarios = @(
    @{ Id = 'workspace'; Class = 'ViewerWorkspaceNativeSmokeTests'; Flag = 'OPENUSD_VIEWER_WORKSPACE_SMOKE' },
    @{ Id = 'document'; Class = 'ViewerDocumentNativeSmokeTests'; Flag = 'OPENUSD_VIEWER_DOCUMENT_SMOKE' },
    @{ Id = 'portable'; Class = 'ViewerPortableDocumentNativeTests'; Flag = 'OPENUSD_VIEWER_PORTABLE_SMOKE' },
    @{ Id = 'transitions'; Class = 'ViewerPortableTransitionNativeTests'; Flag = 'OPENUSD_VIEWER_PORTABLE_TRANSITION_SMOKE' },
    @{ Id = 'retirement'; Class = 'ViewerDocumentRetirementNativeTests'; Flag = 'OPENUSD_VIEWER_RETIREMENT_SMOKE' },
    @{ Id = 'physics-history'; Class = 'ViewerPhysicsHistoryNativeTests'; Flag = 'OPENUSD_VIEWER_PHYSICS_HISTORY_SMOKE' },
    @{ Id = 'property-pages'; Class = 'ViewerPropertyPagingNativeTests'; Flag = 'OPENUSD_VIEWER_PROPERTY_PAGING_SMOKE' },
    @{ Id = 'hierarchy-pages'; Class = 'ViewerHierarchyPagingNativeTests'; Flag = 'OPENUSD_VIEWER_HIERARCHY_PAGING_SMOKE' },
    @{ Id = 'asset-relink'; Class = 'ViewerAssetRelinkNativeTests'; Flag = 'OPENUSD_VIEWER_ASSET_RELINK_SMOKE' },
    @{ Id = 'render-sequence'; Class = 'ViewerRenderSequenceNativeTests'; Flag = 'OPENUSD_VIEWER_RENDER_SEQUENCE_SMOKE' },
    @{ Id = 'camera-bookmarks'; Class = 'ViewerCameraBookmarkNativeTests'; Flag = 'OPENUSD_VIEWER_CAMERA_BOOKMARKS_SMOKE' },
    @{ Id = 'authored-product'; Class = 'ViewerAuthoredRenderProductNativeTests'; Flag = 'OPENUSD_VIEWER_AUTHORED_PRODUCT_SMOKE' }
)
if ($ListScenarios)
{
    $scenarios | ForEach-Object { [pscustomobject]$_ } | Select-Object Id, Class, Flag
    return
}
if (-not $IsWindows)
{
    throw 'These native desktop Viewer workflows require Windows.'
}
if ([string]::IsNullOrWhiteSpace($NativeRuntimeRoot) -or
    -not [IO.Path]::IsPathFullyQualified($NativeRuntimeRoot))
{
    throw 'Provide an absolute matched native runtime root containing bin, lib and plugin\usd.'
}
$runtime = [IO.Path]::GetFullPath($NativeRuntimeRoot)
foreach ($required in @('bin\openusd_dotnet.dll', 'bin\openusd_storm_child.dll', 'plugin\usd\plugInfo.json'))
{
    if (-not (Test-Path -LiteralPath (Join-Path $runtime $required) -PathType Leaf))
    {
        throw "The native Viewer runtime is incomplete: $required"
    }
}
if ($Scenario -and @($Scenario | Select-Object -Unique).Count -ne $Scenario.Count)
{
    throw 'Choose each workflow scenario only once.'
}
$selected = @($scenarios | Where-Object { -not $Scenario -or $_.Id -in $Scenario })
if ($selected.Count -eq 0)
{
    throw 'At least one Viewer workflow is required.'
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'
$project = Join-Path $repoRoot 'tests\OpenUsd.Viewer.Tests\OpenUsd.Viewer.Tests.csproj'
$runner = Join-Path $PSScriptRoot 'run-managed-tests.ps1'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf))
{
    $hostCommand = Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1
    $dotnet = $hostCommand.Source
}
$sdk = & $dotnet --version
if ($LASTEXITCODE -ne 0 -or $sdk.Trim() -ne '10.0.301')
{
    throw 'Viewer workflows require the repository-pinned .NET SDK 10.0.301.'
}

function Get-FileRecords
{
    param([string[]]$Paths)

    foreach ($path in $Paths)
    {
        [ordered]@{
            path = $path
            sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
}

function Get-WorkflowSourcePaths
{
    $paths = @(& git -C $repoRoot ls-files --cached --others --exclude-standard -- `
        'src' 'native\openusd_dotnet' 'native\openusd_storm_child' 'native\openusd_hydra' `
        'native\include' 'native\private' 'tests\OpenUsd.Viewer.Tests' `
        'eng\run-viewer-workflow-tests.ps1' 'eng\run-managed-tests.ps1' 'eng\openusd.lock.json' `
        'Directory.Build.props' 'Directory.Build.targets' 'Directory.Packages.props' 'global.json')
    if ($LASTEXITCODE -ne 0 -or $paths.Count -eq 0)
    {
        throw 'Could not identify the source files for Viewer workflow evidence.'
    }
    $paths | Sort-Object -Unique | ForEach-Object { Join-Path $repoRoot $_ }
}

$sourcePaths = @(Get-WorkflowSourcePaths)
$sourcesBefore = @(Get-FileRecords $sourcePaths)
if (-not $SkipBuild)
{
    $buildOutput = @(& $dotnet build $project --no-restore -c $Configuration -warnaserror -v:q 2>&1)
    if ($LASTEXITCODE -ne 0 -and ($buildOutput -join "`n") -match '\bNETSDK1004\b')
    {
        & $dotnet restore $project -v:q
        if ($LASTEXITCODE -ne 0)
        {
            throw 'Restoring the missing Viewer workflow assets failed.'
        }
        $buildOutput = @(& $dotnet build $project --no-restore -c $Configuration -warnaserror -v:q 2>&1)
    }
    $buildOutput | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0)
    {
        throw 'Viewer workflow build failed. Resolve the build or restore missing assets before rerunning.'
    }
}
$target = @(& $dotnet msbuild $project -nologo "-p:Configuration=$Configuration" `
    '-p:TargetFramework=net10.0' '-getProperty:TargetPath')
if ($LASTEXITCODE -ne 0 -or $target.Count -ne 1 -or -not (Test-Path -LiteralPath $target[0] -PathType Leaf))
{
    throw 'The built net10.0 Viewer test assembly could not be resolved.'
}
$assemblyDirectory = Split-Path -Parent $target[0]
function Get-WorkflowBinaryPaths
{
    @(
        Get-ChildItem -LiteralPath $assemblyDirectory -File -Filter '*.dll'
        Get-ChildItem -LiteralPath (Join-Path $runtime 'bin') -File -Filter '*.dll'
        Get-ChildItem -LiteralPath (Join-Path $runtime 'lib') -File -Filter '*.dll'
    ) | Select-Object -ExpandProperty FullName | Sort-Object -Unique
}
$binaryPaths = @(Get-WorkflowBinaryPaths)
$binariesBefore = @(Get-FileRecords $binaryPaths)
$runId = [Guid]::NewGuid().ToString('N')
$evidenceRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot))
{
    Join-Path $repoRoot "artifacts\viewer-workflows\$runId"
}
else
{
    [IO.Path]::GetFullPath($OutputRoot)
}
if (Test-Path -LiteralPath $evidenceRoot)
{
    throw "Use a new evidence directory; existing output will not be overwritten: $evidenceRoot"
}
New-Item -ItemType Directory -Path $evidenceRoot | Out-Null
$workRoot = [IO.Directory]::CreateTempSubdirectory("openusd-viewer-workflows-$runId-").FullName
$environmentNames = @('PATH', 'OPENUSD_VIEWER_TEST_NATIVE_ROOT', 'OPENUSD_PLUGIN_PATH',
    'OPENUSD_TEST_PLUGIN_PATH', 'OPENUSD_TEST_WORK_ROOT',
    'OPENUSD_VIEWER_AUTHORED_PRODUCT_EVIDENCE_ROOT', 'OPENUSD_VIEWER_CAPTURE_RENDERER',
    'OPENUSD_VIEWER_CAPTURE_EVIDENCE_ROOT') +
    @($scenarios | ForEach-Object { $_.Flag })
$previous = @{}
foreach ($name in $environmentNames)
{
    $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$results = [Collections.Generic.List[object]]::new()
try
{
    $env:PATH = "$(Join-Path $runtime 'bin');$(Join-Path $runtime 'lib');$env:PATH"
    $env:OPENUSD_VIEWER_TEST_NATIVE_ROOT = $runtime
    $env:OPENUSD_PLUGIN_PATH = Join-Path $runtime 'plugin\usd'
    $env:OPENUSD_TEST_PLUGIN_PATH = $env:OPENUSD_PLUGIN_PATH
    $env:OPENUSD_TEST_WORK_ROOT = $workRoot
    $env:OPENUSD_VIEWER_AUTHORED_PRODUCT_EVIDENCE_ROOT = $evidenceRoot
    $env:OPENUSD_VIEWER_CAPTURE_RENDERER = $CaptureRenderer
    $env:OPENUSD_VIEWER_CAPTURE_EVIDENCE_ROOT = $evidenceRoot
    foreach ($entry in $scenarios)
    {
        [Environment]::SetEnvironmentVariable($entry.Flag, '0', 'Process')
    }
    foreach ($entry in $selected)
    {
        $logPath = Join-Path $evidenceRoot "$($entry.Id).log"
        [Environment]::SetEnvironmentVariable($entry.Flag, '1', 'Process')
        $watch = [Diagnostics.Stopwatch]::StartNew()
        try
        {
            & $runner -Project $project -Framework net10.0 -Configuration $Configuration `
                -MinimumExpectedTests 1 -TestArguments @('--treenode-filter', "/*/*/$($entry.Class)/*") `
                *>&1 | Tee-Object -FilePath $logPath
            if ($LASTEXITCODE -ne 0)
            {
                throw "Viewer workflow '$($entry.Id)' failed; see $logPath"
            }
            $log = [IO.File]::ReadAllText($logPath)
            $passed = [regex]::Matches($log, '(?m)^\s*succeeded:\s*(\d+)\s*$')
            $failed = [regex]::Matches($log, '(?m)^\s*failed:\s*(\d+)\s*$')
            $skipped = [regex]::Matches($log, '(?m)^\s*skipped:\s*(\d+)\s*$')
            if ($passed.Count -ne 1 -or $failed.Count -ne 1 -or $skipped.Count -ne 1 -or
                [int]$passed[0].Groups[1].Value -lt 1 -or [int]$failed[0].Groups[1].Value -ne 0 -or
                [int]$skipped[0].Groups[1].Value -ne 0)
            {
                throw "Workflow '$($entry.Id)' did not provide one complete no-skip execution summary."
            }
            $results.Add([ordered]@{
                scenario = $entry.Id
                testClass = $entry.Class
                passed = [int]$passed[0].Groups[1].Value
                elapsedMilliseconds = $watch.ElapsedMilliseconds
                logSha256 = (Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash.ToLowerInvariant()
            })
        }
        finally
        {
            [Environment]::SetEnvironmentVariable($entry.Flag, '0', 'Process')
        }
    }
    $sourcesAfter = @(Get-FileRecords @(Get-WorkflowSourcePaths))
    $binariesAfter = @(Get-FileRecords @(Get-WorkflowBinaryPaths))
    if (($sourcesBefore | ConvertTo-Json -Depth 4 -Compress) -cne
        ($sourcesAfter | ConvertTo-Json -Depth 4 -Compress) -or
        ($binariesBefore | ConvertTo-Json -Depth 4 -Compress) -cne
        ($binariesAfter | ConvertTo-Json -Depth 4 -Compress))
    {
        throw 'Source or executed runtime/test binaries changed during the Viewer workflows; rerun on stable inputs.'
    }
    $summary = [ordered]@{
        schemaVersion = 1
        status = 'passed'
        runId = $runId
        completedAt = [DateTimeOffset]::UtcNow.ToString('O')
        platform = 'win-x64'
        captureRenderer = $CaptureRenderer
        captureEvidence = @(Get-FileRecords @(
            @('sequence-composition.json', 'product-composition.json') |
                ForEach-Object { Join-Path $evidenceRoot $_ } |
                Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }))
        runtimeRoot = $runtime
        testAssembly = $target[0]
        allRegisteredScenarios = $selected.Count -eq $scenarios.Count
        scenarios = @($results.ToArray())
        sourceFiles = $sourcesBefore
        binaryFiles = $binariesBefore
    }
    $temporary = Join-Path $evidenceRoot 'summary.json.tmp'
    [IO.File]::WriteAllText($temporary, ($summary | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporary, (Join-Path $evidenceRoot 'summary.json'), $false)
    Write-Output "[viewer-workflows] Passed $($results.Count) scenario(s); evidence=$evidenceRoot"
}
finally
{
    foreach ($name in $environmentNames)
    {
        if ($null -eq $previous[$name])
        {
            if (Test-Path -LiteralPath "Env:\$name")
            {
                Remove-Item -LiteralPath "Env:\$name"
            }
        }
        else
        {
            [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
        }
    }
    if ([IO.Directory]::Exists($workRoot))
    {
        if (@([IO.Directory]::EnumerateFileSystemEntries($workRoot)).Count -eq 0)
        {
            [IO.Directory]::Delete($workRoot)
        }
        else
        {
            Write-Warning "Workflow fixtures remain for diagnosis: $workRoot"
        }
    }
}
