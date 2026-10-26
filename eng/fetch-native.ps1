#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid,
    [string]$CacheRoot = (Join-Path $PSScriptRoot '../native/downloads'),
    [string]$SourceRoot = (Join-Path $PSScriptRoot '../native/src'),
    [switch]$SourceOnly,
    [string]$PatchLockPath = (Join-Path $PSScriptRoot 'openusd-storage-admission.lock.json'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$lockPath = Join-Path $PSScriptRoot 'openusd.lock.json'
$lock = Get-Content $lockPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($PatchLockPath))
{
    throw 'The standard SDK requires the pinned storage-admission patch profile.'
}
$storagePin = Join-Path $PSScriptRoot 'openusd-storage-admission.lock.json'
if ((Get-FileHash -LiteralPath $storagePin).Hash -cne $lock.openUsd.storageAdmissionPatchLockSha256)
{
    throw 'The storage-admission patch lock does not match the pinned SDK configuration.'
}
$CacheRoot = [System.IO.Path]::GetFullPath($CacheRoot)
$SourceRoot = [System.IO.Path]::GetFullPath($SourceRoot)

New-Item -ItemType Directory -Force -Path $CacheRoot | Out-Null
New-Item -ItemType Directory -Force -Path $SourceRoot | Out-Null

function Get-VerifiedDownload
{
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [Parameter(Mandatory = $true)][string]$FileName,
        [Parameter(Mandatory = $true)][string]$Sha256
    )

    $path = Join-Path $CacheRoot $FileName
    if (Test-Path $path)
    {
        $actual = (Get-FileHash $path -Algorithm SHA256).Hash
        if ($actual -eq $Sha256)
        {
            Write-Host "Verified $FileName"
            return $path
        }

        if (-not $Force)
        {
            throw "Hash mismatch for $path. Expected $Sha256, got $actual. Use -Force to replace it."
        }

        Remove-Item $path -Force
    }

    $partial = "$path.partial"
    Remove-Item $partial -Force -ErrorAction SilentlyContinue
    Write-Host "Downloading $Uri"
    for ($attempt = 1; $attempt -le 3; $attempt++)
    {
        $downloaded = $false
        try
        {
            Invoke-WebRequest -Uri $Uri -OutFile $partial -TimeoutSec 300
            $downloaded = $true
            break
        }
        catch [AggregateException], [System.Net.Http.HttpIOException]
        {
            $failure = $_.Exception
            if ($failure -is [AggregateException])
            {
                $failures = $failure.Flatten().InnerExceptions
                if ($failures.Count -ne 1) { throw }
                $failure = $failures[0]
            }
            if ($failure -isnot [System.Net.Http.HttpIOException] -or
                $failure.HttpRequestError -ne [System.Net.Http.HttpRequestError]::ResponseEnded -or
                $attempt -eq 3)
            {
                throw
            }
            Write-Host "Truncated response for $FileName; retrying pinned download ($attempt/3)."
            Start-Sleep -Seconds 2
        }
        finally
        {
            if (-not $downloaded -and (Test-Path -LiteralPath $partial))
            {
                Remove-Item -LiteralPath $partial -Force
            }
        }
    }
    $downloadHash = (Get-FileHash $partial -Algorithm SHA256).Hash
    if ($downloadHash -ne $Sha256)
    {
        Remove-Item $partial -Force
        throw "Hash mismatch for $Uri. Expected $Sha256, got $downloadHash."
    }

    Move-Item $partial $path
    return $path
}

$openUsdArchive = Get-VerifiedDownload `
    -Uri $lock.openUsd.archiveUrl `
    -FileName $lock.openUsd.archiveName `
    -Sha256 $lock.openUsd.archiveSha256

foreach ($dependency in $lock.dependencies)
{
    if ($SourceOnly) { break }
    if ($dependency.platforms -notcontains $Rid)
    {
        continue
    }

    Get-VerifiedDownload `
        -Uri $dependency.url `
        -FileName $dependency.archiveName `
        -Sha256 $dependency.sha256 | Out-Null
}

