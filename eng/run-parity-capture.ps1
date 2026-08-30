#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$Rid = $(
        if ($IsWindows) { 'win-x64' } elseif ($IsLinux) { 'linux-x64' } elseif ($IsMacOS) { 'osx-arm64' } else { '' }),

    [ValidateNotNullOrEmpty()]
    [string]$Configuration = 'Release',

    [string]$NativeInstallRoot,

    [ValidateSet('Auto', 'Mesa')]
    [string]$StormGl = 'Auto',

    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($Rid))
{
    throw 'Parity capture is supported only on Windows, Linux, and macOS arm64.'
}

$installRoot = if ([string]::IsNullOrWhiteSpace($NativeInstallRoot))
{
    Join-Path $repoRoot 'native/install'
}
else
{
    [System.IO.Path]::GetFullPath($NativeInstallRoot)
}
$openUsdRoot = Join-Path $installRoot $Rid
$shimRoot = Join-Path $installRoot "shim/$Rid"
foreach ($required in @($openUsdRoot, $shimRoot))
{
    if (-not (Test-Path -LiteralPath $required -PathType Container))
    {
        throw "Parity capture native runtime is missing: $required"
    }
}

$stageRoot = Join-Path $repoRoot "artifacts/parity-capture/$Rid"
$runtimeRoot = Join-Path $stageRoot 'runtime'
$binTarget = Join-Path $runtimeRoot 'bin'
$libTarget = Join-Path $runtimeRoot 'lib'
$pluginPath = Join-Path $runtimeRoot 'plugin/usd'
$testHostRoot = Join-Path $repoRoot "tests/OpenUsd.Rendering.ConformanceTests/bin/$Configuration/net10.0"
$testHostOpenGl = Join-Path $testHostRoot 'opengl32.dll'
$removeTestHostOpenGlInFinally = $false
Remove-Item $runtimeRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $binTarget, $libTarget, $pluginPath | Out-Null

function Remove-TestHostMesaOpenGl
{
    if (Test-Path -LiteralPath $testHostOpenGl -PathType Leaf)
    {
        Remove-Item -LiteralPath $testHostOpenGl -Force
        Write-Host "[parity-capture] removed stale test-host OpenGL override: $testHostOpenGl"
    }
}

function Invoke-WindowsSystemWglPreflight
{
    param(
        [Parameter(Mandatory = $true)][string]$ReportPath
    )

    $probeRoot = Join-Path $stageRoot 'system-wgl-preflight'
    $projectRoot = Join-Path $probeRoot 'SystemWglPreflight'
    $publishRoot = Join-Path $probeRoot 'publish'
    Remove-Item $publishRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $projectRoot | Out-Null
    Set-Content -LiteralPath (Join-Path $projectRoot 'SystemWglPreflight.csproj') -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
'@
    Set-Content -LiteralPath (Join-Path $projectRoot 'Program.cs') -Value @'
// Copyright (c) marcschier. Licensed under the MIT License.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 1)
{
    throw new ArgumentException("Usage: SystemWglPreflight <report>");
}

var reportPath = Path.GetFullPath(args[0]);
var className = "OpenUsdSystemWglPreflight";
var instance = Native.GetModuleHandle(null);
var wc = new Native.WndClass
{
    Style = Native.CsOwnDc,
    WindowProc = Native.DefWindowProc,
    Instance = instance,
    ClassName = className
};

if (Native.RegisterClass(ref wc) == 0)
{
    var error = Marshal.GetLastWin32Error();
    if (error != Native.ErrorClassAlreadyExists)
    {
        throw new InvalidOperationException($"RegisterClass failed: {error}");
    }
}

var hwnd = Native.CreateWindowEx(
    0,
    className,
    "OpenUSD system WGL preflight",
    Native.WsOverlappedWindow,
    0,
    0,
    1,
    1,
    IntPtr.Zero,
    IntPtr.Zero,
    instance,
    IntPtr.Zero);
if (hwnd == IntPtr.Zero)
{
    throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
}

var hdc = Native.GetDC(hwnd);
if (hdc == IntPtr.Zero)
{
    throw new InvalidOperationException($"GetDC failed: {Marshal.GetLastWin32Error()}");
}

var pfd = Native.PixelFormatDescriptor.Default;
var pixelFormat = Native.ChoosePixelFormat(hdc, ref pfd);
if (pixelFormat == 0)
{
    throw new InvalidOperationException($"ChoosePixelFormat failed: {Marshal.GetLastWin32Error()}");
}
if (!Native.SetPixelFormat(hdc, pixelFormat, ref pfd))
{
    throw new InvalidOperationException($"SetPixelFormat failed: {Marshal.GetLastWin32Error()}");
}

var context = Native.wglCreateContext(hdc);
if (context == IntPtr.Zero)
{
    throw new InvalidOperationException($"wglCreateContext failed: {Marshal.GetLastWin32Error()}");
}
if (!Native.wglMakeCurrent(hdc, context))
{
    throw new InvalidOperationException($"wglMakeCurrent failed: {Marshal.GetLastWin32Error()}");
}

var vendor = Native.GetGlString(Native.GlVendor);
var renderer = Native.GetGlString(Native.GlRenderer);
var version = Native.GetGlString(Native.GlVersion);
var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().FirstOrDefault(
    candidate => string.Equals(candidate.ModuleName, "opengl32.dll", StringComparison.OrdinalIgnoreCase));
var loadedPath = module?.FileName ?? string.Empty;
var loadedSha256 = File.Exists(loadedPath)
    ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(loadedPath)))
    : string.Empty;
