#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Metal', 'Vulkan')][string]$Partition,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repo 'tests\OpenUsd.Rendering.ConformanceTests\OpenUsd.Rendering.ConformanceTests.csproj'
$vulkanMethods = [ordered]@{
    FifthDirectLightContributesOnVulkan = 'StormSilkParityCaptureDriverTests'
    CylinderLightFullLengthDivergesFromSphereLightOnVulkan = 'StormSilkParityCaptureDriverTests'
    CylinderLightZeroLengthMatchesSphereLightOnVulkan = 'StormSilkParityCaptureDriverTests'
    DiskLightFaceOnDivergesFromEdgeOnLightOnVulkan = 'StormSilkParityCaptureDriverTests'
    RectLightFullAreaDivergesFromSphereLightOnVulkan = 'StormSilkParityCaptureDriverTests'
    RectLightZeroAreaMatchesSphereLightOnVulkan = 'StormSilkParityCaptureDriverTests'
    DiskLightEdgeOnMatchesUnlitSceneOnVulkan = 'StormSilkParityCaptureDriverTests'
    ThirtyTwoDeepGridStillIntegratesExactly = 'VolumeDepthSamplingConformanceTests'
    VulkanCompositesStraightAlphaOverDestination = 'VulkanDeviceTests'
}
if (($Partition -eq 'Metal' -and -not $IsMacOS) -or
    ($Partition -eq 'Vulkan' -and -not ($IsWindows -or $IsLinux)))
{
    throw 'Conformance partitions follow the documented Metal/macOS and Vulkan/Windows/Linux platforms.'
}

# Exclude only this explicit backend partition on macOS, not whole classes.
# Future unguarded tests remain selected, so new unsupported routing fails visibly.
$sources = @(& git -C $repo ls-files -- tests/OpenUsd.Rendering.ConformanceTests)
if ($LASTEXITCODE -ne 0) { throw 'Conformance source inventory is unavailable.' }
$sourceText = ($sources | Where-Object { $_.EndsWith('.cs', [StringComparison]::Ordinal) } |
    ForEach-Object { [IO.File]::ReadAllText((Join-Path $repo $_)) }) -join "`n"
