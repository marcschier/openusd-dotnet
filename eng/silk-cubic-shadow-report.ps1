# Copyright (c) marcschier. Licensed under the MIT License.

function Save-SilkCameraFailureEvidence
{
    param(
        [Parameter(Mandatory = $true)][string]$VolumeRoot,
        [Parameter(Mandatory = $true)][string]$EvidenceRoot,
        [Parameter(Mandatory = $true)][ValidateSet('windows', 'linux', 'macos')][string]$Platform,
        [Parameter(Mandatory = $true)][string]$PrimaryFailure
    )

    $root = Join-Path $EvidenceRoot 'camera-failure-receipts'
    New-Item -ItemType Directory -Path $root -Force -ErrorAction Stop | Out-Null
    $retained = @()
    $errors = [Collections.Generic.List[string]]::new()
    foreach ($backend in @('D3D12', 'Vulkan', 'Metal'))
    {
        $directory = "camera-$Platform-$backend"
        $source = Join-Path (Join-Path $VolumeRoot $directory) 'receipt.json'
        try
        {
            if (-not (Test-Path -LiteralPath $source -PathType Leaf -ErrorAction Stop)) { continue }
            $targetDirectory = Join-Path $root $directory
            New-Item -ItemType Directory -Path $targetDirectory -Force -ErrorAction Stop | Out-Null
            $target = Join-Path $targetDirectory 'receipt.json'
            $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256 -ErrorAction Stop).Hash
            Copy-Item -LiteralPath $source -Destination $target -ErrorAction Stop
            if ((Get-FileHash -LiteralPath $source -Algorithm SHA256 -ErrorAction Stop).Hash -cne $hash -or
                (Get-FileHash -LiteralPath $target -Algorithm SHA256 -ErrorAction Stop).Hash -cne $hash)
            {
                throw "Camera diagnostic receipt changed during retention: $backend"
            }
            $retained += @{ backend = $backend; path = "camera-failure-receipts/$directory/receipt.json"
                sha256 = $hash.ToLowerInvariant() }
        }
        catch
        {
            $errors.Add("${backend}: $($_.Exception.Message)")
        }
    }
    [ordered]@{
        status = 'failed'
        primaryFailure = $PrimaryFailure
        diagnosticOnly = $true
        receipts = @($retained)
        retentionErrors = $errors.ToArray()
    } | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath (Join-Path $EvidenceRoot 'camera.failure.json') -ErrorAction Stop
    foreach ($message in $errors)
    {
        Write-Warning "Camera diagnostic retention failed: $message" -WarningAction Continue
    }
}

