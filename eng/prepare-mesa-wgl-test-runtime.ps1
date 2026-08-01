#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Root,

    [ValidateSet('win-x64')]
    [string]$Rid = 'win-x64',

    [switch]$Activate,

    [switch]$Preflight
)

$ErrorActionPreference = 'Stop'
$rootPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Root)
$lockPath = Join-Path $PSScriptRoot 'mesa-wgl-test-runtime.lock.json'
$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
$runtime = $lock.runtimes.$Rid
if ($null -eq $runtime)
{
    throw "The Mesa WGL test runtime lock does not contain '$Rid'."
}
if (-not $IsWindows)
{
    throw 'The Mesa WGL test runtime is only supported on Windows.'
}

function Assert-Hash
{
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Expected,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($actual -cne $Expected)
    {
        throw "$Description hash mismatch for '$Path'. Expected $Expected, got $actual."
    }
    return $actual
}

function Add-EnvironmentPath
{
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $current = [Environment]::GetEnvironmentVariable($Name, 'Process')
    $entries = if ([string]::IsNullOrWhiteSpace($current))
    {
        @()
    }
    else
    {
        @($current -split [System.IO.Path]::PathSeparator)
    }
    $entries = @($entries | Where-Object { $_ -ne $Path })
    [Environment]::SetEnvironmentVariable(
        $Name,
        (@($Path) + $entries) -join [System.IO.Path]::PathSeparator,
        'Process')
}

