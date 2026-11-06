// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Interactivity;
using OpenUsd.Interop;

namespace OpenUsd.Viewer;

public sealed partial class MainWindow
{
    private ViewerPrimInspectorSnapshot? _shaderRegistryOwner;
    private ViewerShaderInspection? _shaderInspection;
    private bool _shaderRegistryReadBusy;
    private int _shaderRegistryRequest;

    private void PrepareShaderInspection(ViewerPrimInspectorSnapshot? inspector)
    {
        if (ReferenceEquals(_shaderRegistryOwner, inspector))
        {
            return;
        }
        _shaderRegistryOwner = inspector;
        _shaderInspection = null;
        _shaderRegistryReadBusy = false;
        _shaderRegistryRequest++;
    }

    private void UpdateShaderInspectionControls()
    {
        bool shader = _currentInspector?.TypeName == "Shader";
        InspectorShaderRegistryRead.IsVisible = shader;
        InspectorShaderRegistryState.IsVisible = shader;
        InspectorShaderRegistryRead.IsEnabled = shader && CanReadInspectorSnapshot() &&
            !_documentSnapshotRefreshBusy && !_shaderRegistryReadBusy;
        InspectorShaderRegistryState.Text = _shaderRegistryReadBusy
            ? "Reading bounded Sdr registry snapshot..."
            : _shaderInspection?.Summary ?? "Read the Sdr registry explicitly; no source assets are resolved.";
    }

    private async void OnInspectorShaderRegistryRead(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (!InspectorShaderRegistryRead.IsEnabled || _currentInspector is not { TypeName: "Shader" } inspector ||
            _coordinator is not { } coordinator || _documentLifetime is not { } lifetime)
        {
            return;
        }
        int request = ++_shaderRegistryRequest;
        _shaderRegistryReadBusy = true;
        ViewerStartupOptions.WriteStatus($"Shader registry inspection: requested for {inspector.Path}");
        UpdateShaderInspectionControls();
        try
        {
            ViewerShaderInspection inspection = await coordinator.Scheduler.InvokeAsync(stage =>
            {
                UsdPrim prim = stage.GetPrim(inspector.Path);
                double? timeCode = inspector.PropertySnapshot?.TimeCode;
                string implementationSource = timeCode is { } sourceTime
                    ? prim.GetToken("info:implementationSource", sourceTime)
                    : prim.GetToken("info:implementationSource");
                ViewerAttributeSnapshot? identifierAttribute = inspector.Attributes.FirstOrDefault(
                    static attribute => attribute.Name == "info:id" && attribute.TypeName == "token");
                UsdPropertyValueState identifierState = identifierAttribute is null
                    ? UsdPropertyValueState.Unset
                    : identifierAttribute.Inspection?.ValueState ?? UsdPropertyValueState.Deferred;
                string identifier = implementationSource != "id" || identifierState != UsdPropertyValueState.Value
                    ? string.Empty
                    : timeCode is { } identifierTime
                        ? prim.GetToken("info:id", identifierTime)
                        : prim.GetToken("info:id");
                return ViewerShaderInspection.Read(
                    implementationSource, identifier, UsdShaderRegistry.GetNodeDefinitionsSnapshot, identifierState);
            }, lifetime.Token);
            if (request != _shaderRegistryRequest || !ReferenceEquals(inspector, _currentInspector) ||
                lifetime.IsCancellationRequested)
            {
                ViewerStartupOptions.WriteStatus(
                    $"Shader registry inspection: discarded after snapshot change for {inspector.Path}");
                return;
            }
            _shaderInspection = inspection;
            ViewerStartupOptions.WriteStatus($"Shader registry inspection: {inspection.Summary}");
            ShowInspector(inspector);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is OpenUsdNativeException or InvalidDataException or
            InvalidOperationException or NotSupportedException or ArgumentException)
        {
            if (request == _shaderRegistryRequest && ReferenceEquals(inspector, _currentInspector))
            {
                _shaderInspection = new ViewerShaderInspection(
                    $"Sdr lookup failed: {ViewerScalarFormatter.Bound(exception.Message, 512)}", [], false);
                ShowInspector(inspector);
                ShowError($"Shader registry inspection failed: {ViewerPackageErrorFormatter.Format(exception)}");
            }
        }
        finally
        {
            if (request == _shaderRegistryRequest)
            {
                _shaderRegistryReadBusy = false;
                UpdateShaderInspectionControls();
            }
        }
    }
}