function Get-SilkCubicShadowContract
{
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('windows', 'linux', 'macos')]
        [string]$Platform,
        [Parameter(Mandatory = $true)]
        [ValidateSet('SilkCubicCurveNativeTests', 'SilkPointShadowNativeConformanceTests', 'SilkShadowTransmissionConformanceTests', 'SilkFiniteAreaShadowTests', 'VolumeCameraConformanceTests')]
        [string]$Class
    )

    $backends = switch ($Platform)
    {
        'windows' { @('D3D12', 'Vulkan') }
        'linux' { @('Vulkan') }
        'macos' { @('Metal') }
    }
    $cases = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    $skips = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    if ($Class -ceq 'VolumeCameraConformanceTests')
    {
        $method = 'CameraIntegrationBackendGate'
        foreach ($backend in @('D3D12', 'Vulkan', 'Metal'))
        {
            $name = "${method}($backend)"
            $cases.Add($name, $method)
            if ($backend -cnotin $backends) { [void]$skips.Add($name) }
        }
        $expectedCount = 3
    }
    elseif ($Class -ceq 'SilkFiniteAreaShadowTests')
    {
        if ($Platform -ceq 'macos') { throw 'Finite-area shadows have no qualified Metal execution gate.' }
        foreach ($backend in @('D3D12', 'Vulkan'))
        {
            foreach ($method in @(
                'RectEmitterShadowsMatchFiveRayReferenceAcrossUpdateAndRetirement',
                'TransformedRectEmitterShadowsMatchFiveRayReference'))
            {
                $name = "${method}($backend)"
                $cases.Add($name, $method)
                if ($Platform -ceq 'linux' -and $backend -ceq 'D3D12') { [void]$skips.Add($name) }
            }
        }
        $expectedCount = 4
    }
    elseif ($Class -ceq 'SilkShadowTransmissionConformanceTests')
    {
        if ($Platform -ceq 'macos') { throw 'Transmission has no qualified Metal execution gate.' }
        foreach ($backend in @('D3D12', 'Vulkan'))
        {
            foreach ($method in @(
                'TextureBudgetRefusalPreservesTransmissionAndRetriesTheSamePage',
                'OpaqueAndCutoutCastersComposeWithTransmission'))
            {
                $cases.Add("${method}($backend)", $method)
            }
            $method = 'SingleAndMultipleLayersRespectReceiverDepthAndLifecycle'
            foreach ($arguments in @('1, False', '2, False', '4, False', '1, True', '2, True'))
            {
                $cases.Add("${method}($backend, $arguments)", $method)
            }
            foreach ($method in @(
                'OverflowAndEqualDepthRefusalPreservePublishedPixelsAndAllowCorrection',
                'OpacityTextureEditsAndShadowLinksMatchAnalyticPixels'))
            {
                foreach ($argument in @('False', 'True'))
                {
                    $cases.Add("${method}($backend, $argument)", $method)
                }
            }
            $method = 'ConstantAndTexturedLayersComposeInEitherDepthOrder'
            foreach ($streaming in @('False', 'True'))
            {
                foreach ($constantBehindTexture in @('False', 'True'))
                {
                    $cases.Add("${method}($backend, $streaming, $constantBehindTexture)", $method)
                }
            }
        }
        if ($Platform -ceq 'linux')
        {
            foreach ($name in $cases.Keys)
            {
                if ($name.Contains('(D3D12', [StringComparison]::Ordinal)) { [void]$skips.Add($name) }
            }
        }
        $expectedCount = 30
    }
    elseif ($Class -ceq 'SilkCubicCurveNativeTests')
    {
        $method = 'CubicBezierSamplesAndWidthsMatchIndependentBasis'
        foreach ($wrap in @('nonperiodic', 'periodic', 'pinned'))
        {
            foreach ($interpolation in @('constant', 'uniform', 'varying', 'vertex'))
            {
                foreach ($bounded in @('False', 'True'))
                {
                    $cases.Add("${method}($wrap, $interpolation, $bounded)", $method)
                }
            }
        }
        $method = 'PinnedBezierShortControlsMatchRepeatedLastControlReference'
        foreach ($layout in @('two', 'three', 'pair'))
        {
            foreach ($interpolation in @('constant', 'uniform', 'varying', 'vertex'))
            {
                foreach ($bounded in @('False', 'True'))
                {
                    $cases.Add("${method}($layout, $interpolation, $bounded)", $method)
                }
            }
        }
        $method = 'ShortBezierFallbackRemainsScopedToPinnedWrap'
        foreach ($entry in @(
            @{ wrap = 'nonperiodic'; controls = 2 },
            @{ wrap = 'nonperiodic'; controls = 3 },
            @{ wrap = 'periodic'; controls = 2 }))
        {
            foreach ($bounded in @('False', 'True'))
            {
                $cases.Add("${method}($($entry.wrap), $($entry.controls), $bounded)", $method)
            }
        }
        foreach ($method in @(
            'CubicBezierVertexWidthsAcceptUnusedTrailingPoints',
            'PinnedBezierShortControlEditsPreserveBudgetAndIdentity',
            'CubicBezierReservationCountsChordsAndPreservesOldPageOnRefusal',
            'CubicBezierMalformedDeclaredWidthsUseDefaultInsteadOfAnotherInterpolation'))
        {
            $cases.Add($method, $method)
        }
        $method = 'CubicBezierPointTopologyAndUnsupportedEditsRestoreIdentity'
        foreach ($bounded in @('False', 'True'))
        {
            $cases.Add("${method}($bounded)", $method)
        }
        $method = 'CubicBezierInstancesShareOneReservationAndKeepInstanceIdentity'
        foreach ($entry in @(
            @{ controls = 4; wrap = 'nonperiodic' },
            @{ controls = 2; wrap = 'pinned' },
            @{ controls = 3; wrap = 'pinned' }))
        {
            $cases.Add("${method}($($entry.controls), $($entry.wrap))", $method)
        }
        $method = 'CubicBezierUnsupportedInputsRefuseBoundedAndRemoveLegacyGeometry'
        foreach ($entry in @(
            @{ basis = 'bspline'; wrap = 'nonperiodic'; extra = '' },
            @{ basis = 'catmullRom'; wrap = 'nonperiodic'; extra = '' },
            @{ basis = 'bezier'; wrap = 'segmented'; extra = '' },
            @{ basis = 'bezier'; wrap = 'nonperiodic'
                extra = 'normal3f[] normals = [(0,0,1)] (interpolation = "constant")' },
            @{ basis = 'bezier'; wrap = 'nonperiodic'
                extra = 'float[] primvars:displayOpacity = [0.5] (interpolation = "constant")' },
            @{ basis = 'bezier'; wrap = 'nonperiodic'
                extra = 'texCoord2f[] primvars:st = [(0,0)] (interpolation = "constant")' }))
        {
            # TUnit replaces periods in argument display names with U+00B7.
            $extra = $entry.extra.Replace('.', [string][char]0x00B7)
            $cases.Add("${method}($($entry.basis), $($entry.wrap), $extra)", $method)
        }
        $method = 'CubicBezierRasterMatchesIndependentSegments'
        foreach ($backend in $backends)
        {
            foreach ($bounded in @('False', 'True'))
            {
                $cases.Add("${method}($backend, $bounded)", $method)
            }
        }
        $expectedCount = if ($Platform -ceq 'windows') { 73 } else { 71 }
    }
    else
    {
        $method = 'AuthoredBoundedPointShadowMatchesAnalyticPixelsWhenSupported'
        foreach ($backend in $backends)
        {
            $cases.Add("${method}($backend)", $method)
        }
        $expectedCount = if ($Platform -ceq 'windows') { 2 } else { 1 }
    }
    if ($cases.Count -ne $expectedCount)
    {
        throw "The $Platform $Class case manifest has $($cases.Count) entries; expected $expectedCount."
    }
    return [pscustomobject]@{
        Class = $Class; Count = $expectedCount; Cases = $cases
        Skips = $skips; Passed = $expectedCount - $skips.Count
    }
}