$openUsdSource = Join-Path $SourceRoot $lock.openUsd.extractDirectory
if (-not (Test-Path $openUsdSource))
{
    Write-Host "Extracting $($lock.openUsd.archiveName)"
    & tar -xzf $openUsdArchive -C $SourceRoot
    if ($LASTEXITCODE -ne 0)
    {
        throw "Failed to extract $openUsdArchive."
    }
}

$buildScript = Join-Path $openUsdSource $lock.openUsd.buildScript
if (-not (Test-Path $buildScript))
{
    throw "OpenUSD build script was not found at $buildScript."
}

$buildScriptHash = (Get-FileHash $buildScript -Algorithm SHA256).Hash
$knownUnpatchedHashes = @(
    $lock.openUsd.buildScriptSha256,
    'D1ADFB3E014FF92B5234DD0D3106507CDB5DBABF6DD84EFE6ABEFF130E12F609',
    '4175A4790F95BD9E1E5AF3B67DB99F63E37679CCAD37139417DB51ED9874AD8B',
    'FFC713F58159AAF18682438FC686BABB47484A3FFA94D16EC016FE33EE367194',
    '328341514C4218AD12D16E9ACF09BBAD289C92FDAF413BF12AB89911E9F71242',
    'F6FD234EE167F2695BA8F62C159B5E29F5073DE64F09D97D426F9850EC9448E9',
    '58874A95818876A9F1A6145D9C73F5A499C907A6C621542C7D449F34B6819E42'
)
if ($knownUnpatchedHashes -contains $buildScriptHash)
{
    $content = [System.IO.File]::ReadAllText($buildScript)
    $oldBootstrap = '            bootstrap = "bootstrap.bat"'
    $previousBootstrap = '            bootstrap = "cmd.exe /d /s /c bootstrap.bat"'
    $secondBootstrap = '            bootstrap = "cmd.exe /d /s /c .\\bootstrap.bat vc143"'
    $newBootstrap = (
        '            bootstrap = "cmd.exe /d /s /c bootstrap.bat " + ' +
        '("vc145" if IsVisualStudio2026OrGreater() else "vc143")')
    if (-not $content.Contains($newBootstrap))
    {
        if ($content.Contains($oldBootstrap))
        {
            $content = $content.Replace($oldBootstrap, $newBootstrap)
        }
        elseif ($content.Contains($previousBootstrap))
        {
            $content = $content.Replace($previousBootstrap, $newBootstrap)
        }
        elseif ($content.Contains($secondBootstrap))
        {
            $content = $content.Replace($secondBootstrap, $newBootstrap)
        }
        else
        {
            throw 'The expected Windows Boost bootstrap command was not found.'
        }
    }

    $compilerBindingMarker = '            PatchFile("project-config.jam",'
    if (-not $content.Contains($compilerBindingMarker))
    {
        $runBootstrap = '        Run(bootstrapCmd)'
        $runBootstrapPatched = @(
            '        Run(bootstrapCmd)',
            '        if Windows():',
            '            PatchFile("project-config.jam",',
            '                      [("using msvc : 14.3 ;",',
            '                        "using msvc : 14.3 : cl ;"),',
            '                       ("using msvc : 14.5 ;",',
            '                        "using msvc : 14.5 : cl ;")])'
        ) -join "`n"
        if (-not $content.Contains($runBootstrap))
        {
            throw 'The expected Boost bootstrap invocation was not found.'
        }
        $content = $content.Replace($runBootstrap, $runBootstrapPatched)
    }

    $boostDependencyMarker = 'BOOST = Dependency("boost", InstallBoost, *BOOST_VERSION_FILES)'
    $boostDependencyPatched = @(
        'class BoostDependency(Dependency):',
        '    def Exists(self, context):',
        '        if not Dependency.Exists(self, context):',
        '            return False',
        '        if not context.enableOpenVDB:',
        '            return True',
        '        return any(os.path.isfile(path) for pattern in [',
        '            os.path.join(context.instDir, "bin", "*boost_iostreams*"),',
        '            os.path.join(context.instDir, "lib", "*boost_iostreams*")]',
        '            for path in glob.glob(pattern))',
        '',
        'BOOST = BoostDependency("boost", InstallBoost, *BOOST_VERSION_FILES)'
    ) -join "`n"
    if ($content.Contains($boostDependencyMarker))
    {
        $content = $content.Replace($boostDependencyMarker, $boostDependencyPatched)
    }
    elseif (-not $content.Contains('class BoostDependency(Dependency):'))
    {
        throw 'The expected Boost dependency marker was not found.'
    }

    $dracoMonolithicGuard = @(
        '# Error out if user is building monolithic library on windows with draco plugin',
        '# enabled. This currently results in missing symbols.',
        'if context.buildDraco and context.buildMonolithic and Windows():',
        '    PrintError("Draco plugin can not be enabled for monolithic build on Windows")',
        '    sys.exit(1)',
        ''
    ) -join "`n"
    if ($content.Contains($dracoMonolithicGuard))
    {
        $content = $content.Replace(
            $dracoMonolithicGuard,
            '# The locked .NET native profile verifies Draco with the Windows monolithic build.')
    }

    $installUsdMarker = @(
        'def InstallUSD(context, force, buildArgs):',
        '    with CurrentWorkingDirectory(context.usdSrcDir):',
        '        extraArgs = []'
    ) -join "`n"
    $installUsdPatched = @(
        'def InstallUSD(context, force, buildArgs):',
        '    with CurrentWorkingDirectory(context.usdSrcDir):',
        '        PatchFile("cmake/defaults/Options.cmake",',
        '                  [("if (${PXR_BUILD_DRACO_PLUGIN} AND ${PXR_BUILD_MONOLITHIC} AND WIN32)\n    message(FATAL_ERROR \n        \"Draco plugin can not be enabled for monolithic builds on Windows\")\nendif()",',
        '                    "# The locked .NET native profile verifies Draco with the Windows monolithic build.")],',
        '                  multiLineMatches=True)',
        '        extraArgs = []'
    ) -join "`n"
    if (-not $content.Contains('PatchFile("cmake/defaults/Options.cmake"'))
    {
        if (-not $content.Contains($installUsdMarker))
        {
            throw 'The expected USD install function marker was not found.'
        }
        $content = $content.Replace($installUsdMarker, $installUsdPatched)
    }

    $alembicUsdMarker = "            extraArgs.append('-DPXR_BUILD_ALEMBIC_PLUGIN=ON')"
    $alembicUsdPatched = @(
        "            extraArgs.append('-DPXR_BUILD_ALEMBIC_PLUGIN=ON')",
        '            extraArgs.append(''-DALEMBIC_DIR="{}"''.format(context.instDir))'
    ) -join "`n"
    if ($content.Contains($alembicUsdMarker) -and -not $content.Contains("extraArgs.append('-DALEMBIC_DIR="))
    {
        $content = $content.Replace($alembicUsdMarker, $alembicUsdPatched)
    }
    elseif (-not $content.Contains("extraArgs.append('-DALEMBIC_DIR="))
    {
        throw 'The expected Alembic USD configure marker was not found.'
    }

    $openVdbBoostMarker = @(
        '        # Make sure to use boost installed by the build script and not any',
        '        # system installed boost',
        "        extraArgs.append('-DBoost_NO_SYSTEM_PATHS=ON')",
        '',
        '        extraArgs.append(''-DBLOSC_ROOT="{instDir}"'''
    ) -join "`n"
    $openVdbBoostPatched = @(
        '        # Make sure to use boost installed by the build script and not any',
        '        # system installed boost',
        "        extraArgs.append('-DBoost_NO_SYSTEM_PATHS=ON')",
        "        extraArgs.append('-DBoost_NO_BOOST_CMAKE=ON')",
        '',
        '        extraArgs.append(''-DBLOSC_ROOT="{instDir}"'''
    ) -join "`n"
    if ($content.Contains($openVdbBoostMarker) -and
        -not $content.Contains("extraArgs.append('-DBoost_NO_BOOST_CMAKE=ON')"))
    {
        $content = $content.Replace($openVdbBoostMarker, $openVdbBoostPatched)
    }
    elseif (-not $content.Contains("extraArgs.append('-DBoost_NO_BOOST_CMAKE=ON')"))
    {
        throw 'The expected OpenVDB Boost configure marker was not found.'
    }

    $openVdbUsdMarker = @(
        '            if context.enableOpenVDB:',
        "                extraArgs.append('-DPXR_ENABLE_OPENVDB_SUPPORT=ON')",
        '            else:',
        "                extraArgs.append('-DPXR_ENABLE_OPENVDB_SUPPORT=OFF')"
    ) -join "`n"
    $openVdbUsdPatched = @(
        '            if context.enableOpenVDB:',
        "                extraArgs.append('-DPXR_ENABLE_OPENVDB_SUPPORT=ON')",
        '                extraArgs.append(''-DOPENVDB_LOCATION="{instDir}"''',
        '                                 .format(instDir=context.instDir))',
        '            else:',
        "                extraArgs.append('-DPXR_ENABLE_OPENVDB_SUPPORT=OFF')"
    ) -join "`n"
    if ($content.Contains($openVdbUsdMarker) -and
        -not $content.Contains("extraArgs.append('-DOPENVDB_LOCATION="))
    {
        $content = $content.Replace($openVdbUsdMarker, $openVdbUsdPatched)
    }
    elseif (-not $content.Contains("extraArgs.append('-DOPENVDB_LOCATION="))
    {
        throw 'The expected USD OpenVDB configure marker was not found.'
    }
    $alembicPolicyMarker = @(
        'def InstallAlembic(context, force, buildArgs):',
        '    with CurrentWorkingDirectory(DownloadURL(ALEMBIC_URL, context, force)):',
        "        cmakeOptions = ['-DUSE_BINARIES=OFF', '-DUSE_TESTS=OFF', '-DUSE_HDF5=OFF']"
    ) -join "`n"
    $alembicPolicyPatched = @(
        'def InstallAlembic(context, force, buildArgs):',
        '    with CurrentWorkingDirectory(DownloadURL(ALEMBIC_URL, context, force)):',
        '        PatchFile("CMakeLists.txt",',
        '                  [("CMAKE_POLICY(SET CMP0042 OLD)",',
        '                    "CMAKE_POLICY(SET CMP0042 NEW)")])',
        "        cmakeOptions = ['-DUSE_BINARIES=OFF', '-DUSE_TESTS=OFF', '-DUSE_HDF5=OFF']"
    ) -join "`n"
    if (-not $content.Contains('CMAKE_POLICY(SET CMP0042 NEW)'))
    {
        if (-not $content.Contains($alembicPolicyMarker))
        {
            throw 'The expected Alembic install marker was not found.'
        }
        $content = $content.Replace($alembicPolicyMarker, $alembicPolicyPatched)
    }

    $bloscConfigureMarker = @'
def InstallBLOSC(context, force, buildArgs):
    with CurrentWorkingDirectory(DownloadURL(BLOSC_URL, context, force)):
        # MacOS we can use the built in Zlib instead of the external one.
        macArgs = ["-DPREFER_EXTERNAL_ZLIB=ON"]
        if MacOS() and apple_utils.IsTargetArm(context):
            # Need to disable SSE for macOS ARM targets.
            macArgs += ["-DDEACTIVATE_SSE2=ON"]
        RunCMake(context, force, buildArgs + macArgs)
'@
    $bloscConfigurePatched = @'
def InstallBLOSC(context, force, buildArgs):
    with CurrentWorkingDirectory(DownloadURL(BLOSC_URL, context, force)):
        # MacOS we can use the built in Zlib instead of the external one.
        macArgs = ["-DPREFER_EXTERNAL_ZLIB=ON"]
        if MacOS() and apple_utils.IsTargetArm(context):
            # Need to disable SSE for macOS ARM targets.
            macArgs += ["-DDEACTIVATE_SSE2=ON"]
        cmakeArgs = buildArgs + macArgs
        if Linux():
            zstdIncludeDir = os.path.join(context.instDir, "include")
            zstdLibrary = os.path.join(context.instDir, "lib", "libzstd.so")
            if (not os.path.isfile(os.path.join(zstdIncludeDir, "zstd.h"))
                    or not os.path.isfile(zstdLibrary)):
                raise RuntimeError(
                    "The exact pinned SDK Zstd include/library pair is required by Linux Blosc.")
            cmakeArgs += [
                "-DPREFER_EXTERNAL_ZSTD=ON",
                "-DZSTD_INCLUDE_DIR=" + zstdIncludeDir,
                "-DZSTD_LIBRARY=" + zstdLibrary]
        RunCMake(context, force, cmakeArgs)
        if Linux():
            import subprocess
            import re
            buildDir = os.path.join(context.buildDir, os.path.basename(os.getcwd()))
            cachePath = os.path.join(buildDir, "CMakeCache.txt")
            cacheValues = {}
            with open(cachePath, "r", encoding="utf-8") as cache:
                for line in cache:
                    if not line.startswith("//") and ":" in line and "=" in line:
                        key, value = line.split("=", 1)
                        cacheValues[key.split(":", 1)[0]] = value.strip()
            expectedCache = {
                "PREFER_EXTERNAL_ZSTD": "ON",
                "ZSTD_INCLUDE_DIR": zstdIncludeDir,
                "ZSTD_LIBRARY": zstdLibrary}
            for key, expected in expectedCache.items():
                actual = cacheValues.get(key)
                if actual is None:
                    raise RuntimeError(
                        "Linux Blosc did not cache required external Zstd setting {}.".format(key))
                if key == "PREFER_EXTERNAL_ZSTD":
                    matches = actual.upper() in ("ON", "TRUE", "1")
                else:
                    matches = os.path.realpath(actual) == os.path.realpath(expected)
                if not matches:
                    raise RuntimeError(
                        "Linux Blosc resolved {} to {!r}, expected the locked SDK value {!r}."
                        .format(key, actual, expected))

            bloscLibrary = os.path.join(context.instDir, "lib", "libblosc.so")
            if not os.path.isfile(bloscLibrary):
                raise RuntimeError("The Linux Blosc shared library was not installed.")
            dynamic = subprocess.check_output(
                ["readelf", "-d", bloscLibrary], text=True, stderr=subprocess.STDOUT)
            if not re.search(
                    r"\(NEEDED\).*Shared library: \[libzstd\.so\.1\]", dynamic):
                raise RuntimeError(
                    "Linux Blosc does not link to the pinned Zstd shared-library ABI.")

            runtimeEnv = os.environ.copy()
            runtimeEnv["LD_LIBRARY_PATH"] = os.path.join(context.instDir, "lib")
            loaded = subprocess.check_output(
                ["ldd", bloscLibrary], text=True, stderr=subprocess.STDOUT,
                env=runtimeEnv)
            provider = re.search(
                r"^\s*libzstd\.so\.1\s+=>\s+(\S+)", loaded, re.M)
            if (not provider or
                    os.path.realpath(provider.group(1)) != os.path.realpath(zstdLibrary)):
                raise RuntimeError(
                    "Linux Blosc runtime provider is not the exact pinned SDK Zstd library.")

            symbols = GetElfDynamicSymbols(bloscLibrary)
            publicHeader = os.path.join(os.getcwd(), "blosc", "blosc.h")
            with open(publicHeader, "r", encoding="utf-8") as header:
                expectedApi = set(re.findall(
                    r"\bBLOSC_EXPORT\s+[^;]*?\b(blosc_[A-Za-z0-9_]+)\s*\(",
                    header.read(), re.S))
            missingApi = sorted(expectedApi.difference(symbols))
            leakedZstd = sorted(
                name for name in symbols
                if name.startswith(("ZSTD_", "ZDICT_")))
            if not expectedApi or missingApi or leakedZstd:
                raise RuntimeError(
                    "Linux Blosc ELF API verification failed; missing={}, "
                    "leakedZstd={}".format(missingApi, leakedZstd))
'@
    $bloscConfigureMarker = $bloscConfigureMarker.Replace("`r`n", "`n").TrimEnd()
    $bloscConfigurePatched = $bloscConfigurePatched.Replace("`r`n", "`n").TrimEnd()
    $normalizedContent = $content.Replace("`r`n", "`n")
    $preimageCount = [regex]::Matches(
        $normalizedContent,
        [regex]::Escape($bloscConfigureMarker)).Count
    $postimageCount = [regex]::Matches(
        $normalizedContent,
        [regex]::Escape($bloscConfigurePatched)).Count
    if ($preimageCount -eq 1 -and $postimageCount -eq 0)
    {
        $normalizedContent = $normalizedContent.Replace(
            $bloscConfigureMarker, $bloscConfigurePatched)
        $postimageCount = [regex]::Matches(
            $normalizedContent,
            [regex]::Escape($bloscConfigurePatched)).Count
        $preimageCount = [regex]::Matches(
            $normalizedContent,
            [regex]::Escape($bloscConfigureMarker)).Count
    }
    if ($preimageCount -ne 0 -or $postimageCount -ne 1)
    {
        throw "Expected exactly one patched Blosc installer and no preimage; found preimage=$preimageCount, postimage=$postimageCount."
    }
    $content = $normalizedContent

    $codecDependencies = @'
LIBDEFLATE_URL = "locked-libdeflate"
IMATH_URL = "locked-imath"
ZSTD_URL = "locked-zstd"

def InstallLibdeflate(context, force, buildArgs):
    with CurrentWorkingDirectory(DownloadURL(LIBDEFLATE_URL, context, force)):
        RunCMake(context, force, buildArgs + [
            '-DLIBDEFLATE_BUILD_SHARED_LIB=ON',
            '-DLIBDEFLATE_BUILD_STATIC_LIB=OFF'])

LIBDEFLATE = Dependency("libdeflate", InstallLibdeflate, "include/libdeflate.h")

def InstallImath(context, force, buildArgs):
    with CurrentWorkingDirectory(DownloadURL(IMATH_URL, context, force)):
        RunCMake(context, force, buildArgs + [
            '-DBUILD_SHARED_LIBS=ON', '-DBUILD_TESTING=OFF',
            '-DIMATH_BUILD_PYTHON=OFF'])

IMATH = Dependency("Imath", InstallImath, "include/Imath/ImathConfig.h")

def GetElfDynamicSymbols(binaryPath):
    import subprocess
    output = subprocess.check_output(
        ["readelf", "--dyn-syms", "--wide", binaryPath],
        text=True, stderr=subprocess.STDOUT)
    symbols = {}
    for line in output.splitlines():
        fields = line.split()
        if (len(fields) >= 8 and fields[0].endswith(":")
                and fields[6] != "UND" and fields[4] in ("GLOBAL", "WEAK")):
            symbols[fields[7].split("@", 1)[0]] = fields[5]
    if not symbols:
        raise RuntimeError("ELF dynamic symbol table is empty: {}".format(binaryPath))
    return symbols

def VerifyLinuxZstdApi(context):
    includeDir = os.path.join(context.instDir, "include")
    library = os.path.join(context.instDir, "lib", "libzstd.so")
    if not os.path.isfile(library):
        raise RuntimeError("The pinned Linux Zstd shared library was not installed.")

    publicApi = set()
    apiPattern = re.compile(
        r'\b(?:ZSTDLIB_API|ZDICTLIB_API|ZSTDERRORLIB_API)\s+'
        r'[^;\n]*(?:\n[ \t]+[^#;\n]+)*?'
        r'\b((?:ZSTD|ZDICT)_[A-Za-z0-9_]+)\s*\(', re.M)
    for header in ("zstd.h", "zdict.h", "zstd_errors.h"):
        path = os.path.join(includeDir, header)
        if not os.path.isfile(path):
            raise RuntimeError("The pinned Linux Zstd public header is missing: {}".format(header))
        with open(path, "r", encoding="utf-8") as source:
            publicApi.update(apiPattern.findall(source.read()))

    symbols = GetElfDynamicSymbols(library)
    missing = sorted(publicApi.difference(symbols))
    wrongVisibility = sorted(
        name for name in publicApi if symbols.get(name) != "PROTECTED")
    unprotectedCodecSymbols = sorted(
        name for name, visibility in symbols.items()
        if name.startswith(("ZSTD_", "ZDICT_")) and visibility != "PROTECTED")
    if (not publicApi or missing or wrongVisibility or unprotectedCodecSymbols):
        raise RuntimeError(
            "Linux Zstd ELF API verification failed; missing={}, "
            "wrongVisibility={}, unprotectedCodecSymbols={}".format(
                missing, wrongVisibility, unprotectedCodecSymbols))

def InstallZstd(context, force, buildArgs):
    source = DownloadURL(ZSTD_URL, context, force)
    with CurrentWorkingDirectory(os.path.join(source, "build", "cmake")):
        cmakeArgs = [
            '-DZSTD_BUILD_SHARED=ON', '-DZSTD_BUILD_STATIC=OFF',
            '-DZSTD_BUILD_PROGRAMS=OFF', '-DZSTD_BUILD_TESTS=OFF']
        if Linux():
            cmakeArgs += [
                '-DCMAKE_C_VISIBILITY_PRESET=hidden',
                '-DZSTDLIB_VISIBLE=protected',
                '-DZSTDERRORLIB_VISIBLE=protected',
                '-DZDICTLIB_VISIBLE=protected']
        RunCMake(context, force, buildArgs + cmakeArgs)
    if Linux():
        VerifyLinuxZstdApi(context)

ZSTD = Dependency("zstd", InstallZstd, "include/zstd.h")

'@
    $openExrMarker = 'def InstallOpenEXR(context, force, buildArgs):'
    $codecInstallerMarker = 'def InstallZstd(context, force, buildArgs):'
    $openExrMarkerCount = [regex]::Matches(
        $content, [regex]::Escape($openExrMarker)).Count
    $codecInstallerCount = [regex]::Matches(
        $content, [regex]::Escape($codecInstallerMarker)).Count
    if ($openExrMarkerCount -ne 1 -or $codecInstallerCount -ne 0)
    {
        throw "Expected one OpenEXR insertion point and no existing codec installer; found openExr=$openExrMarkerCount codec=$codecInstallerCount."
    }
    $content = $content.Replace($openExrMarker, $codecDependencies.Replace("`r`n", "`n") + "`n" + $openExrMarker)
    $codecInstallerCount = [regex]::Matches(
        $content, [regex]::Escape($codecInstallerMarker)).Count
    if ($codecInstallerCount -ne 1)
    {
        throw "Expected one inserted Zstd installer, found $codecInstallerCount."
    }

    $requiredDependenciesMarker = 'requiredDependencies = [TBB]'
    $requiredDependenciesPatched = 'requiredDependencies = [TBB, LIBDEFLATE, IMATH, ZSTD]'
    $requiredDependenciesMarkerCount = [regex]::Matches(
        $content, [regex]::Escape($requiredDependenciesMarker)).Count
    $requiredDependenciesPatchedCount = [regex]::Matches(
        $content, [regex]::Escape($requiredDependenciesPatched)).Count
    if ($requiredDependenciesMarkerCount -ne 1 -or $requiredDependenciesPatchedCount -ne 0)
    {
        throw "Expected one OpenEXR codec dependency preimage and no postimage; found preimage=$requiredDependenciesMarkerCount postimage=$requiredDependenciesPatchedCount."
    }
    $content = $content.Replace(
        $requiredDependenciesMarker, $requiredDependenciesPatched)
    if ([regex]::Matches($content, [regex]::Escape($requiredDependenciesMarker)).Count -ne 0 -or
        [regex]::Matches($content, [regex]::Escape($requiredDependenciesPatched)).Count -ne 1)
    {
        throw 'The OpenEXR codec dependency postimage is not unique.'
    }

    $internalImathMarker = '-DOPENEXR_FORCE_INTERNAL_IMATH=ON'
    $externalImathPatched = '-DOPENEXR_FORCE_INTERNAL_IMATH=OFF'
    $internalImathMarkerCount = [regex]::Matches(
        $content, [regex]::Escape($internalImathMarker)).Count
    $externalImathPatchedCount = [regex]::Matches(
        $content, [regex]::Escape($externalImathPatched)).Count
    if ($internalImathMarkerCount -ne 1 -or $externalImathPatchedCount -ne 0)
    {
        throw "Expected one internal-Imath preimage and no external-Imath postimage; found preimage=$internalImathMarkerCount postimage=$externalImathPatchedCount."
    }
    $content = $content.Replace($internalImathMarker, $externalImathPatched)
    if ([regex]::Matches($content, [regex]::Escape($internalImathMarker)).Count -ne 0 -or
        [regex]::Matches($content, [regex]::Escape($externalImathPatched)).Count -ne 1)
    {
        throw 'The OpenEXR Imath selection postimage is not unique.'
    }

    $dependencyVariables = @{
        oneTBB = 'ONETBB_URL'
        zlib = 'ZLIB_URL'
        'libjpeg-turbo' = 'JPEG_URL'
        libtiff = 'TIFF_URL'
        libpng = 'PNG_URL'
        OpenEXR = 'OPENEXR_URL'
        Ptex = 'PTEX_URL'
        Blosc = 'BLOSC_URL'
        OpenVDB = 'OPENVDB_URL'
        OpenImageIO = 'OIIO_URL'
        OpenColorIO = 'OCIO_URL'
        OpenSubdiv = 'OPENSUBDIV_URL'
        Alembic = 'ALEMBIC_URL'
        Draco = 'DRACO_URL'
        MaterialX = 'MATERIALX_URL'
        libdeflate = 'LIBDEFLATE_URL'
        Imath = 'IMATH_URL'
        zstd = 'ZSTD_URL'
    }
    foreach ($group in $lock.dependencies | Group-Object name)
    {
        $dependency = $group.Group[0]
        foreach ($platformDependency in $group.Group)
        {
            if ($platformDependency.version -cne $dependency.version -or
                $platformDependency.url -cne $dependency.url -or
                $platformDependency.sha256 -cne $dependency.sha256)
            {
                throw "The reviewed build-script binding requires one source identity for '$($dependency.name)'."
            }
        }
        if ($dependency.name -eq 'Boost')
        {
            $version = $dependency.version.Replace('.', ', ')
            $content = [regex]::Replace($content, 'BOOST_VERSION = \(\d+, \d+, \d+\)', "BOOST_VERSION = ($version)")
            $content = [regex]::Replace(
                $content,
                'BOOST_SHA256 = "[0-9a-f]+"',
                "BOOST_SHA256 = `"$($dependency.sha256.ToLowerInvariant())`"")
            $content = $content.Replace(
                '"*/libs/*/doc/*",',
                '"*/libs/*/doc/*",' + "`n" + '        "*/libs/*/test/*",')
            continue
        }
        $variable = $dependencyVariables[$dependency.name]
        if (-not $variable)
        {
            throw "No build-script source binding exists for locked dependency '$($dependency.name)'."
        }
        $pattern = '(?m)^(\s*' + [regex]::Escape($variable) + '\s*=\s*)"[^"]+"'
        if (-not [regex]::IsMatch($content, $pattern))
        {
            throw "The upstream build script does not declare locked dependency '$variable'."
        }
        $url = [string]$dependency.url
        $content = [regex]::Replace($content, $pattern, {
            param($match)
            $match.Groups[1].Value + '"' + $url + '"'
        })
        if ($dependency.name -eq 'Ptex')
        {
            $content = [regex]::Replace($content, 'PTEX_VERSION = "[^"]+"', "PTEX_VERSION = `"$($dependency.tag)`"")
        }
    }

    [System.IO.File]::WriteAllText(
        $buildScript,
        $content,
        [System.Text.UTF8Encoding]::new($false))
    $buildScriptHash = (Get-FileHash $buildScript -Algorithm SHA256).Hash
}

if ($buildScriptHash -ne $lock.openUsd.patchedBuildScriptSha256)
{
    throw "OpenUSD patched build script hash mismatch. Expected $($lock.openUsd.patchedBuildScriptSha256), got $buildScriptHash."
}

$runtimePatchLock = Join-Path $PSScriptRoot 'openusd-runtime-patches.lock.json'
if ((Get-FileHash -LiteralPath $runtimePatchLock).Hash -cne $lock.openUsd.runtimePatchLockSha256)
{
    throw 'The required runtime patch lock does not match the pinned OpenUSD source configuration.'
}
& (Join-Path $PSScriptRoot 'apply-openusd-source-patches.ps1') `
    -SourceRoot $openUsdSource -PatchLockPath $runtimePatchLock | Out-Null

$patchSet = $null
if (-not [string]::IsNullOrWhiteSpace($PatchLockPath))
{
    $patchSet = & (Join-Path $PSScriptRoot 'apply-openusd-source-patches.ps1') `
        -SourceRoot $openUsdSource -PatchLockPath $PatchLockPath
}

[pscustomobject]@{
    Rid = $Rid
    RepositoryRoot = $repoRoot.Path
    CacheRoot = $CacheRoot
    SourceRoot = $openUsdSource
    BuildScript = $buildScript
    PatchSet = $patchSet
}
