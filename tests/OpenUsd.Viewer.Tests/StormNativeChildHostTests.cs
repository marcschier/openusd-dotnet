// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Viewer.Tests;

public sealed class StormNativeChildHostTests
{
    [Test]
    public async Task WindowsShellDefaultsToAngleAndKeepsLegacyWglSoak()
    {
        await Assert.That(Program.GetWindowsRenderingModes(false, null))
            .IsEquivalentTo([Win32RenderingMode.AngleEgl]);
        await Assert.That(Program.GetWindowsRenderingModes(false, "windows-wgl"))
            .IsEquivalentTo([Win32RenderingMode.Wgl]);
        await Assert.That(Program.GetWindowsRenderingModes(true, null))
            .IsEquivalentTo([Win32RenderingMode.Wgl]);
    }

    [Test]
    public async Task NativeChildHostUsesAvaloniaNativeControlOwnership()
    {
        await Assert.That(typeof(StormNativeControlHost).IsSubclassOf(typeof(NativeControlHost)))
            .IsTrue();
        await Assert.That(
            typeof(OpenUsdStormChildSession).GetMethod(
                "Finalize",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.DeclaredOnly))
            .IsNull();
        await Assert.That(
            typeof(OpenUsdStormChildSession).GetField(
                "_gate",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic))
            .IsNotNull();
    }

    [Test]
    public async Task MacOSShellIsFixedToMetalAndStormUsesAnNSViewChild()
    {
        await Assert.That(Program.GetMacOSRenderingModes())
            .IsEquivalentTo([AvaloniaNativeRenderingMode.Metal]);
        await Assert.That(StormNativeControlHostMacOS.HandleDescriptor)
            .IsEqualTo("NSView");
    }

    [Test]
    public async Task SwitchingSequenceUsesStormAndMetalOnMacOS()
    {
        RenderBackendKind[] sequence = MainWindow.GetSwitchSoakSequence();
        if (OperatingSystem.IsMacOS())
        {
            await Assert.That(sequence)
                .IsEquivalentTo([RenderBackendKind.Metal, RenderBackendKind.Storm]);
        }
        else
        {
            await Assert.That(sequence).Contains(RenderBackendKind.Storm);
        }
    }

    [Test]
    public async Task SwitchingViewportTogglesOneControlWithoutRebuildingApplication()
    {
        var viewport = new RendererSwitchingViewport();
        var storm = new Border();
        var composition = new Border();

        viewport.Attach(storm);
        viewport.Detach(storm);
        viewport.Attach(composition);

        await Assert.That(storm.IsVisible).IsFalse();
        await Assert.That(composition.IsVisible).IsTrue();
        await Assert.That(viewport.Children.Count).IsEqualTo(1);
        await Assert.That(viewport.Children[0]).IsSameReferenceAs(composition);
    }

    [Test]
    public async Task SwitchingViewportKeepsCandidateHiddenUntilOldHostIsDeactivated()
    {
        var viewport = new RendererSwitchingViewport();
        var oldHost = new Border();
        var candidate = new Border();
        viewport.Attach(oldHost);
        viewport.Attach(candidate, isActive: false);

        await Assert.That(viewport.VisibleControlCount).IsEqualTo(1);
        await Assert.That(viewport.AttachedControlCount).IsEqualTo(2);

        viewport.SetActive(oldHost, isActive: false);
        viewport.SetActive(candidate, isActive: true);

        await Assert.That(viewport.VisibleControlCount).IsEqualTo(1);
        await Assert.That(oldHost.IsVisible).IsFalse();
        await Assert.That(candidate.IsVisible).IsTrue();

        viewport.Detach(oldHost);

        await Assert.That(viewport.VisibleControlCount).IsEqualTo(1);
        await Assert.That(viewport.AttachedControlCount).IsEqualTo(1);
    }

    [Test]
    public async Task NativeChildSizingUsesPhysicalPixelsAtDpiTransitions()
    {
        await Assert.That(ViewportPixelMath.ToPixels(200, 100, 1.5))
            .IsEqualTo(new ViewportDimensions(300, 150));
        await Assert.That(ViewportPixelMath.ToPixels(200, 100, 2))
            .IsEqualTo(new ViewportDimensions(400, 200));
    }

