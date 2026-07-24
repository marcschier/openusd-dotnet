# Copyright (c) marcschier. Licensed under the MIT License.

function Get-AvaloniaVulkanSmokeSourceIdentity
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    $root = [System.IO.Path]::GetFullPath($RepoRoot)
    $files = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    foreach ($relativeRoot in @(
        'src',
        'tests/OpenUsd.AvaloniaVulkanSmoke'))
    {
        $path = Join-Path $root $relativeRoot
        if (Test-Path $path)
        {
            Get-ChildItem $path -Recurse -File |
                Where-Object {
                    $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and
                    $_.Extension -in @(
                        '.cs', '.csproj', '.props', '.targets', '.json',
                        '.glsl', '.hlsl', '.spv', '.metal')
                } |
                ForEach-Object { $files.Add($_) }
        }
    }
    foreach ($relativePath in @(
        'Directory.Build.props',
        'Directory.Packages.props',
        'global.json',
        'nuget.config',
        'version.json',
        'test-assets/managed-authored.usda',
        'tests/OpenUsd.Rendering.ConformanceTests/VulkanCompositionPresentationTests.cs',
        'eng/avalonia-vulkan-smoke-identity.ps1',
        'eng/publish-avalonia-vulkan-smoke.ps1',
        'eng/run-avalonia-vulkan-smoke.ps1',
        'eng/run-avalonia-vulkan-smoke-linux.sh',
        'eng/test-avalonia-vulkan-smoke-identity.ps1',
        '.github/workflows/render.yml'))
    {
        $path = Join-Path $root $relativePath
        if (Test-Path $path)
        {
            $files.Add((Get-Item $path))
        }
    }

    $entries = foreach ($file in $files |
        Sort-Object { [System.IO.Path]::GetRelativePath($root, $_.FullName) })
    {
        $relative = [System.IO.Path]::GetRelativePath($root, $file.FullName)
        $relative = $relative.Replace('\', '/')
        $hash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash
        "$hash  $relative"
    }
    $manifest = ($entries -join "`n") + "`n"
    $manifestBytes = [System.Text.Encoding]::UTF8.GetBytes($manifest)
    $sourceHash = [System.Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData($manifestBytes))
    $latest = ($files | Measure-Object LastWriteTimeUtc -Maximum).Maximum
    [pscustomobject]@{
        sourceSha256 = $sourceHash
        sourceFileCount = $files.Count
        latestSourceWriteUtc = $latest.ToUniversalTime().ToString('O')
        manifest = $entries
    }
}

function Get-AvaloniaVulkanSmokeExecutableIdentity
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath
    )

    $file = Get-Item ([System.IO.Path]::GetFullPath($ExecutablePath))
    [pscustomobject]@{
        executableSha256 = (Get-FileHash $file.FullName -Algorithm SHA256).Hash
        executableLength = $file.Length
        executableLastWriteUtc = $file.LastWriteTimeUtc.ToString('O')
    }
}

function Assert-AvaloniaVulkanSmokeIdentity
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Expected,
        [Parameter(Mandatory = $true)]
        [object]$Source,
        [Parameter(Mandatory = $true)]
        [object]$Executable
    )

    foreach ($comparison in @(
        @('sourceSha256', $Expected.sourceSha256, $Source.sourceSha256),
        @('sourceFileCount', $Expected.sourceFileCount, $Source.sourceFileCount),
        @('executableSha256', $Expected.executableSha256, $Executable.executableSha256),
        @('executableLength', $Expected.executableLength, $Executable.executableLength)))
    {
        if ([string]$comparison[1] -cne [string]$comparison[2])
        {
            throw "Stale Vulkan smoke evidence: $($comparison[0]) changed " +
                "from '$($comparison[1])' to '$($comparison[2])'."
        }
    }

    $sourceTime = ([DateTimeOffset]$Expected.latestSourceWriteUtc).ToUniversalTime()
    $actualSourceTime = ([DateTimeOffset]$Source.latestSourceWriteUtc).ToUniversalTime()
    $buildTime = ([DateTimeOffset]$Expected.buildCompletedUtc).ToUniversalTime()
    $executableTime = ([DateTimeOffset]$Expected.executableLastWriteUtc).ToUniversalTime()
    $actualExecutableTime = ([DateTimeOffset]$Executable.executableLastWriteUtc).ToUniversalTime()
    if ($sourceTime.Ticks -ne $actualSourceTime.Ticks)
    {
        throw "Stale Vulkan smoke evidence: latestSourceWriteUtc changed " +
            "from '$sourceTime' to '$actualSourceTime'."
    }

    if ($executableTime.Ticks -ne $actualExecutableTime.Ticks)
    {
        throw "Stale Vulkan smoke evidence: executableLastWriteUtc changed " +
            "from '$executableTime' to '$actualExecutableTime'."
    }

    if ($buildTime -lt $sourceTime)
    {
        throw "Stale Vulkan smoke build: source=$sourceTime build=$buildTime."
    }
}
