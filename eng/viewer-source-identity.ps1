# Copyright (c) marcschier. Licensed under the MIT License.

function Test-ViewerBuildOutputPath
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $Path -match '[/\\](bin|obj)[/\\]'
}

function Get-ViewerEvidenceContractInputPaths
{
    @(
        'Directory.Build.props',
        'Directory.Packages.props',
        'global.json',
        'src/OpenUsd.Viewer/OpenUsd.Viewer.csproj',
        'src/OpenUsd.Viewer/AvaloniaViewerRenderBackendHost.cs',
        'src/OpenUsd.Viewer/MainWindow.axaml.cs',
        'src/OpenUsd.Viewer/RendererSwitchingViewport.cs',
        'src/OpenUsd.Viewer/StormNativeControlHost.cs',
        'src/OpenUsd.Viewer/StormViewportControl.cs',
        'src/OpenUsd.Viewer/ViewerCameraEvidence.cs',
        'src/OpenUsd.Viewer/ViewerCameraNavigationUi.cs',
        'src/OpenUsd.Viewer/ViewerFrameAdapters.cs',
        'src/OpenUsd.Viewer/ViewerSwitchingEvidence.cs',
        'src/OpenUsd.Rendering.Silk/SilkSelectionOutline.cs',
        'src/OpenUsd.Rendering.Silk/SilkMeshRenderer.cs',
        'src/OpenUsd.Rendering.Silk.D3D12/D3D12SilkGraphicsDevice.SelectionOutline.cs',
        'src/OpenUsd.Rendering.Silk.D3D12/D3D12CompositionViewportPresenter.cs',
        'src/OpenUsd.Rendering.Silk.Vulkan/VulkanSilkGraphicsDevice.SelectionOutline.cs',
        'src/OpenUsd.Rendering.Silk.Vulkan/VulkanCompositionViewportPresenter.cs',
        'src/OpenUsd.Rendering.Silk.Metal/MetalSilkGraphicsDevice.SelectionOutline.cs',
        'src/OpenUsd.Rendering.Silk.Metal/MetalCompositionViewportPresenter.cs',
        'src/OpenUsd.Rendering.Storm/OpenUsdStormChildRuntime.cs',
        'tests/OpenUsd.Rendering.ConformanceTests/D3D12SelectionOutlineTests.cs',
        'tests/OpenUsd.Rendering.ConformanceTests/VulkanSelectionOutlineTests.cs',
        'tests/OpenUsd.Rendering.ConformanceTests/MetalSelectionOutlineConformanceTests.cs',
        'tests/OpenUsd.Rendering.Tests/StormNavigationInputTests.cs',
        'tests/OpenUsd.Viewer.Tests/StormNativeChildHostTests.cs',
        'tests/OpenUsd.Viewer.Tests/ViewerCameraNavigationUiTests.cs',
        'tests/OpenUsd.Viewer.Tests/ViewerCameraPropagationTests.cs',
        'tests/OpenUsd.Viewer.Tests/ViewerSourceContractTests.cs',
        'tests/OpenUsd.Viewer.Tests/ViewerSwitchingEvidenceTests.cs',
        'test-assets/viewer-stage-camera-smoke.usda',
        'eng/shaders/sources/selection.mask.vertex.slang',
        'eng/shaders/sources/selection.mask.fragment.slang',
        'eng/shaders/sources/selection.outline.vertex.slang',
        'eng/shaders/sources/selection.outline.fragment.slang',
        'eng/shaders/shader-manifest.json',
        'docs/rendering.md',
        'docs/testing.md',
        'docs/viewer.md',
        'eng/shared-stage-soak-identity.ps1',
        'eng/viewer-evidence-contract.ps1',
        'eng/test-viewer-evidence-contract.ps1',
        'eng/viewer-source-identity.ps1',
        'eng/test-viewer-source-identity.ps1',
        'eng/run-viewer.ps1',
        'eng/run-viewer-stage-camera-smoke.ps1',
        'eng/run-platform-smoke.ps1',
        'eng/run-storm-native-child.ps1',
        'eng/run-storm-native-child-linux.sh',
        'eng/storm-native-child-linux-lib.sh',
        'eng/test-storm-native-child-linux.sh',
        'eng/run-storm-native-child-macos.ps1',
        '.github/workflows/render.yml'
    )
}

function Get-ViewerSourceIdentity
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    $root = [System.IO.Path]::GetFullPath($RepoRoot)
    $roots = @(
        'src/OpenUsd.Viewer',
        'src/OpenUsd',
        'src/OpenUsd.Interop',
        'src/OpenUsd.Rendering',
        'src/OpenUsd.Rendering.Storm',
        'src/OpenUsd.Rendering.Silk',
        'src/OpenUsd.Rendering.Silk.D3D12',
        'src/OpenUsd.Rendering.Silk.Vulkan',
        'src/OpenUsd.Rendering.Silk.Metal',
        'eng/shaders/checked',
        'tests/OpenUsd.Viewer.Tests',
        'test-assets/minimal.usda',
        'native/CMakeLists.txt',
        'native/CMakePresets.json',
        'native/hdSilk',
        'native/openusd_dotnet',
        'native/openusd_hydra',
        'native/openusd_storm_child',
        'native/private') + @(Get-ViewerEvidenceContractInputPaths)
    $files = foreach ($relative in $roots)
    {
        $path = Join-Path $root $relative
        if (Test-Path $path -PathType Leaf)
        {
            Get-Item $path
        }
        elseif (Test-Path $path)
        {
            Get-ChildItem $path -File -Recurse |
                Where-Object {
                    -not (Test-ViewerBuildOutputPath -Path $_.FullName)
                }
        }
    }
    $entries = @($files |
        Sort-Object FullName -Unique |
        ForEach-Object {
            [ordered]@{
                path = [System.IO.Path]::GetRelativePath($root, $_.FullName)
                sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
                length = $_.Length
            }
        })
    $payload = $entries | ConvertTo-Json -Depth 4 -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
    [ordered]@{
        sha256 = [Convert]::ToHexString(
            [System.Security.Cryptography.SHA256]::HashData($bytes))
        files = $entries
    }
}