    [Test]
    public async Task MacOSNativeSourcePreservesCocoaAndCompletedFrameContracts()
    {
        string root = FindRepositoryRoot();
        string source = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_storm_child",
            "src",
            "openusd_storm_child_macos.mm"));
        string probe = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_storm_child",
            "tests",
            "storm_child_probe_macos.mm"));
        string input = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_storm_child",
            "src",
            "openusd_storm_child_macos_input.h"));

        await Assert.That(source).Contains("PerformMainThreadContextOperation");
        await Assert.That(source).Contains("[context setView:operation->view]");
        await Assert.That(source).Contains("InitializeContextOnRenderThread");
        await Assert.That(source).Contains("openusd_storm_child_macos_inject_view_diagnostic_input");
        await Assert.That(source).Contains("openusd_storm_child_get_navigation_input");
        await Assert.That(source).Contains("otherMouseDragged:");
        await Assert.That(source).Contains("flagsChanged:");
        await Assert.That(source).Contains("OpenUsdStormChildNormalizeMacScrollDelta");
        await Assert.That(source).Contains("hasPreciseScrollingDeltas");
        await Assert.That(source).Contains("isDirectionInvertedFromDevice");
        await Assert.That(source).Contains("[event isARepeat]");
        await Assert.That(input).Contains("OpenUsdStormChildMacScrollPointsPerStep = 40.0");
        await Assert.That(input).Contains("OpenUsdStormChildMacMaximumScrollStepsPerEvent = 4.0");
        await Assert.That(input).Contains("std::copysign(1.0, directed)");
        await Assert.That(source).Contains("ExpectedRendererName = \"Storm / Metal\"");
        await Assert.That(source).Contains(" + OpenGL 4.1 core presentation");
        await Assert.That(source).DoesNotContain("Hgi Metal");
        string resize = source[source.IndexOf(
            "openusd_status openusd_storm_child_resize",
            StringComparison.Ordinal)..source.IndexOf(
            "openusd_status openusd_storm_child_set_visible",
            StringComparison.Ordinal)];
        int resizeLock = resize.IndexOf(
            "std::lock_guard context_lock(state->context_gate)",
            StringComparison.Ordinal);
        int setFrame = resize.IndexOf("[state->view setFrameSize:", StringComparison.Ordinal);
        int publishWidth = resize.IndexOf("state->width.store", StringComparison.Ordinal);
        int publishUpdate = resize.IndexOf(
            "state->context_update_required = true",
            StringComparison.Ordinal);
        await Assert.That(resizeLock).IsGreaterThanOrEqualTo(0);
        await Assert.That(setFrame).IsGreaterThan(resizeLock);
        await Assert.That(publishWidth).IsGreaterThan(setFrame);
        await Assert.That(publishUpdate).IsGreaterThan(publishWidth);
        await Assert.That(source)
            .Contains("openusd_storm_child_macos_get_resize_diagnostics");
        string recovery = source[source.IndexOf(
            "openusd_status RecreateAfterContextLoss",
            StringComparison.Ordinal)..source.IndexOf(
            "openusd_status TeardownRendererAndContext",
            StringComparison.Ordinal)];
        int stageRecovery = recovery.IndexOf(
            "StageContextOperationLocked(child, true)",
            StringComparison.Ordinal);
        int pauseRecovery = recovery.IndexOf(
            "PauseRecoveryAfterStagingForTest(child)",
            StringComparison.Ordinal);
        int dispatchRecovery = recovery.IndexOf(
            "RunMainThreadContextOperation(&replacement)",
            StringComparison.Ordinal);
        int publishLock = recovery.IndexOf(
            "std::lock_guard context_lock(child->context_gate)",
            dispatchRecovery,
            StringComparison.Ordinal);
        int publishRecovery = recovery.IndexOf(
            "PublishCreatedContextLocked(child, &replacement)",
            StringComparison.Ordinal);
        int initializeRecovery = recovery.IndexOf(
            "InitializePublishedContextAndRendererLocked(child, error)",
            StringComparison.Ordinal);
        await Assert.That(stageRecovery).IsGreaterThanOrEqualTo(0);
        await Assert.That(pauseRecovery).IsGreaterThan(stageRecovery);
        await Assert.That(dispatchRecovery).IsGreaterThan(pauseRecovery);
        await Assert.That(publishLock).IsGreaterThan(dispatchRecovery);
        await Assert.That(publishRecovery).IsGreaterThan(publishLock);
        await Assert.That(initializeRecovery).IsGreaterThan(publishRecovery);
        await Assert.That(source)
            .Contains("openusd_storm_child_macos_enable_recovery_barrier");
        await Assert.That(source)
            .Contains("openusd_storm_child_macos_wait_recovery_staged");
        await Assert.That(source)
            .Contains("openusd_storm_child_macos_release_recovery_barrier");
        await Assert.That(probe).Contains("ThreadBarrier resize_barrier(2)");
        await Assert.That(probe).Contains("iteration < 64");
        await Assert.That(probe)
            .Contains("context_update_generation !=");
        await Assert.That(probe)
            .Contains("rendered_resize_generation !=");
        await Assert.That(probe).Contains("PumpMainRunLoopUntil");
        await Assert.That(probe).Contains("navigationCommands=");
        await Assert.That(probe).Contains("VerifyMacScrollNormalization");
        await Assert.That(probe).Contains("20.0, true, true");
        await Assert.That(probe)
            .Contains("first_recovery_frame_context_generation ==");
        int read = source.IndexOf("glReadPixels(", StringComparison.Ordinal);
        int flush = source.IndexOf("[child->context flushBuffer]", StringComparison.Ordinal);
        await Assert.That(read).IsGreaterThanOrEqualTo(0);
        await Assert.That(flush).IsGreaterThan(read);
        string capture = source[source.IndexOf(
            "openusd_status CaptureFramebuffer",
            StringComparison.Ordinal)..source.IndexOf(
            "openusd_status RecreateAfterContextLoss",
            StringComparison.Ordinal)];
        await Assert.That(capture).DoesNotContain("glReadPixels");
        await Assert.That(capture)
            .Contains("OPENUSD_STORM_CHILD_CAPTURE_READ_PRESERVED_TEXTURE");
    }

    [Test]
    public async Task MacOSRunnerAndWorkflowCarryTheHardGates()
    {
        string root = FindRepositoryRoot();
        string runner = await File.ReadAllTextAsync(
            Path.Combine(root, "eng", "run-storm-native-child-macos.ps1"));
        string silkRunner = await File.ReadAllTextAsync(
            Path.Combine(root, "eng", "run-silk-probe.ps1"));
        string workflow = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "render.yml"));

        await Assert.That(runner).Contains("[ValidateSet('build', 'archive')]");
        await Assert.That(runner).Contains("bin/openusd_storm_child_probe");
        await Assert.That(runner).Contains("compositionDraws=[1-9]\\d*");
        await Assert.That(runner).Contains("VIEWER_METAL_HDSILK_READY");
        await Assert.That(runner).Contains("triangles=[1-9]\\d*");
        await Assert.That(runner)
            .Contains("rendererName=Storm / Metal \\+ OpenGL 4\\.1 core presentation");
        await Assert.That(runner)
            .Contains("Storm native child initialized on Storm / Metal");
        await Assert.That(runner).Contains("resizeGeneration=[1-9]\\d*");
        await Assert.That(runner).Contains("contextUpdateGeneration=[1-9]\\d*");
        await Assert.That(runner).Contains("renderedResizeGeneration=[1-9]\\d*");
        await Assert.That(runner).Contains("recoveryGeneration=[1-9]\\d*");
        await Assert.That(runner).Contains("rendererContextGeneration=[1-9]\\d*");
        await Assert.That(runner)
            .Contains("firstRecoveryFrameContextGeneration=[1-9]\\d*");
        await Assert.That(runner).Contains("navigationSequence=[1-9]\\d*");
        await Assert.That(runner).Contains("navigationCommands=2/2/4");
        await Assert.That(runner).Contains("scrollPointsPerStep=40");
        await Assert.That(runner).Contains("gpuScenes=0; gpuMeshes=0");
        await Assert.That(runner).Contains("STAGE_DRAW_BLOCKED");
        await Assert.That(silkRunner).Contains("elseif ($MetalComposition)");
        await Assert.That(silkRunner)
            .Contains("& $executable --metal-composition $pluginPath $stagedStage $artifactPath");
        await Assert.That(workflow).Contains("runs-on: macos-15");
        await Assert.That(workflow).DoesNotContain("macos-15-intel");
        await Assert.That(workflow).Contains("-MetalComposition");
        await Assert.That(workflow)
            .Contains("MetalCompositionPresentationTests/*");
        await Assert.That(workflow).Contains("-NativeSource $env:NATIVE_SOURCE");
        await Assert.That(workflow).Contains("artifacts/metal-composition-probe/osx-arm64");
        await Assert.That(workflow).Contains("artifacts/package-macos-storm-child");
        await Assert.That(workflow).Contains("artifacts/storm-native-child-macos");
    }

    [Test]
    public async Task LinuxStormChildCMakePinsAbi7SonameTopology()
    {
        string root = FindRepositoryRoot();
        string targetCmake = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_storm_child",
            "CMakeLists.txt"));
        string probeCmake = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_storm_child",
            "tests",
            "CMakeLists.txt"));
        string probeSource = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_storm_child",
            "tests",
            "storm_child_probe_linux.cpp"));

        await Assert.That(targetCmake).Contains("VERSION 7.0.0");
        await Assert.That(targetCmake).Contains("SOVERSION 7");
        await Assert.That(probeCmake)
            .Contains("$<TARGET_SONAME_FILE_NAME:openusd_storm_child>");
        await Assert.That(probeCmake)
            .Contains("$<TARGET_LINKER_FILE_NAME:openusd_storm_child>");
        await Assert.That(probeSource).Contains("libopenusd_storm_child.so.7.0.0");
        await Assert.That(probeSource).Contains("libopenusd_storm_child.so.7");
        await Assert.That(probeSource).Contains("libopenusd_storm_child.so");
    }

    [Test]
    public async Task LinuxInitializationCallsNativeDispatcherBeforeAbiValidation()
    {
        var state = new LinuxX11Threading.InitializationState();
        var calls = new List<string>();

        LinuxX11Threading.InitializeCore(
            state,
            () =>
            {
                calls.Add("XInitThreads");
                return 1;
            },
            () => calls.Add("InitializeLinux"),
            () =>
            {
                calls.Add("GetAbiVersion");
                return 7;
            });

        await Assert.That(calls.Count).IsEqualTo(3);
        await Assert.That(calls[0]).IsEqualTo("XInitThreads");
        await Assert.That(calls[1]).IsEqualTo("InitializeLinux");
        await Assert.That(calls[2]).IsEqualTo("GetAbiVersion");
        await Assert.That(state.IsInitialized).IsTrue();
    }

    [Test]
    public async Task LinuxX11InitializationFailuresLeaveStateUnpublished()
    {
        var xInitFailureState = new LinuxX11Threading.InitializationState();
        int stormCalls = 0;

        await Assert.That(() => LinuxX11Threading.InitializeCore(
            xInitFailureState,
            () => 0,
            () => stormCalls++))
            .Throws<InvalidOperationException>();
        await Assert.That(xInitFailureState.IsInitialized).IsFalse();
        await Assert.That(stormCalls).IsEqualTo(0);

        var stormFailureState = new LinuxX11Threading.InitializationState();
        await Assert.That(() => LinuxX11Threading.InitializeCore(
            stormFailureState,
            () => 1,
            () => throw new NotSupportedException("Fake Storm initialization failure.")))
            .Throws<NotSupportedException>();
        await Assert.That(stormFailureState.IsInitialized).IsFalse();
    }

    [Test]
    public async Task LinuxX11InitializationStateIsExposedBeforePlatformStartup()
    {
        string root = FindRepositoryRoot();
        string programSource = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "Program.cs"));
        int platformInitialization = programSource.IndexOf(
            "LinuxX11Threading.Initialize();",
            StringComparison.Ordinal);
        int appBuilder = programSource.IndexOf(
            "BuildAvaloniaApp(decision)",
            StringComparison.Ordinal);
        await Assert.That(platformInitialization).IsGreaterThanOrEqualTo(0);
        await Assert.That(appBuilder).IsGreaterThan(platformInitialization);

        string nativeSource = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_storm_child",
            "src",
            "openusd_storm_child_linux.cpp"));
        await Assert.That(
            nativeSource.Split(
                "XSetErrorHandler(DispatchXError)",
                StringSplitOptions.None).Length - 1).IsEqualTo(1);
        await Assert.That(nativeSource).DoesNotContain("XSetErrorHandler(_previous)");
        await Assert.That(nativeSource).Contains("XErrorState g_x_error_state;");
        await Assert.That(nativeSource).Contains("state.active.store(false");
        await Assert.That(nativeSource)
            .Contains("event.xany.send_event != False");
        await Assert.That(nativeSource).Contains("EnterWindowMask | LeaveWindowMask");
        await Assert.That(nativeSource).Contains("XLookupKeysym");
        await Assert.That(nativeSource).Contains("XkbSetDetectableAutoRepeat");
        await Assert.That(nativeSource).Contains("IsLegacyAutoRepeatRelease");
        await Assert.That(nativeSource)
            .Contains("openusd_storm_child_get_navigation_input");
        string initializeLinux = nativeSource[nativeSource.IndexOf(
            "extern \"C\" openusd_status openusd_storm_child_initialize_linux",
            StringComparison.Ordinal)..nativeSource.IndexOf(
            "extern \"C\" openusd_status openusd_storm_child_create",
            StringComparison.Ordinal)];
        int installDispatcher = initializeLinux.IndexOf(
            "XSetErrorHandler(DispatchXError)",
            StringComparison.Ordinal);
        int preserveDispatcher = initializeLinux.IndexOf(
            "previous != DispatchXError",
            StringComparison.Ordinal);
        int initializedReturn = initializeLinux.IndexOf(
            "initialization == XDispatcherInitialization::Initialized",
            StringComparison.Ordinal);
        await Assert.That(installDispatcher).IsGreaterThanOrEqualTo(0);
        await Assert.That(preserveDispatcher).IsGreaterThan(installDispatcher);
        await Assert.That(initializedReturn).IsGreaterThan(preserveDispatcher);
        int create = nativeSource.IndexOf(
            "extern \"C\" openusd_status openusd_storm_child_create",
            StringComparison.Ordinal);
        int initializationGuard = nativeSource.IndexOf(
            "g_x_dispatcher_initialization.load",
            create,
            StringComparison.Ordinal);
        int argumentValidation = nativeSource.IndexOf(
            "const Window parent",
            create,
            StringComparison.Ordinal);
        await Assert.That(initializationGuard).IsGreaterThan(create);
        await Assert.That(argumentValidation).IsGreaterThan(initializationGuard);
        await Assert.That(nativeSource)
            .Contains("The Linux X11 error dispatcher is not initialized.");

        string appSource = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "App.axaml.cs"));
        int rebind = appSource.IndexOf(
            "LinuxX11Threading.RebindAfterPlatformSetup();",
            StringComparison.Ordinal);
        int windowCreation = appSource.IndexOf("new MainWindow()", StringComparison.Ordinal);
        await Assert.That(rebind).IsGreaterThanOrEqualTo(0);
        await Assert.That(windowCreation).IsGreaterThan(rebind);

        string viewportSource = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "RendererSwitchingViewport.cs"));
        await Assert.That(viewportSource).DoesNotContain("XSendEvent");
        await Assert.That(viewportSource).Contains("XTestQueryExtension");
        await Assert.That(viewportSource).Contains("XTestFakeMotionEvent");
        await Assert.That(viewportSource).Contains("XTestFakeButtonEvent");
        await Assert.That(viewportSource).Contains("XTestFakeKeyEvent");

        string probeSource = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_storm_child",
            "tests",
            "storm_child_probe_linux.cpp"));
        await Assert.That(probeSource).DoesNotContain("XSendEvent");
        await Assert.That(probeSource).Contains("XTestQueryExtension");
        await Assert.That(probeSource).Contains("SendDragStart");
        await Assert.That(probeSource).Contains("SendCommandWithRepeat");
        await Assert.That(probeSource).Contains("frame_selected_press_count == 2");

        string workflow = await File.ReadAllTextAsync(Path.Combine(
            root,
            ".github",
            "workflows",
            "render.yml"));
        await Assert.That(workflow).Contains("libxtst-dev");
        await Assert.That(workflow).Contains("libxtst6");

        string linuxRunner = await File.ReadAllTextAsync(Path.Combine(
            root,
            "eng",
            "run-storm-native-child-linux.sh"));
        await Assert.That(linuxRunner).Contains("bin/openusd_storm_child_probe");
        await Assert.That(linuxRunner)
            .Contains("\"$repo_root/native/install/shim/linux-x64/lib\" 2>&1 | tee \"$probe_log\"");

        var state = new LinuxX11Threading.InitializationState();
        var calls = new List<string>();
        var initializedDuringCallbacks = new List<bool>();
        await Assert.That(state.IsInitialized).IsFalse();

        LinuxX11Threading.InitializeCore(
            state,
            () =>
            {
                calls.Add("XInitThreads");
                initializedDuringCallbacks.Add(state.IsInitialized);
                return 1;
            },
            () =>
            {
                calls.Add("InitializeStorm");
                initializedDuringCallbacks.Add(state.IsInitialized);
            });
        LinuxX11Threading.InitializeCore(
            state,
            () =>
            {
                calls.Add("RepeatedXInitThreads");
                return 1;
            },
            () => calls.Add("RepeatedInitializeStorm"));

        await Assert.That(calls.Count).IsEqualTo(2);
        await Assert.That(calls[0]).IsEqualTo("XInitThreads");
        await Assert.That(calls[1]).IsEqualTo("InitializeStorm");
        await Assert.That(initializedDuringCallbacks.Count).IsEqualTo(2);
        await Assert.That(initializedDuringCallbacks[0]).IsFalse();
        await Assert.That(initializedDuringCallbacks[1]).IsFalse();
        await Assert.That(state.IsInitialized).IsTrue();
    }

    [Test]
    public async Task NativeNavigationAbiIsPointerFreeAndImplementedOnEveryPlatform()
    {
        string root = FindRepositoryRoot();
        string header = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_storm_child",
            "include",
            "openusd_storm_child.h"));
        string windows = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_storm_child",
            "src",
            "openusd_storm_child.cpp"));
        string linux = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_storm_child",
            "src",
            "openusd_storm_child_linux.cpp"));
        string macOS = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_storm_child",
            "src",
            "openusd_storm_child_macos.mm"));

        await Assert.That(header).Contains("OPENUSD_STORM_CHILD_ABI_VERSION 7u");
        await Assert.That(header).Contains("openusd_storm_child_navigation_input");
        await Assert.That(header).Contains("static_assert(sizeof(");
        string snapshot = header[header.IndexOf(
            "typedef struct openusd_storm_child_navigation_input",
            StringComparison.Ordinal)..header.IndexOf(
            "OPENUSD_STORM_CHILD_API uint32_t",
            StringComparison.Ordinal)];
        await Assert.That(snapshot).DoesNotContain("*");
        foreach (string source in new[] { windows, linux, macOS })
        {
            await Assert.That(source)
                .Contains("openusd_storm_child_get_navigation_input");
            await Assert.That(source)
                .Contains("OpenUsdStormChildCopyNavigationInput");
        }
        await Assert.That(windows).Contains("WM_MBUTTONDOWN");
        await Assert.That(windows).Contains("WM_MOUSELEAVE");
        await Assert.That(windows).Contains("uintptr_t{1} << 30");
        string navigation = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_storm_child",
            "src",
            "openusd_storm_child_navigation.h"));
        await Assert.That(navigation).Contains("command_keys_down");
        await Assert.That(macOS).Contains("NSTrackingMouseEnteredAndExited");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the OpenUSD repository root.");
    }
}