var supportsStorm = TryGetMajorVersion(version, out int major) && major >= 4;

Native.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
Native.wglDeleteContext(context);
_ = Native.ReleaseDC(hwnd, hdc);
_ = Native.DestroyWindow(hwnd);

var report = new SortedDictionary<string, object?>
{
    ["schemaVersion"] = 1,
    ["loadedOpenGl32"] = loadedPath,
    ["loadedOpenGl32Sha256"] = loadedSha256,
    ["glVendor"] = vendor,
    ["glRenderer"] = renderer,
    ["glVersion"] = version,
    ["supportsStorm"] = supportsStorm
};
Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

if (!supportsStorm)
{
    throw new InvalidOperationException($"System WGL reported GL_VERSION='{version}', which is not usable for Storm.");
}

static bool TryGetMajorVersion(string version, out int major)
{
    major = 0;
    int dot = version.IndexOf('.');
    string token = dot < 0 ? version : version[..dot];
    return int.TryParse(token, out major);
}

internal static partial class Native
{
    public const int CsOwnDc = 0x0020;
    public const int ErrorClassAlreadyExists = 1410;
    public const uint WsOverlappedWindow = 0x00CF0000;
    public const uint PfdDrawToWindow = 0x00000004;
    public const uint PfdSupportOpenGl = 0x00000020;
    public const uint PfdDoubleBuffer = 0x00000001;
    public const byte PfdTypeRgba = 0;
    public const byte PfdMainPlane = 0;
    public const uint GlVendor = 0x1F00;
    public const uint GlRenderer = 0x1F01;
    public const uint GlVersion = 0x1F02;