function Assert-SilkCubicShadowReport
{
    param(
        [Parameter(Mandatory = $true)][string]$ReportPath,
        [Parameter(Mandatory = $true)]
        [ValidateSet('windows', 'linux', 'macos')]
        [string]$Platform,
        [Parameter(Mandatory = $true)]
        [ValidateSet('SilkCubicCurveNativeTests', 'SilkPointShadowNativeConformanceTests', 'SilkShadowTransmissionConformanceTests', 'SilkFiniteAreaShadowTests', 'VolumeCameraConformanceTests')]
        [string]$Class,
        [Parameter(Mandatory = $true)][DateTime]$StartedUtc,
        [Parameter(Mandatory = $true)][int]$LogPassedCount,
        [string]$ExpectedSha256,
        [string[]]$PriorReportPaths = @()
    )

    $contract = Get-SilkCubicShadowContract -Platform $Platform -Class $Class
    if (-not (Test-Path -LiteralPath $ReportPath -PathType Leaf) -or
        (Get-Item -LiteralPath $ReportPath).LastWriteTimeUtc -lt $StartedUtc.ToUniversalTime())
    {
        throw "A fresh $Class report is required: $ReportPath"
    }
    $hash = (Get-FileHash -LiteralPath $ReportPath -Algorithm SHA256).Hash
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSha256) -and $hash -ine $ExpectedSha256)
    {
        throw "The retained $Class report does not match the execution report."
    }
    $report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
    $groups = @($report.groups)
    if ($report.schemaVersion -ne 1 -or
        $report.assemblyName -cne 'OpenUsd.Rendering.ConformanceTests' -or
        [string]::IsNullOrWhiteSpace($report.publicationGeneration) -or
        $groups.Count -ne 1 -or
        $groups[0].namespace -cne 'OpenUsd.Rendering.ConformanceTests' -or
        $groups[0].className -cne $Class)
    {
        throw "The report does not identify the selected $Class assembly, namespace, and class."
    }
    foreach ($priorPath in $PriorReportPaths)
    {
        if (-not (Test-Path -LiteralPath $priorPath -PathType Leaf))
        {
            throw "Prior report evidence is missing: $priorPath"
        }
        if ((Get-FileHash -LiteralPath $priorPath -Algorithm SHA256).Hash -ieq $hash)
        {
            throw "The $Class report was not replaced."
        }
        try
        {
            $prior = Get-Content -LiteralPath $priorPath -Raw | ConvertFrom-Json
        }
        catch
        {
            throw "Prior $Class report evidence is invalid: $priorPath. $($_.Exception.Message)"
        }
        if ($null -eq $prior -or [string]::IsNullOrWhiteSpace($prior.publicationGeneration))
        {
            throw "Prior $Class report evidence has no publication generation: $priorPath"
        }
        if ($prior.publicationGeneration -ceq $report.publicationGeneration)
        {
            throw "The $Class report reused a prior publication generation."
        }
    }
    foreach ($summary in @($report.summary, $groups[0].summary))
    {
        if ($summary.total -ne $contract.Count -or $summary.passed -ne $contract.Passed -or
            $summary.failed -ne 0 -or $summary.skipped -ne $contract.Skips.Count -or
            $summary.cancelled -ne 0 -or $summary.timedOut -ne 0 -or $summary.flaky -ne 0)
        {
            $requirement = if ($contract.Skips.Count -eq 0) { 'no skips or failures' }
                else { "only the $($contract.Skips.Count) named platform skips and no failures" }
            throw "$Class requires exactly $($contract.Passed) passes with $requirement, including flaky cases."
        }
    }
    $tests = @($groups[0].tests)
    if ($LogPassedCount -ne $contract.Passed -or $tests.Count -ne $contract.Count)
    {
        throw "The execution log and $Class report must contain $($contract.Passed) passes and $($contract.Count) total identities."
    }
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($test in $tests)
    {
        $expectedMethod = $null
        $name = [string]$test.displayName
        $id = [string]$test.id
        $expectedStatus = if ($contract.Skips.Contains($name)) { 'skipped' } else { 'passed' }
        if ($test.className -cne $Class -or $test.status -cne $expectedStatus -or
            [string]::IsNullOrWhiteSpace($id) -or
            -not $id.StartsWith("OpenUsd.Rendering.ConformanceTests.$Class.", [StringComparison]::Ordinal) -or
            -not $ids.Add($id) -or [string]::IsNullOrWhiteSpace($name) -or
            -not $names.Add($name) -or -not $contract.Cases.TryGetValue($name, [ref]$expectedMethod) -or
            $test.methodName -cne $expectedMethod)
        {
            throw "An unexpected, duplicate, mis-cased, or non-passing $Class case was reported: $name"
        }
        if (-not ($id.Contains(".$expectedMethod.", [StringComparison]::Ordinal) -or
                $id.Contains(".$expectedMethod(", [StringComparison]::Ordinal)))
        {
            throw "An unexpected, duplicate, mis-cased, or non-passing $Class case was reported: $name"
        }
        if ($expectedStatus -ceq 'skipped') { continue }
        try
        {
            $caseStart = if ($test.startTime -is [DateTime])
            {
                $test.startTime.ToUniversalTime()
            }
            elseif ($test.startTime -is [DateTimeOffset])
            {
                $test.startTime.UtcDateTime
            }
            else
            {
                [DateTimeOffset]::Parse(
                    [string]$test.startTime, [Globalization.CultureInfo]::InvariantCulture,
                    [Globalization.DateTimeStyles]::AssumeUniversal).UtcDateTime
            }
        }
        catch
        {
            throw "The $Class case has no valid execution start time: $name"
        }
        if ($caseStart -lt $StartedUtc.ToUniversalTime())
        {
            throw "The $Class case predates this execution: $name"
        }
    }
    if ((Get-FileHash -LiteralPath $ReportPath -Algorithm SHA256).Hash -ine $hash)
    {
        throw "The $Class report changed while it was checked."
    }
    return [pscustomobject]@{
        platform = $Platform
        testClass = $Class
        passed = $contract.Passed
        skipped = $contract.Skips.Count
        publicationGeneration = $report.publicationGeneration
        reportSha256 = $hash.ToLowerInvariant()
    }
}
