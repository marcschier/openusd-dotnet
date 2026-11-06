#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

function Get-FlatOrdinalContract
{
    param([Parameter(Mandatory = $true)][ValidateSet('windows', 'linux', 'macos')][string]$Platform)

    $backends = switch -CaseSensitive ($Platform)
    {
        'windows' { @('D3D12', 'Vulkan') }
        'linux' { @('Vulkan') }
        'macos' { @('Metal') }
    }
    $rows = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($backend in $backends)
    {
        foreach ($first in @(255, 32767, 65534))
        {
            foreach ($varyingClipW in @('False', 'True'))
            {
                [void]$rows.Add("$backend|$first|$varyingClipW")
            }
        }
    }
    return [pscustomobject]@{ Rows = $rows; Count = $rows.Count }
}

function ConvertFrom-FlatOrdinalJson
{
    param([Parameter(Mandatory = $true)][string]$Json)

    if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind'))
    {
        return $Json | ConvertFrom-Json -DateKind String
    }
    return $Json | ConvertFrom-Json
}

function ConvertTo-FlatOrdinalUtc
{
    param([AllowNull()][object]$Value, [string]$Name)

    if ($Value -is [DateTime])
    {
        if ($Value.Kind -eq [DateTimeKind]::Unspecified)
        {
            throw "The flat-ordinal case has no time zone on $Name."
        }
        return $Value.ToUniversalTime()
    }
    if ($Value -is [DateTimeOffset]) { return $Value.UtcDateTime }
    if ($Value -isnot [string] -or $Value -cnotmatch '(Z|[+-]\d{2}:\d{2})$')
    {
        throw "The flat-ordinal case has no valid time zone on $Name."
    }
    try
    {
        return [DateTimeOffset]::Parse(
            $Value, [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::None).UtcDateTime
    }
    catch
    {
        throw "The flat-ordinal case has an invalid $Name."
    }
}

function Assert-FlatOrdinalReport
{
    param(
        [Parameter(Mandatory = $true)][object]$Report,
        [Parameter(Mandatory = $true)][ValidateSet('windows', 'linux', 'macos')][string]$Platform,
        [Parameter(Mandatory = $true)][DateTime]$StartedUtc,
        [Parameter(Mandatory = $true)][DateTime]$FinishedUtc,
        [AllowEmptyString()][string]$PriorGeneration = ''
    )

    $class = 'SilkFlatOrdinalTransportConformanceTests'
    $method = 'SingleIndexedDrawCarriesFlatCompactOrdinalsWithoutProvingPtex'
    $generation = [Guid]::Empty
    if (($Report.schemaVersion -isnot [int] -and $Report.schemaVersion -isnot [long]) -or
        $Report.schemaVersion -ne 1 -or
        $Report.assemblyName -cne 'OpenUsd.Rendering.ConformanceTests' -or
        $Report.publicationGeneration -isnot [string] -or
        -not [Guid]::TryParseExact($Report.publicationGeneration, 'N', [ref]$generation) -or
        $generation -eq [Guid]::Empty -or
        $Report.groups -isnot [array] -or $Report.groups.Count -ne 1 -or
        $Report.groups[0].namespace -cne 'OpenUsd.Rendering.ConformanceTests' -or
        $Report.groups[0].className -cne $class)
    {
        throw 'The report does not identify the selected flat-ordinal assembly, namespace, and class.'
    }
    $priorId = [Guid]::Empty
    if (-not [string]::IsNullOrWhiteSpace($PriorGeneration) -and
        (-not [Guid]::TryParseExact($PriorGeneration, 'N', [ref]$priorId) -or $generation -eq $priorId))
    {
        throw 'The flat-ordinal report reused the prior publication generation.'
    }
    $contract = Get-FlatOrdinalContract -Platform $Platform
    $expectedCounts = [ordered]@{
        total = $contract.Count
        passed = $contract.Count
        failed = 0
        skipped = 0
        cancelled = 0
        timedOut = 0
        flaky = 0
    }
    foreach ($summary in @($Report.summary, $Report.groups[0].summary))
    {
        foreach ($name in $expectedCounts.Keys)
        {
            $value = $summary.$name
            if (($value -isnot [int] -and $value -isnot [long]) -or
                $value -ne $expectedCounts[$name])
            {
                throw "The flat-ordinal gate requires exactly $($contract.Count) passes with no skips or incomplete cases."
            }
        }
    }
    $tests = $Report.groups[0].tests
    if ($tests -isnot [array] -or $tests.Count -ne $contract.Count)
    {
        throw "The flat-ordinal report must contain exactly $($contract.Count) cases."
    }
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $observedRows = [Collections.Generic.List[string]]::new()
    $prefix = "OpenUsd.Rendering.ConformanceTests.$class."
    $rowPattern = '\A' + [regex]::Escape($method) +
        '\((D3D12|Vulkan|Metal),[ \t]*(255|32767|65534),[ \t]*(False|True)\)\z'
    foreach ($test in $tests)
    {
        $id = $test.id
        if ($test.className -cne $class -or $test.methodName -cne $method -or
            $test.status -cne 'passed' -or $id -isnot [string] -or
            [string]::IsNullOrWhiteSpace($id) -or
            -not $id.StartsWith($prefix, [StringComparison]::Ordinal) -or
            -not ($id.Contains(".$method(", [StringComparison]::Ordinal) -or
                $id.Contains(".$method.", [StringComparison]::Ordinal)) -or
            -not $ids.Add($id) -or $test.displayName -isnot [string])
        {
            throw 'An unexpected, duplicate, or non-passing flat-ordinal test case was reported.'
        }
        $match = [regex]::Match($test.displayName, $rowPattern)
        if (-not $match.Success)
        {
            throw "The flat-ordinal case has an unexpected display name: $($test.displayName)"
        }
        $row = "$($match.Groups[1].Value)|$($match.Groups[2].Value)|$($match.Groups[3].Value)"
        if (-not $contract.Rows.Remove($row))
        {
            throw "The flat-ordinal case is unexpected or duplicated: $row"
        }
        $caseStart = ConvertTo-FlatOrdinalUtc -Value $test.startTime -Name 'start time'
        $caseEnd = ConvertTo-FlatOrdinalUtc -Value $test.endTime -Name 'end time'
        if ($caseStart -lt $StartedUtc.ToUniversalTime() -or
            $caseEnd -gt $FinishedUtc.ToUniversalTime() -or $caseEnd -lt $caseStart)
        {
            throw "The flat-ordinal case is outside this execution: $($test.displayName)"
        }
        $observedRows.Add($row)
    }
    if ($contract.Rows.Count -ne 0)
    {
        throw "The flat-ordinal report is missing rows: $($contract.Rows -join ', ')"
    }
    return [pscustomobject]@{
        Count = $tests.Count
        Rows = @($observedRows | Sort-Object -CaseSensitive)
        PublicationGeneration = $Report.publicationGeneration
    }
}

function Assert-FlatOrdinalShaderReceipt
{
    param(
        [Parameter(Mandatory = $true)][object]$Receipt,
        [Parameter(Mandatory = $true)][object[]]$Files
    )

    if (($Receipt.schemaVersion -isnot [int] -and $Receipt.schemaVersion -isnot [long]) -or
        $Receipt.schemaVersion -ne 2)
    {
        throw 'The test-only flat-ordinal shader receipt is stale or invalid.'
    }
    $filesByPath = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($file in $Files)
    {
        if ($file.path -isnot [string] -or [string]::IsNullOrWhiteSpace($file.path) -or
            $filesByPath.ContainsKey($file.path))
        {
            throw 'The flat-ordinal provenance inventory contains an invalid or duplicate path.'
        }
        $filesByPath.Add($file.path, $file)
    }
    $allRecords = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $inputRecords = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $artifactRecords = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($kind in @('inputs', 'artifacts'))
    {
        $records = $Receipt.$kind
        if ($records -isnot [array] -or $records.Count -eq 0)
        {
            throw "The test-only shader receipt has no $kind."
        }
        foreach ($record in $records)
        {
            $path = $record.path
            if ($path -isnot [string] -or [string]::IsNullOrWhiteSpace($path) -or
                $path -cmatch '(^/|^[A-Za-z]:|//|\\|(^|/)\.{1,2}(/|$))' -or
                $record.sha256 -isnot [string] -or $record.sha256 -cnotmatch '^[0-9a-fA-F]{64}$' -or
                ($record.size -isnot [int] -and $record.size -isnot [long]) -or
                $record.size -le 0 -or -not $allRecords.Add($path))
            {
                throw "The test-only shader receipt has an invalid or duplicate $kind record."
            }
            if (-not $filesByPath.ContainsKey($path))
            {
                throw "The test-only shader input or artifact is missing: $path"
            }
            $file = $filesByPath[$path]
            if ($file.size -ne $record.size -or $file.sha256 -ine $record.sha256)
            {
                throw "The test-only shader input or artifact is stale: $path"
            }
            if ($kind -ceq 'inputs') { [void]$inputRecords.Add($path) }
            else { [void]$artifactRecords.Add($path) }
        }
    }
    $shaderPrefix = 'tests/OpenUsd.Rendering.ConformanceTests/FlatOrdinalShaders/'
    $requiredInputs = @(
        'eng/shaders/toolchain.lock.json',
        'eng/shaders/scripts/normalize-reflection.py',
        'eng/shaders/scripts/shader_model.py',
        "${shaderPrefix}build_flat_ordinal_shader.py",
        "${shaderPrefix}flat-ordinal.fragment.slang",
        "${shaderPrefix}flat-ordinal.vertex.slang",
        "${shaderPrefix}flat-ordinal.manifest.json"
    )
    foreach ($path in $requiredInputs)
    {
        if (-not $inputRecords.Contains($path))
        {
            throw "The test-only shader receipt omits a required input: $path"
        }
    }
    $checkedPrefix = "${shaderPrefix}checked/"
    $requiredArtifacts = @(
        "${checkedPrefix}flat-ordinal.fragment.dxil",
        "${checkedPrefix}flat-ordinal.fragment.dxil-reflection.json",
        "${checkedPrefix}flat-ordinal.fragment.metal",
        "${checkedPrefix}flat-ordinal.fragment.reflection.json",
        "${checkedPrefix}flat-ordinal.fragment.spirv-reflection.json",
        "${checkedPrefix}flat-ordinal.fragment.spv",
        "${checkedPrefix}flat-ordinal.vertex.dxil",
        "${checkedPrefix}flat-ordinal.vertex.dxil-reflection.json")
    if ($artifactRecords.Count -ne $requiredArtifacts.Count -or
        @($requiredArtifacts | Where-Object { -not $artifactRecords.Contains($_) }).Count -ne 0)
    {
        throw 'Checked flat-ordinal artifact inventory is incomplete or unexpected.'
    }
    if (-not $filesByPath.ContainsKey("${checkedPrefix}manifest.json"))
    {
        throw 'The test-only shader receipt is missing from the provenance inventory.'
    }
    foreach ($file in $Files)
    {
        $path = $file.path
        if ($path.StartsWith($checkedPrefix, [StringComparison]::Ordinal) -and
            $path -cne "${checkedPrefix}manifest.json" -and -not $artifactRecords.Contains($path))
        {
            throw "The test-only shader receipt omits a generated artifact: $path"
        }
        if ($path.StartsWith($shaderPrefix, [StringComparison]::Ordinal) -and
            ($path.EndsWith('.slang', [StringComparison]::Ordinal) -or
                $path -ceq "${shaderPrefix}flat-ordinal.manifest.json" -or
                $path -ceq "${shaderPrefix}build_flat_ordinal_shader.py") -and
            -not $inputRecords.Contains($path))
        {
            throw "The test-only shader receipt omits a shader source: $path"
        }
    }
}

function Assert-FlatOrdinalInputsUnchanged
{
    param(
        [Parameter(Mandatory = $true)][object[]]$Before,
        [Parameter(Mandatory = $true)][object[]]$After
    )

    if ($Before.Count -ne $After.Count)
    {
        throw 'The flat-ordinal input inventory changed during execution.'
    }
    for ($index = 0; $index -lt $Before.Count; $index++)
    {
        if ($Before[$index].path -cne $After[$index].path -or
            $Before[$index].size -ne $After[$index].size -or
            $Before[$index].sha256 -cne $After[$index].sha256)
        {
            throw "A flat-ordinal input changed during execution: $($Before[$index].path)"
        }
    }
}

function Get-FlatOrdinalInputs
{
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$AssemblyPath
    )

    $shaderDirectory = Join-Path $RepositoryRoot 'tests/OpenUsd.Rendering.ConformanceTests/FlatOrdinalShaders'
    $receiptPath = Join-Path $shaderDirectory 'checked/manifest.json'
    if (-not (Test-Path -LiteralPath $shaderDirectory -PathType Container) -or
        -not (Test-Path -LiteralPath $receiptPath -PathType Leaf))
    {
        throw 'The test-only flat-ordinal shader directory or receipt is missing.'
    }
    $receiptHash = (Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash
    $receipt = ConvertFrom-FlatOrdinalJson -Json (Get-Content -LiteralPath $receiptPath -Raw)
    if ((Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash -cne $receiptHash -or
        $receipt.inputs -isnot [array] -or $receipt.artifacts -isnot [array])
    {
        throw 'The test-only flat-ordinal shader receipt changed or is invalid.'
    }
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $required = @(
        'Directory.Build.props',
        'Directory.Packages.props',
        'global.json',
        'eng/run-compact-ordinal-conformance.ps1',
        'eng/run-managed-tests.ps1',
        'eng/prepare-vulkan-test-runtime.ps1',
        'eng/vulkan-test-runtime-registry.ps1',
        'eng/vulkan-test-runtime.lock.json',
        'tests/OpenUsd.Rendering.ConformanceTests/OpenUsd.Rendering.ConformanceTests.csproj',
        'tests/OpenUsd.Rendering.ConformanceTests/SilkFlatOrdinalTransportConformanceTests.cs',
        'tests/OpenUsd.Rendering.ConformanceTests/FlatOrdinalProbeShader.cs',
        'tests/OpenUsd.Rendering.ConformanceTests/FlatOrdinalShaders/checked/manifest.json'
    )
    foreach ($relative in $required)
    {
        $path = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $relative))
        if (-not (Test-Path -LiteralPath $path -PathType Leaf))
        {
            throw "A required flat-ordinal input is missing: $relative"
        }
        [void]$paths.Add($path)
    }
    foreach ($record in @($receipt.inputs) + @($receipt.artifacts))
    {
        $relative = $record.path
        if ($relative -isnot [string] -or [string]::IsNullOrWhiteSpace($relative) -or
            $relative -cmatch '(^/|^[A-Za-z]:|//|\\|(^|/)\.{1,2}(/|$))')
        {
            throw 'The test-only shader receipt has an invalid input or artifact path.'
        }
        $path = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $relative))
        $resolvedRelative = [IO.Path]::GetRelativePath($RepositoryRoot, $path).Replace('\', '/')
        if ($resolvedRelative -cne $relative -or -not (Test-Path -LiteralPath $path -PathType Leaf))
        {
            throw "The test-only shader input or artifact is missing or outside the repository: $relative"
        }
        [void]$paths.Add($path)
    }
    foreach ($file in Get-ChildItem -LiteralPath $shaderDirectory -Recurse -File -Force)
    {
        $relative = [IO.Path]::GetRelativePath($RepositoryRoot, $file.FullName).Replace('\', '/')
        if ($relative -cmatch '(^|/)__pycache__/|\.pyc$') { continue }
        [void]$paths.Add($file.FullName)
    }
    $assemblyDirectory = [IO.Path]::GetDirectoryName($AssemblyPath)
    foreach ($file in Get-ChildItem -LiteralPath $assemblyDirectory -Filter '*.dll' -File)
    {
        [void]$paths.Add($file.FullName)
    }
    foreach ($path in @(
        $AssemblyPath,
        [IO.Path]::ChangeExtension($AssemblyPath, '.pdb'),
        [IO.Path]::ChangeExtension($AssemblyPath, '.deps.json'),
        [IO.Path]::ChangeExtension($AssemblyPath, '.runtimeconfig.json')))
    {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf))
        {
            throw "A required flat-ordinal assembly input is missing: $path"
        }
        [void]$paths.Add($path)
    }
    $files = [Collections.Generic.List[object]]::new()
    foreach ($path in @($paths | Sort-Object -CaseSensitive))
    {
        $file = Get-Item -LiteralPath $path -ErrorAction Stop
        if ($file.Length -le 0) { throw "A flat-ordinal input is empty: $path" }
        $files.Add([pscustomobject]@{
            path = [IO.Path]::GetRelativePath($RepositoryRoot, $path).Replace('\', '/')
            size = $file.Length
            sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        })
    }
    Assert-FlatOrdinalShaderReceipt -Receipt $receipt -Files $files.ToArray()
    if ((Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash -cne $receiptHash)
    {
        throw 'The test-only flat-ordinal shader receipt changed while its inputs were inspected.'
    }
    return $files.ToArray()
}

function Assert-FlatOrdinalCompiledSources
{
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$AssemblyPath
    )

    $pdbPath = [IO.Path]::ChangeExtension($AssemblyPath, '.pdb')
    if (-not (Test-Path -LiteralPath $pdbPath -PathType Leaf))
    {
        throw 'The compiled conformance assembly has no portable PDB for source provenance.'
    }
    $sources = [ordered]@{
        '/tests/OpenUsd.Rendering.ConformanceTests/SilkFlatOrdinalTransportConformanceTests.cs' =
            'tests/OpenUsd.Rendering.ConformanceTests/SilkFlatOrdinalTransportConformanceTests.cs'
        '/tests/OpenUsd.Rendering.ConformanceTests/FlatOrdinalProbeShader.cs' =
            'tests/OpenUsd.Rendering.ConformanceTests/FlatOrdinalProbeShader.cs'
    }
    $counts = @{}
    foreach ($suffix in $sources.Keys) { $counts[$suffix] = 0 }
    $assemblyStream = $null
    $pe = $null
    $pdbStream = $null
    $provider = $null
    try
    {
        $assemblyStream = [IO.File]::OpenRead($AssemblyPath)
        $pe = [Reflection.PortableExecutable.PEReader]::new($assemblyStream)
        $pdbStream = [IO.File]::OpenRead($pdbPath)
        $provider = [Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($pdbStream)
        $metadata = $provider.GetMetadataReader()
        $id = [byte[]]$metadata.DebugMetadataHeader.Id
        $entries = @($pe.ReadDebugDirectory() | Where-Object {
            $_.Type -eq [Reflection.PortableExecutable.DebugDirectoryEntryType]::CodeView
        })
        if ($id.Length -ne 20 -or $entries.Count -ne 1)
        {
            throw 'The conformance assembly and portable PDB do not identify one matching build.'
        }
        $codeView = $pe.ReadCodeViewDebugDirectoryData($entries[0])
        if ($codeView.Age -ne 1 -or $codeView.Guid -ne [Guid]::new([byte[]]$id[0..15]) -or
            $entries[0].Stamp -ne [BitConverter]::ToUInt32($id, 16))
        {
            throw 'The conformance assembly and portable PDB belong to different builds.'
        }
        foreach ($handle in $metadata.Documents)
        {
            $document = $metadata.GetDocument($handle)
            $name = $metadata.GetString($document.Name).Replace('\', '/')
            foreach ($suffix in $sources.Keys)
            {
                if (-not $name.EndsWith($suffix, [StringComparison]::Ordinal)) { continue }
                $counts[$suffix]++
                $hash = [Convert]::ToHexString($metadata.GetBlobBytes($document.Hash))
                $sourcePath = Join-Path $RepositoryRoot $sources[$suffix]
                if ($metadata.GetGuid($document.HashAlgorithm) -ne
                    [Guid]'8829d00f-11b8-4213-878b-770e8597ac16' -or
                    $hash -cne (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash)
                {
                    throw "The compiled flat-ordinal source is stale: $($sources[$suffix])"
                }
            }
        }
        foreach ($suffix in $sources.Keys)
        {
            if ($counts[$suffix] -ne 1)
            {
                throw "The portable PDB does not identify the flat-ordinal source exactly once: $suffix"
            }
        }
    }
    finally
    {
        if ($null -ne $provider) { $provider.Dispose() }
        if ($null -ne $pdbStream) { $pdbStream.Dispose() }
        if ($null -ne $pe) { $pe.Dispose() }
        if ($null -ne $assemblyStream) { $assemblyStream.Dispose() }
    }
}

$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repo 'tests/OpenUsd.Rendering.ConformanceTests/OpenUsd.Rendering.ConformanceTests.csproj'
$source = Join-Path $repo 'tests/OpenUsd.Rendering.ConformanceTests/SilkFlatOrdinalTransportConformanceTests.cs'
$helper = Join-Path $repo 'tests/OpenUsd.Rendering.ConformanceTests/FlatOrdinalProbeShader.cs'
$platform = if ($IsWindows) { 'windows' } elseif ($IsLinux) { 'linux' } elseif ($IsMacOS) { 'macos' }
else { throw 'Flat-ordinal transport conformance requires Windows, Linux, or macOS.' }
$contract = Get-FlatOrdinalContract -Platform $platform
$head = @(& git -C $repo rev-parse --verify HEAD)
if ($LASTEXITCODE -ne 0 -or $head.Count -ne 1)
{
    throw 'The flat-ordinal source revision is unavailable.'
}
$dotnet = Join-Path $repo $(if ($IsWindows) { '.dotnet/dotnet.exe' } else { '.dotnet/dotnet' })
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf))
{
    $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
}
$target = @(& $dotnet msbuild $project -nologo "-p:Configuration=$Configuration" `
    '-p:TargetFramework=net10.0' '-getProperty:TargetPath')
if ($LASTEXITCODE -ne 0 -or $target.Count -ne 1 -or
    -not [IO.Path]::IsPathFullyQualified($target[0]) -or
    -not (Test-Path -LiteralPath $target[0] -PathType Leaf))
{
    throw 'The compiled flat-ordinal conformance assembly is unavailable.'
}
$inputsBefore = @(Get-FlatOrdinalInputs -RepositoryRoot $repo -AssemblyPath $target[0])
$assemblyTime = (Get-Item -LiteralPath $target[0]).LastWriteTimeUtc
foreach ($compileInput in @($source, $helper, $project,
    (Join-Path $repo 'Directory.Build.props'), (Join-Path $repo 'Directory.Packages.props'),
    (Join-Path $repo 'global.json')))
{
    if (-not (Test-Path -LiteralPath $compileInput -PathType Leaf) -or
        (Get-Item -LiteralPath $compileInput).LastWriteTimeUtc -gt $assemblyTime)
    {
        throw "A compiled flat-ordinal input is missing or newer than the assembly: $compileInput"
    }
}
Assert-FlatOrdinalCompiledSources -RepositoryRoot $repo -AssemblyPath $target[0]
$inputsConfirmed = @(Get-FlatOrdinalInputs -RepositoryRoot $repo -AssemblyPath $target[0])
Assert-FlatOrdinalInputsUnchanged -Before $inputsBefore -After $inputsConfirmed
$hashes = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
foreach ($file in $inputsBefore) { $hashes.Add($file.path, $file.sha256) }
$sourceHash = $hashes['tests/OpenUsd.Rendering.ConformanceTests/SilkFlatOrdinalTransportConformanceTests.cs']
$helperHash = $hashes['tests/OpenUsd.Rendering.ConformanceTests/FlatOrdinalProbeShader.cs']
$assemblyRelative = [IO.Path]::GetRelativePath($repo, $target[0]).Replace('\', '/')
$pdbRelative = [IO.Path]::GetRelativePath(
    $repo, [IO.Path]::ChangeExtension($target[0], '.pdb')).Replace('\', '/')
$assemblyHash = $hashes[$assemblyRelative]
$pdbHash = $hashes[$pdbRelative]
$report = Join-Path ([IO.Path]::GetDirectoryName($target[0])) (
    "TestResults/OpenUsd.Rendering.ConformanceTests-$platform-net10.0.tunit-report.json")
$previousReportHash = $null
$previousGeneration = ''
if (Test-Path -LiteralPath $report -PathType Leaf)
{
    $previousReportHash = (Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash
    try
    {
        $previous = ConvertFrom-FlatOrdinalJson -Json (Get-Content -LiteralPath $report -Raw)
        $priorId = [Guid]::Empty
        if ($previous.publicationGeneration -isnot [string] -or
            -not [Guid]::TryParseExact($previous.publicationGeneration, 'N', [ref]$priorId) -or
            $priorId -eq [Guid]::Empty -or
            (Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash -cne $previousReportHash)
        {
            throw 'The prior report changed or has no valid publication generation.'
        }
        $previousGeneration = $previous.publicationGeneration
    }
    catch
    {
        throw "The prior flat-ordinal report cannot establish freshness: $($_.Exception.Message)"
    }
}
$selector = '/*/*/SilkFlatOrdinalTransportConformanceTests/' +
    'SingleIndexedDrawCarriesFlatCompactOrdinalsWithoutProvingPtex'
$started = [DateTime]::UtcNow
$executionFailure = $null
try
{
    & (Join-Path $PSScriptRoot 'run-managed-tests.ps1') -Project $project -Framework net10.0 `
        -Configuration $Configuration -MinimumExpectedTests $contract.Count `
        -TestArguments @('--treenode-filter', $selector)
    if ($LASTEXITCODE -ne 0) { throw 'The flat-ordinal transport conformance execution failed.' }
}
catch { $executionFailure = $_ }
$finished = [DateTime]::UtcNow
$validationFailure = $null
try
{
    if (-not (Test-Path -LiteralPath $report -PathType Leaf))
    {
        throw 'A fresh flat-ordinal test report is required.'
    }
    $reportTime = (Get-Item -LiteralPath $report).LastWriteTimeUtc
    if ($reportTime -lt $started -or $reportTime -gt $finished)
    {
        throw 'The flat-ordinal test report was not written during this execution.'
    }
    $reportHash = (Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash
    if ($reportHash -ceq $previousReportHash)
    {
        throw 'The flat-ordinal test report was not replaced.'
    }
    $output = Join-Path $repo (
        "artifacts/platform-conformance/$platform-flat-ordinal/$([Guid]::NewGuid().ToString('N'))")
    $retainedReport = Join-Path $output 'flat-ordinal.tunit-report.json'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    Copy-Item -LiteralPath $report -Destination $retainedReport
    if ((Get-FileHash -LiteralPath $retainedReport -Algorithm SHA256).Hash -cne $reportHash)
    {
        throw 'The retained flat-ordinal report differs from the executed report.'
    }
    $inputsAfter = @(Get-FlatOrdinalInputs -RepositoryRoot $repo -AssemblyPath $target[0])
    Assert-FlatOrdinalInputsUnchanged -Before $inputsBefore -After $inputsAfter
    $actual = ConvertFrom-FlatOrdinalJson -Json (Get-Content -LiteralPath $retainedReport -Raw)
    $observed = Assert-FlatOrdinalReport -Report $actual -Platform $platform `
        -StartedUtc $started -FinishedUtc $finished -PriorGeneration $previousGeneration
    $finalHead = @(& git -C $repo rev-parse --verify HEAD)
    if ($LASTEXITCODE -ne 0 -or $finalHead.Count -ne 1 -or $head[0].Trim() -cne $finalHead[0].Trim())
    {
        throw 'The flat-ordinal source revision changed or became unavailable during execution.'
    }
    if ((Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash -cne $reportHash -or
        (Get-FileHash -LiteralPath $retainedReport -Algorithm SHA256).Hash -cne $reportHash)
    {
        throw 'The executed or retained flat-ordinal report changed while it was inspected.'
    }
}
catch { $validationFailure = $_ }
if ($executionFailure -and $validationFailure)
{
    throw [AggregateException]::new('Flat-ordinal execution and evidence validation failed.',
        [Exception[]]@($executionFailure.Exception, $validationFailure.Exception))
}
if ($executionFailure) { throw $executionFailure }
if ($validationFailure) { throw $validationFailure }

[ordered]@{
    source = $head[0].Trim()
    sourceSha256 = $sourceHash
    helperSha256 = $helperHash
    assemblySha256 = $assemblyHash
    pdbSha256 = $pdbHash
    reportSha256 = $reportHash
    retainedReport = 'flat-ordinal.tunit-report.json'
    publicationGeneration = $observed.PublicationGeneration
    startedUtc = $started.ToString('O')
    finishedUtc = $finished.ToString('O')
    platform = $platform
    configuration = $Configuration
    selector = $selector
    scope = 'Sampled flat per-vertex ordinal transport only; not Ptex, atlas filtering, UV transport, or exhaustive correctness.'
    expectedCaseCount = $contract.Count
    observedCaseCount = $observed.Count
    rows = $observed.Rows
    inputs = $inputsBefore
    summary = $actual.summary
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'execution.json')
Write-Host "FLAT_ORDINAL_TRANSPORT_GATE_PASSED platform=$platform cases=$($contract.Count) evidence=$output"