foreach ($method in $vulkanMethods.Keys)
{
    $declaration = 'public\s+(?:async\s+)?Task\s+' + [regex]::Escape($method) + '\s*\('
    if ([regex]::Matches($sourceText, $declaration).Count -ne 1)
    {
        throw "The explicit supported-backend method inventory changed: $method"
    }
}
$dotnet = Join-Path $repo $(if ($IsWindows) { '.dotnet\dotnet.exe' } else { '.dotnet/dotnet' })
if (-not (Test-Path -LiteralPath $dotnet))
{
    $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop |
        Select-Object -First 1).Source
}
$target = @(& $dotnet msbuild $project -nologo "-p:Configuration=$Configuration" `
    '-p:TargetFramework=net10.0' '-getProperty:TargetPath')
if ($LASTEXITCODE -ne 0 -or $target.Count -ne 1 -or
    -not (Test-Path -LiteralPath $target[0] -PathType Leaf))
{
    throw 'The actual compiled conformance assembly is unavailable.'
}
$stream = [IO.File]::OpenRead($target[0])
$pe = [Reflection.PortableExecutable.PEReader]::new($stream)
$inventory = [Collections.Generic.List[object]]::new()
$identities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
try
{
    $reader = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    foreach ($handle in $reader.MethodDefinitions)
    {
        $method = $reader.GetMethodDefinition($handle)
        foreach ($attributeHandle in $method.GetCustomAttributes())
        {
            $attribute = $reader.GetCustomAttribute($attributeHandle)
            if ($attribute.Constructor.Kind -ne [Reflection.Metadata.HandleKind]::MemberReference) { continue }
            $member = $reader.GetMemberReference(
                [Reflection.Metadata.MemberReferenceHandle]::op_Explicit($attribute.Constructor))
            if ($member.Parent.Kind -ne [Reflection.Metadata.HandleKind]::TypeReference) { continue }
            $type = $reader.GetTypeReference(
                [Reflection.Metadata.TypeReferenceHandle]::op_Explicit($member.Parent))
            if ($reader.GetString($type.Name) -ceq 'TestAttribute' -and
                $reader.GetString($type.Namespace) -ceq 'TUnit.Core')
            {
                $declaringType = $reader.GetTypeDefinition($method.GetDeclaringType())
                $namespace = $reader.GetString($declaringType.Namespace)
                $class = $reader.GetString($declaringType.Name)
                $name = $reader.GetString($method.Name)
                $identity = "$namespace.$class.$name"
                if (-not $identities.Add($identity))
                { throw "Ambiguous compiled test method identity: $identity" }
                $inventory.Add([pscustomobject]@{
                    namespace = $namespace; class = $class; method = $name; identity = $identity
                })
            }
        }
    }
}
finally { $pe.Dispose(); $stream.Dispose() }
$declaredTestCount = $inventory.Count
foreach ($excluded in $vulkanMethods.Keys)
{
    $definition = @($inventory | Where-Object method -CEQ $excluded)
    if ($definition.Count -ne 1 -or $definition[0].class -cne $vulkanMethods[$excluded])
    { throw "The compiled supported-backend method partition changed: $excluded" }
}
$selectedInventory = @($inventory | Where-Object {
    if ($Partition -ceq 'Metal') { -not $vulkanMethods.Contains($_.method) }
    else { $vulkanMethods.Contains($_.method) }
})
$selected = @($selectedInventory.method | Sort-Object -Unique)
if ($selected.Count -lt 1 -or @($selected | Where-Object { $_ -cnotmatch '^[A-Za-z_][A-Za-z0-9_]*$' }).Count)
{ throw 'The actual test metadata cannot be represented by a complete tree selector.' }
$selector = if ($Partition -ceq 'Metal') { '/*/*/*/(' + ($selected -join '|') + ')' }
else { '/*/*/*/(' + ($vulkanMethods.Keys -join '|') + ')' }
$started = [DateTime]::UtcNow
$executionFailure = $null
try
{
    & (Join-Path $PSScriptRoot 'run-managed-tests.ps1') -Project $project -Framework net10.0 `
        -Configuration $Configuration -MinimumExpectedTests $(if ($Partition -eq 'Vulkan') { 9 } else { 1 }) `
        -TestArguments @('--treenode-filter', $selector)
    if ($LASTEXITCODE -ne 0) { throw 'The platform conformance execution failed.' }
}
catch { $executionFailure = $_ }
$platform = if ($IsMacOS) { 'macos' } elseif ($IsWindows) { 'windows' } else { 'linux' }
$report = Join-Path ([IO.Path]::GetDirectoryName($target[0])) (
    "TestResults/" +
    "OpenUsd.Rendering.ConformanceTests-$platform-net10.0.tunit-report.json")
if (-not (Test-Path -LiteralPath $report -PathType Leaf) -or
    (Get-Item -LiteralPath $report).LastWriteTimeUtc -lt $started)
{
    if ($executionFailure) { throw $executionFailure }
    throw 'A fresh actual platform test report is required.'
}
$output = Join-Path $repo "artifacts/platform-conformance/$platform-$Partition"
$reportHash = (Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash
$retainedReport = Join-Path $output 'full-partition.tunit-report.json'
try
{
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    Copy-Item -LiteralPath $report -Destination $retainedReport -Force
    if ((Get-FileHash -LiteralPath $retainedReport -Algorithm SHA256).Hash -cne $reportHash)
    { throw 'The complete retained platform report differs from the executed report.' }
}
catch
{
    if ($executionFailure)
    {
        throw [AggregateException]::new('Platform execution and report retention failed.',
            $executionFailure.Exception, $_.Exception)
    }
    throw
}
if ($executionFailure) { throw $executionFailure }
$actual = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
$tests = @($actual.groups | ForEach-Object { $_.tests })
if ($actual.summary.failed -ne 0 -or $actual.summary.passed -lt 1)
{
    throw 'The platform test report did not establish successful execution.'
}
$selectedIdentities = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
foreach ($definition in $selectedInventory)
{
    $selectedIdentities.Add($definition.identity, $definition)
}
$observed = [Collections.Generic.Dictionary[string,int]]::new([StringComparer]::Ordinal)
$caseIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($group in $actual.groups)
{
    foreach ($test in $group.tests)
    {
        $identity = "$($group.namespace).$($test.className).$($test.methodName)"
        if ($test.className -cne $group.className -or -not $selectedIdentities.ContainsKey($identity) -or
            [string]::IsNullOrWhiteSpace($test.id) -or -not $caseIds.Add($test.id))
        { throw "Ambiguous or unselected actual test identity: $identity" }
        if ($test.status -cne 'passed' -and $test.status -cne 'skipped')
        { throw "The selected original test did not pass or report its prerequisite skip: $identity" }
        if (-not $observed.ContainsKey($identity)) { $observed.Add($identity, 0) }
        $observed[$identity]++
    }
}
$missing = @($selectedInventory | Where-Object { -not $observed.ContainsKey($_.identity) })
if ($missing.Count -gt 0) { throw "Selected original methods are missing: $($missing.identity -join ', ')" }
if ($actual.summary.total -ne $tests.Count -or
    $actual.summary.passed -ne @($tests | Where-Object status -CEQ 'passed').Count -or
    $actual.summary.skipped -ne @($tests | Where-Object status -CEQ 'skipped').Count)
{ throw 'The complete platform report summary does not account for its actual cases.' }
$evidence = @()
foreach ($method in $vulkanMethods.Keys)
{
    $matching = @($tests | Where-Object methodName -CEQ $method)
    if ($Partition -eq 'Metal')
    {
        if ($matching.Count -ne 0) { throw "The Metal partition unexpectedly ran Vulkan case $method." }
    }
    elseif ($matching.Count -ne 1 -or $matching[0].className -cne $vulkanMethods[$method] -or
        $matching[0].status -cne 'passed')
    {
        throw "The original supported Vulkan case did not execute and pass: $method"
    }
    else
    {
        Write-Host "SUPPORTED_VULKAN_CASE_PASSED $($vulkanMethods[$method]).$method"
        $evidence += @{ class = $matching[0].className; method = $method; status = $matching[0].status }
    }
}
if ($Partition -eq 'Metal')
{
    foreach ($method in @(
        'PresentsIOSurfaceFramesWithTimelineSynchronizationOnMacOS',
        'SubmissionLeasesTextureUntilCompletionOnMacOS'))
    {
        $matching = @($tests | Where-Object methodName -CEQ $method)
        if ($matching.Count -ne 1 -or $matching[0].status -cne 'passed')
        {
            throw "The unchanged original Metal contract did not execute and pass: $method"
        }
    }
}
elseif ($actual.summary.passed -ne 9 -or $actual.summary.skipped -ne 0)
{
    throw 'The supported Vulkan partition must execute exactly nine original methods with no skips.'
}
[ordered]@{
    source = (& git -C $repo rev-parse HEAD).Trim()
    partition = $Partition
    declaredTestMethods = $declaredTestCount
    selectedMethodCount = $selectedInventory.Count
    observedMethodCount = $observed.Count
    observedCaseCount = $tests.Count
    missingMethodCount = $missing.Count
    selectedMethods = $selectedInventory
    observedMethodCaseCounts = $observed
    selector = $selector
    summary = $actual.summary
    assemblySha256 = (Get-FileHash -LiteralPath $target[0] -Algorithm SHA256).Hash
    reportSha256 = $reportHash
    retainedReport = 'full-partition.tunit-report.json'
    originalVulkanMethods = $evidence
} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'execution.json')