function Invoke-WglPreflight
{
    param(
        [Parameter(Mandatory = $true)][string]$StagePath,
        [Parameter(Mandatory = $true)][string]$ExpectedOpenGlPath,
        [Parameter(Mandatory = $true)][string]$ExpectedOpenGlSha256,
        [Parameter(Mandatory = $true)][string]$ReportPath
    )

    $probeRoot = Join-Path $StagePath 'preflight'
    $projectRoot = Join-Path $probeRoot 'MesaWglPreflight'
    New-Item -ItemType Directory -Force -Path $projectRoot | Out-Null
    Set-Content -LiteralPath (Join-Path $projectRoot 'MesaWglPreflight.csproj') -Value @'
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

if (args.Length != 3)
{
    throw new ArgumentException("Usage: MesaWglPreflight <expected-opengl32> <expected-sha256> <report>");
}

var expectedPath = Path.GetFullPath(args[0]);
var expectedSha256 = args[1].ToUpperInvariant();
var reportPath = Path.GetFullPath(args[2]);
var className = "OpenUsdMesaWglPreflight";
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
    "OpenUSD Mesa WGL preflight",
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
var pixelFormat = Native.wglChoosePixelFormat(hdc, ref pfd);
if (pixelFormat == 0)
{
    throw new InvalidOperationException($"ChoosePixelFormat failed: {Marshal.GetLastWin32Error()}");
}
if (!Native.wglSetPixelFormat(hdc, pixelFormat, ref pfd))
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

Native.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
Native.wglDeleteContext(context);
_ = Native.ReleaseDC(hwnd, hdc);
_ = Native.DestroyWindow(hwnd);

var report = new SortedDictionary<string, object?>
{
    ["schemaVersion"] = 1,
    ["loadedOpenGl32"] = loadedPath,
    ["loadedOpenGl32Sha256"] = loadedSha256,
    ["expectedOpenGl32"] = expectedPath,
    ["expectedOpenGl32Sha256"] = expectedSha256,
    ["loadedExpectedOpenGl32"] = string.Equals(
        Path.GetFullPath(loadedPath),
        expectedPath,
        StringComparison.OrdinalIgnoreCase),
    ["loadedExpectedSha256"] = string.Equals(loadedSha256, expectedSha256, StringComparison.Ordinal),
    ["glVendor"] = vendor,
    ["glRenderer"] = renderer,
    ["glVersion"] = version
};
Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

if (!(bool)report["loadedExpectedOpenGl32"]! || !(bool)report["loadedExpectedSha256"]!)
{
    throw new InvalidOperationException(
        $"WGL preflight loaded '{loadedPath}' ({loadedSha256}) instead of '{expectedPath}' ({expectedSha256}).");
}
if (!renderer.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException($"WGL preflight did not report llvmpipe. Renderer: '{renderer}'.");
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

    [DllImport("opengl32.dll", SetLastError = true)]
    public static extern int wglChoosePixelFormat(IntPtr hdc, ref PixelFormatDescriptor pixelFormatDescriptor);

    [DllImport("opengl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool wglSetPixelFormat(
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

    $publishRoot = Join-Path $probeRoot 'publish'
    $publishOutput = & dotnet publish $projectRoot -c Release -r win-x64 --self-contained false -o $publishRoot 2>&1
    $publishExitCode = $LASTEXITCODE
    $publishOutput | ForEach-Object { Write-Host $_ }
    if ($publishExitCode -ne 0)
    {
        throw "Mesa WGL preflight publish exited with code $publishExitCode."
    }
    Copy-Item -LiteralPath $ExpectedOpenGlPath -Destination $publishRoot -Force
    $expectedRuntimeOpenGlPath = Join-Path $publishRoot 'opengl32.dll'
    $preflightExe = Join-Path $publishRoot 'MesaWglPreflight.exe'
    & $preflightExe $expectedRuntimeOpenGlPath $ExpectedOpenGlSha256 $ReportPath
    if ($LASTEXITCODE -ne 0)
    {
        throw "Mesa WGL preflight exited with code $LASTEXITCODE."
    }
    $report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
    Write-Host (
        "[mesa-wgl-runtime] loadedOpenGl32=$($report.loadedOpenGl32) " +
        "sha256=$($report.loadedOpenGl32Sha256)")
    Write-Host (
        "[mesa-wgl-runtime] GL_VENDOR='$($report.glVendor)' " +
        "GL_RENDERER='$($report.glRenderer)' GL_VERSION='$($report.glVersion)'")
}

New-Item -ItemType Directory -Force -Path $rootPath | Out-Null
$downloadRoot = Join-Path $rootPath 'downloads'
$stageRoot = Join-Path $rootPath "runtimes/$Rid/native"
New-Item -ItemType Directory -Force -Path $downloadRoot, $stageRoot | Out-Null
$archivePath = Join-Path $downloadRoot ([string]$lock.package.archive)
$url = [string]$lock.package.url
$archiveSha256 = [string]$lock.package.archiveSha256
if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf))
{
    Write-Host "[mesa-wgl-runtime] Downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $archivePath
}
Assert-Hash -Path $archivePath -Expected $archiveSha256 -Description 'Mesa WGL archive' | Out-Null

$tar = Get-Command tar -ErrorAction SilentlyContinue
if ($null -eq $tar)
{
    throw 'The Mesa WGL test runtime requires tar.exe to extract the pinned 7z archive.'
}
foreach ($file in $runtime.files)
{
    $relativePath = [string]$file.path
    & $tar.Source -xf $archivePath -C $stageRoot $relativePath
    if ($LASTEXITCODE -ne 0)
    {
        throw "Could not extract '$relativePath' from '$archivePath'."
    }
    $stagedPath = Join-Path $stageRoot $relativePath
    $hash = Assert-Hash `
        -Path $stagedPath `
        -Expected ([string]$file.sha256) `
        -Description 'Mesa WGL runtime file'
    Write-Host "[mesa-wgl-runtime] staged=$stagedPath sha256=$hash"
}

$openglPath = Join-Path $stageRoot 'opengl32.dll'
$openglSha256 = [string]$runtime.files[0].sha256
if ($Activate)
{
    Add-EnvironmentPath -Name 'PATH' -Path $stageRoot
    $env:OPENUSD_MESA_WGL_OPENGL32_PATH = $openglPath
    $env:OPENUSD_MESA_WGL_OPENGL32_SHA256 = $openglSha256
    $env:OPENUSD_MESA_WGL_ARCHIVE_URL = $url
    $env:OPENUSD_MESA_WGL_ARCHIVE_SHA256 = $archiveSha256
    $env:GALLIUM_DRIVER = 'llvmpipe'
    $env:LIBGL_ALWAYS_SOFTWARE = '1'
    $env:LP_NUM_THREADS = '1'
    $env:MESA_SHADER_CACHE_DISABLE = 'true'
    Write-Host (
        "[mesa-wgl-runtime] rid=$Rid version=$($lock.package.version) " +
        "license=$($lock.package.license)")
    Write-Host "[mesa-wgl-runtime] PATH prepended with $stageRoot"
}

$preflightReportPath = Join-Path $rootPath 'mesa-wgl-preflight.json'
if ($Preflight)
{
    Invoke-WglPreflight `
        -StagePath $rootPath `
        -ExpectedOpenGlPath $openglPath `
        -ExpectedOpenGlSha256 $openglSha256 `
        -ReportPath $preflightReportPath
}

[ordered]@{
    schemaVersion = 1
    rid = $Rid
    package = $lock.package
    stageRoot = $stageRoot
    files = @($runtime.files | ForEach-Object {
        [ordered]@{
            path = Join-Path $stageRoot ([string]$_.path)
            sha256 = [string]$_.sha256
        }
    })
    preflight = if (Test-Path -LiteralPath $preflightReportPath -PathType Leaf)
    {
        Get-Content -LiteralPath $preflightReportPath -Raw | ConvertFrom-Json
    }
    else
    {
        $null
    }
} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $rootPath 'mesa-wgl-runtime.json')

Write-Output $stageRoot
