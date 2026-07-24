# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'viewer-source-identity.ps1')
$workRoot = Join-Path $repoRoot 'artifacts/viewer-source-identity-test'

Remove-Item $workRoot -Recurse -Force -ErrorAction SilentlyContinue
try
{
    $sourceRoot = Join-Path $workRoot 'src/OpenUsd.Viewer'
    $privateRoot = Join-Path $workRoot 'native/private'
    $privateHeader = Join-Path $privateRoot 'openusd_render_camera_internal.h'
    New-Item (Join-Path $sourceRoot 'bin/Debug') -ItemType Directory -Force |
        Out-Null
    New-Item (Join-Path $sourceRoot 'obj/Debug') -ItemType Directory -Force |
        Out-Null
    New-Item $privateRoot -ItemType Directory -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $sourceRoot 'Viewer.cs'), 'source')
    [IO.File]::WriteAllText($privateHeader, 'camera contract')
    [IO.File]::WriteAllText(
        (Join-Path $sourceRoot 'bin/Debug/generated.cs'),
        'binary output')
    [IO.File]::WriteAllText(
        (Join-Path $sourceRoot 'obj/Debug/generated.cs'),
        'intermediate output')
    $contractInputs = @(Get-ViewerEvidenceContractInputPaths)
    foreach ($relative in $contractInputs)
    {
        $path = Join-Path $workRoot $relative
        New-Item (Split-Path $path -Parent) -ItemType Directory -Force |
            Out-Null
        [IO.File]::WriteAllText($path, "contract input: $relative")
    }

    if (-not (Test-ViewerBuildOutputPath -Path '/repo/src/bin/Debug/a.dll') -or
        -not (Test-ViewerBuildOutputPath -Path 'C:\repo\src\obj\Debug\a.dll'))
    {
        throw 'Viewer build-output matching does not support both path separators.'
    }

    $identity = Get-ViewerSourceIdentity -RepoRoot $workRoot
    $paths = @($identity.files.path.Replace('\', '/'))
    $missingInputs = @($contractInputs | Where-Object { $_ -notin $paths })
    if ('src/OpenUsd.Viewer/Viewer.cs' -notin $paths -or
        'native/private/openusd_render_camera_internal.h' -notin $paths -or
        $missingInputs.Count -ne 0 -or
        @($paths | Where-Object { Test-ViewerBuildOutputPath -Path $_ }).Count -ne 0)
    {
        throw "Viewer source identity inputs were incomplete: missing=$($missingInputs -join ', ')"
    }

    [IO.File]::AppendAllText(
        (Join-Path $sourceRoot 'bin/Debug/generated.cs'),
        ' changed')
    [IO.File]::AppendAllText(
        (Join-Path $sourceRoot 'obj/Debug/generated.cs'),
        ' changed')
    $identityAfterBuild = Get-ViewerSourceIdentity -RepoRoot $workRoot
    if ($identityAfterBuild.sha256 -ne $identity.sha256)
    {
        throw 'Viewer source identity changed when only build outputs changed.'
    }

    [IO.File]::AppendAllText($privateHeader, ' mutated')
    $identityAfterPrivateHeader = Get-ViewerSourceIdentity -RepoRoot $workRoot
    if ($identityAfterPrivateHeader.sha256 -eq $identity.sha256)
    {
        throw 'Viewer source identity ignored the shared render camera contract.'
    }
    [IO.File]::WriteAllText($privateHeader, 'camera contract')

    foreach ($relative in $contractInputs)
    {
        $path = Join-Path $workRoot $relative
        $original = [IO.File]::ReadAllText($path)
        [IO.File]::AppendAllText($path, ' mutated')
        $mutated = Get-ViewerSourceIdentity -RepoRoot $workRoot
        if ($mutated.sha256 -eq $identity.sha256)
        {
            throw "Viewer source identity ignored contract mutation: $relative"
        }
        [IO.File]::WriteAllText($path, $original)
    }

    Write-Output (
        'VIEWER_SOURCE_IDENTITY_TEST passed separators=true ' +
        "buildOutputsExcluded=true contractMutations=$($contractInputs.Count)")
}
finally
{
    Remove-Item $workRoot -Recurse -Force -ErrorAction SilentlyContinue
}