    public delegate IntPtr WindowProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WndClass
    {
        public uint Style;
        public WindowProc WindowProc;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PixelFormatDescriptor
    {
        public ushort Size;
        public ushort Version;
        public uint Flags;
        public byte PixelType;
        public byte ColorBits;
        public byte RedBits;
        public byte RedShift;
        public byte GreenBits;
        public byte GreenShift;
        public byte BlueBits;
        public byte BlueShift;
        public byte AlphaBits;
        public byte AlphaShift;
        public byte AccumBits;
        public byte AccumRedBits;
        public byte AccumGreenBits;
        public byte AccumBlueBits;
        public byte AccumAlphaBits;
        public byte DepthBits;
        public byte StencilBits;
        public byte AuxBuffers;
        public byte LayerType;
        public byte Reserved;
        public uint LayerMask;
        public uint VisibleMask;
        public uint DamageMask;

        public static PixelFormatDescriptor Default => new()
        {
            Size = (ushort)Marshal.SizeOf<PixelFormatDescriptor>(),
            Version = 1,
            Flags = PfdDrawToWindow | PfdSupportOpenGl | PfdDoubleBuffer,
            PixelType = PfdTypeRgba,
            ColorBits = 32,
            DepthBits = 24,
            StencilBits = 8,
            LayerType = PfdMainPlane
        };
    }

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", EntryPoint = "RegisterClassW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClass(ref WndClass wndClass);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode)]
    public static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int ChoosePixelFormat(IntPtr hdc, ref PixelFormatDescriptor pixelFormatDescriptor);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetPixelFormat(
        IntPtr hdc,
        int format,
        ref PixelFormatDescriptor pixelFormatDescriptor);

    [DllImport("opengl32.dll", SetLastError = true)]
    public static extern IntPtr wglCreateContext(IntPtr hdc);

    [DllImport("opengl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool wglMakeCurrent(IntPtr hdc, IntPtr context);

    [DllImport("opengl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool wglDeleteContext(IntPtr context);

    [DllImport("opengl32.dll", EntryPoint = "glGetString")]
    private static extern IntPtr GetGlStringPointer(uint name);

    public static string GetGlString(uint name)
    {
        var pointer = GetGlStringPointer(name);
        return pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(pointer) ?? string.Empty;
    }
}
'@

    $publishOutput = & dotnet publish $projectRoot -c Release -r win-x64 --self-contained false -o $publishRoot 2>&1
    $publishExitCode = $LASTEXITCODE
    $publishOutput | ForEach-Object { Write-Host $_ }
    if ($publishExitCode -ne 0)
    {
        Write-Warning "System WGL preflight publish exited with code $publishExitCode."
        return $null
    }

    $preflightExe = Join-Path $publishRoot 'SystemWglPreflight.exe'
    & $preflightExe $ReportPath
    if ($LASTEXITCODE -ne 0)
    {
        Write-Warning "System WGL preflight exited with code $LASTEXITCODE."
        return $null
    }

    return Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
}

function Enable-MesaWglParity
{
    $mesaStageRoot = & (Join-Path $PSScriptRoot 'prepare-mesa-wgl-test-runtime.ps1') `
        -Root (Join-Path $stageRoot 'mesa-wgl-runtime') `
        -Rid $Rid `
        -Activate `
        -Preflight
    $mesaOpenGl = Join-Path $mesaStageRoot 'opengl32.dll'
    foreach ($mesaTarget in @($testHostRoot, $binTarget))
    {
        New-Item -ItemType Directory -Force -Path $mesaTarget | Out-Null
        Copy-Item -LiteralPath $mesaOpenGl -Destination $mesaTarget -Force
        $copied = Join-Path $mesaTarget 'opengl32.dll'
        $actualHash = (Get-FileHash -LiteralPath $copied -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($actualHash -cne $env:OPENUSD_MESA_WGL_OPENGL32_SHA256)
        {
            throw "Copied Mesa opengl32.dll hash mismatch for '$copied'."
        }
        Write-Host "[mesa-wgl-runtime] copied=$copied sha256=$actualHash"
    }
    $env:OPENUSD_MESA_WGL_OPENGL32_PATH = $testHostOpenGl
    $script:removeTestHostOpenGlInFinally = $true
    Write-Host (
        "[parity-capture] StormGl=Mesa gates " +
        "$($env:OPENUSD_PARITY_EXPECTED_SCENE_COUNT) scenes; excludes " +
        "$($env:OPENUSD_PARITY_EXPECTED_EXCLUDED_SCENES).")
}

function New-StormParityTreeFilter
{
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$TestNames
    )

    return '/*/*/StormSilkParityCaptureDriverTests/(' + ($TestNames -join '|') + ')'
}

foreach ($layout in @(
    @{ Source = (Join-Path $openUsdRoot 'bin'); Target = $binTarget },
    @{ Source = (Join-Path $openUsdRoot 'lib'); Target = $libTarget },
    @{ Source = (Join-Path $shimRoot 'bin'); Target = $binTarget },
    @{ Source = (Join-Path $shimRoot 'lib'); Target = $libTarget }))
{
    if (Test-Path -LiteralPath $layout.Source -PathType Container)
    {
        Get-ChildItem -LiteralPath $layout.Source -File |
            Where-Object { $_.Name -match '\.dll$|\.dylib$|\.so($|\.)' } |
            Copy-Item -Destination $layout.Target -Force
    }
}

foreach ($pluginSource in @(
    (Join-Path (Join-Path $openUsdRoot 'lib') 'usd'),
    (Join-Path (Join-Path $openUsdRoot 'plugin') 'usd'),
    (Join-Path (Join-Path $shimRoot 'plugin') 'usd')))
{
    if (Test-Path -LiteralPath $pluginSource -PathType Container)
    {
        Copy-Item (Join-Path $pluginSource '*') $pluginPath -Recurse -Force
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $pluginPath 'plugInfo.json') -PathType Leaf))
{
    throw "Parity capture could not stage an OpenUSD plugin root at $pluginPath."
}

if (-not $SkipBuild)
{
    & dotnet build `
        (Join-Path $repoRoot 'tests/OpenUsd.Rendering.ConformanceTests/OpenUsd.Rendering.ConformanceTests.csproj') `
        -c $Configuration `
        -f net10.0
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}

$oldPath = $env:PATH
$oldLdLibraryPath = $env:LD_LIBRARY_PATH
$oldDyldLibraryPath = $env:DYLD_LIBRARY_PATH
$oldPluginPath = $env:OPENUSD_PLUGIN_PATH
$oldCaptureRequired = $env:OPENUSD_PARITY_CAPTURE_REQUIRED
$oldMesaOpenGl = $env:OPENUSD_MESA_WGL_OPENGL32_PATH
$oldMesaOpenGlSha256 = $env:OPENUSD_MESA_WGL_OPENGL32_SHA256
$oldMesaArchiveUrl = $env:OPENUSD_MESA_WGL_ARCHIVE_URL
$oldMesaArchiveSha256 = $env:OPENUSD_MESA_WGL_ARCHIVE_SHA256
$oldGalliumDriver = $env:GALLIUM_DRIVER
$oldLibGlAlwaysSoftware = $env:LIBGL_ALWAYS_SOFTWARE
$oldLlvmPipeThreads = $env:LP_NUM_THREADS
$oldMesaShaderCacheDisable = $env:MESA_SHADER_CACHE_DISABLE
$oldExpectedSceneCount = $env:OPENUSD_PARITY_EXPECTED_SCENE_COUNT
$oldExpectedExcludedScenes = $env:OPENUSD_PARITY_EXPECTED_EXCLUDED_SCENES
$oldWindowsBackends = $env:OPENUSD_PARITY_WINDOWS_BACKENDS
try
{
    $env:PATH = $binTarget + [System.IO.Path]::PathSeparator +
        $libTarget + [System.IO.Path]::PathSeparator + $oldPath
    $env:LD_LIBRARY_PATH = $libTarget + [System.IO.Path]::PathSeparator +
        $binTarget + [System.IO.Path]::PathSeparator + $oldLdLibraryPath
    $env:DYLD_LIBRARY_PATH = $libTarget + [System.IO.Path]::PathSeparator +
        $binTarget + [System.IO.Path]::PathSeparator + $oldDyldLibraryPath
    $env:OPENUSD_PLUGIN_PATH = $pluginPath
    $env:OPENUSD_PARITY_CAPTURE_REQUIRED = '1'
    $env:OPENUSD_PARITY_WINDOWS_BACKENDS = $null

    # Declared once, deliberately, rather than derived from the scene list: an
    # expected count that reads from the same source it is checking would pass
    # no matter how many scenes silently disappeared. Adding a parity scene
    # means incrementing this by one, and adding one that Storm cannot render
    # on Mesa llvmpipe also means naming it in $mesaExcludedScenes.
    $totalParityScenes = 25
    $mesaExcludedScenes = @(
        'single-sided-winding',
        'bounds-draw-mode',
        'origin-draw-mode',
        'subdivision-catmull-clark')
    $mesaSceneCount = $totalParityScenes - $mesaExcludedScenes.Count
    $mesaExcludedList = $mesaExcludedScenes -join ','
    $env:OPENUSD_PARITY_EXPECTED_SCENE_COUNT = "$totalParityScenes"
    $env:OPENUSD_PARITY_EXPECTED_EXCLUDED_SCENES = ''

    if ($Rid -eq 'win-x64')
    {
        if ($StormGl -eq 'Mesa')
        {
            $env:OPENUSD_PARITY_EXPECTED_SCENE_COUNT = "$mesaSceneCount"
            $env:OPENUSD_PARITY_EXPECTED_EXCLUDED_SCENES = $mesaExcludedList
            # Render run 31263500952 showed hosted WGL has no system Vulkan ICD:
            # Mesa WGL parity proves the OpenGL route, not Vulkan device creation.
            $env:OPENUSD_PARITY_WINDOWS_BACKENDS = 'D3D12'
            Enable-MesaWglParity
        }
        else
        {
            Remove-TestHostMesaOpenGl
            $env:OPENUSD_MESA_WGL_OPENGL32_PATH = $null
            $env:OPENUSD_MESA_WGL_OPENGL32_SHA256 = $null
            $env:OPENUSD_PARITY_EXPECTED_SCENE_COUNT = "$totalParityScenes"
            $env:OPENUSD_PARITY_EXPECTED_EXCLUDED_SCENES = ''
            $systemReportPath = Join-Path $stageRoot 'system-wgl-preflight.json'
            $systemReport = Invoke-WindowsSystemWglPreflight -ReportPath $systemReportPath
            if ($null -ne $systemReport -and $systemReport.supportsStorm)
            {
                Write-Host (
                    "[parity-capture] StormGl=Auto using system OpenGL; " +
                    "gates $totalParityScenes scenes; " +
                    "loadedOpenGl32=$($systemReport.loadedOpenGl32) " +
                    "sha256=$($systemReport.loadedOpenGl32Sha256)")
                Write-Host (
                    "[parity-capture] System GL_VENDOR='$($systemReport.glVendor)' " +
                    "GL_RENDERER='$($systemReport.glRenderer)' GL_VERSION='$($systemReport.glVersion)'")
            }
            else
            {
                $env:OPENUSD_PARITY_EXPECTED_SCENE_COUNT = "$mesaSceneCount"
                $env:OPENUSD_PARITY_EXPECTED_EXCLUDED_SCENES = $mesaExcludedList
                $env:OPENUSD_PARITY_WINDOWS_BACKENDS = 'D3D12'
                Write-Warning (
                    'StormGl=Auto could not find a system WGL implementation usable by Storm; ' +
                    "falling back to Mesa llvmpipe. This gates $mesaSceneCount scenes, not " +
                    "$totalParityScenes, and excludes $mesaExcludedList.")
                Enable-MesaWglParity
            }
        }
    }
    elseif ($StormGl -eq 'Mesa')
    {
        throw 'StormGl=Mesa is supported only for win-x64.'
    }

    $testProject = 'tests/OpenUsd.Rendering.ConformanceTests/OpenUsd.Rendering.ConformanceTests.csproj'
    $windowsWglTestNames = @(
        'CapturesStormAndHdSilkBackendsDeterministically',
        'ComparisonDetectsPerturbedCaptures',
        'CuratedSceneParityClaimsAreStructured',
        'DisplayColorReachesPixelsForImplicitSurfacesAndMeshes',
        'SilkComplexityDefaultPreservesExplicitLowPointPage',
        'SilkComplexityMediumChangesPointPage',
        'HdSilkDomeAmbientPreservesAuthoredColorIntensityAndExposure',
        'ChasePresentationRetainsHighlightsOnD3D12',
        'SilkWireframeDrawModeDivergesFromSmoothShadedPixelsOnD3D12',
        'SilkFrameCaptureReturnsDimensionsAndNonTrivialPixels',
        'SilkFrameCaptureRendersEveryFrameFromTheSameSession',
        'SilkFrameCaptureRefusesToCaptureABlankFrameFromASynchronizedSession',
        'SilkFrameCaptureReturnsBlankFrameForEmptyUnsynchronizedSession',
        'SilkFrameCaptureRetainedRendersTheSceneALiveRendererSynchronized',
        'SilkFrameCaptureAppliesOcioDisplayTransform',
        'MixedTextureSlotWrapModesRemainIndependentOnD3D12',
        'OpacityAndOcclusionTextureSlotsRenderIndependentlyOnD3D12',
        'SpecularColorTextureSlotRendersIndependentlyOnD3D12',
        'ClearcoatTextureSlotsRenderIndependentlyOnD3D12',
        'IorTextureSlotRendersIndependentlyOnD3D12',
        'FloatTexturePreservesHdrBeforeScaleOnD3D12',
        'SparseUdimTilesAndFallbackRenderOnD3D12',
        'MaterialXGeneratedUnlitMatchesPreviewSelfConsistencyOnD3D12')
    $macosCglTestNames = @(
        'CapturesStormAndHdSilkBackendsDeterministically',
        'ComparisonDetectsPerturbedCaptures',
        'CuratedSceneParityClaimsAreStructured',
        'SilkComplexityDefaultPreservesExplicitLowPointPage',
        'SilkComplexityMediumChangesPointPage',
        'HdSilkDomeAmbientPreservesAuthoredColorIntensityAndExposure',
        'MaterialXGeneratedUnlitMatchesPreviewSelfConsistencyOnMetal')
    $linuxGlxTestNames = @(
        'CapturesStormAndHdSilkBackendsDeterministically',
        'ComparisonDetectsPerturbedCaptures',
        'CuratedSceneParityClaimsAreStructured',
        'SilkComplexityDefaultPreservesExplicitLowPointPage',
        'SilkComplexityMediumChangesPointPage',
        'HdSilkDomeAmbientPreservesAuthoredColorIntensityAndExposure',
        'ChasePresentationRetainsHighlightsOnVulkan',
        'MaterialXStandardSurfaceMatchesPreviewSelfConsistencyOnVulkan',
        'MaterialXGeneratedUnlitMatchesPreviewSelfConsistencyOnVulkan',
        'TextureWrapModesMatchRepeatWithinUnitUvRangeOnVulkan',
        'TextureWrapModesDivergeOutsideUnitUvRangeOnVulkan',
        'TextureAutoColorSpaceMatchesSrgbOnDiffuseTextureOnVulkan',
        'TextureRawColorSpaceDivergesFromSrgbOnDiffuseTextureOnVulkan',
        'TextureScaleBiasFallbackMatchesEquivalentConstantOnVulkan',
        'TextureScaleBiasFallbackDivergesWhenBiasIsRemovedOnVulkan',
        'MixedTextureSlotWrapModesRemainIndependentOnVulkan',
        'FloatTexturePreservesHdrBeforeScaleOnVulkan',
        'SparseUdimTilesAndFallbackRenderOnVulkan',
        'NonDiffuseTextureSlotsMatchNeutralInputsOnVulkan',
        'NonDiffuseTextureSlotsDivergeFromNeutralInputsOnVulkan',
        'RemainingPreviewSurfaceConstantInputsMatchEquivalentMaterialsOnVulkan',
        'RemainingPreviewSurfaceConstantInputsDivergeFromEquivalentMaterialsOnVulkan',
        'CullStyleBackMatchesBackUnlessDoubleSidedForSingleSidedMeshOnVulkan',
        'CullStyleBackDivergesFromBackUnlessDoubleSidedForDoubleSidedBackFacesOnVulkan',
        'RectLightZeroAreaMatchesSphereLightOnVulkan',
        'RectLightFullAreaDivergesFromSphereLightOnVulkan',
        'DiskLightEdgeOnMatchesUnlitSceneOnVulkan',
        'DiskLightFaceOnDivergesFromEdgeOnLightOnVulkan',
        'CylinderLightZeroLengthMatchesSphereLightOnVulkan',
        'CylinderLightFullLengthDivergesFromSphereLightOnVulkan',
        'FifthDirectLightContributesOnVulkan')
    if ($Rid -eq 'win-x64')
    {
        & (Join-Path $PSScriptRoot 'run-managed-tests.ps1') `
            -Project $testProject `
            -Framework net10.0 `
            -Configuration $Configuration `
            -MinimumExpectedTests $windowsWglTestNames.Count `
            -TestArguments @(
                '--treenode-filter',
                (New-StormParityTreeFilter -TestNames $windowsWglTestNames))
        exit $LASTEXITCODE
    }

    if ($Rid -eq 'osx-arm64')
    {
        & (Join-Path $PSScriptRoot 'run-managed-tests.ps1') `
            -Project $testProject `
            -Framework net10.0 `
            -Configuration $Configuration `
            -MinimumExpectedTests $macosCglTestNames.Count `
            -TestArguments @(
                '--treenode-filter',
                (New-StormParityTreeFilter -TestNames $macosCglTestNames))
        exit $LASTEXITCODE
    }

    & (Join-Path $PSScriptRoot 'run-managed-tests.ps1') `
        -Project $testProject `
        -Framework net10.0 `
        -Configuration $Configuration `
        -MinimumExpectedTests $linuxGlxTestNames.Count `
        -TestArguments @(
            '--treenode-filter',
            (New-StormParityTreeFilter -TestNames $linuxGlxTestNames))
    exit $LASTEXITCODE
}
finally
{
    $env:PATH = $oldPath
    $env:LD_LIBRARY_PATH = $oldLdLibraryPath
    $env:DYLD_LIBRARY_PATH = $oldDyldLibraryPath
    $env:OPENUSD_PLUGIN_PATH = $oldPluginPath
    $env:OPENUSD_PARITY_CAPTURE_REQUIRED = $oldCaptureRequired
    $env:OPENUSD_MESA_WGL_OPENGL32_PATH = $oldMesaOpenGl
    $env:OPENUSD_MESA_WGL_OPENGL32_SHA256 = $oldMesaOpenGlSha256
    $env:OPENUSD_MESA_WGL_ARCHIVE_URL = $oldMesaArchiveUrl
    $env:OPENUSD_MESA_WGL_ARCHIVE_SHA256 = $oldMesaArchiveSha256
    $env:GALLIUM_DRIVER = $oldGalliumDriver
    $env:LIBGL_ALWAYS_SOFTWARE = $oldLibGlAlwaysSoftware
    $env:LP_NUM_THREADS = $oldLlvmPipeThreads
    $env:MESA_SHADER_CACHE_DISABLE = $oldMesaShaderCacheDisable
    $env:OPENUSD_PARITY_EXPECTED_SCENE_COUNT = $oldExpectedSceneCount
    $env:OPENUSD_PARITY_EXPECTED_EXCLUDED_SCENES = $oldExpectedExcludedScenes
    $env:OPENUSD_PARITY_WINDOWS_BACKENDS = $oldWindowsBackends
    if ($removeTestHostOpenGlInFinally)
    {
        Remove-TestHostMesaOpenGl
    }
}
