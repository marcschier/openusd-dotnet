// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;

namespace OpenUsd.Viewer;

internal sealed partial class PropertyEditWindow
{
    private readonly ViewerRelationshipSnapshot? _binding;
    private ViewerAuthoredEditCapture? _bindingCapture;

    private void ConfigureMaterialBinding()
    {
        Title = "Edit direct material binding";
        PropertySelector.ItemsSource = new[] { _binding!.Name };
        PropertySelector.SelectedIndex = 0;
        PropertySelector.IsVisible = false;
        PropertyFieldSelector.IsVisible = false;
        BlockPropertyButton.IsVisible = false;
        PropertyValue.PlaceholderText = "Absolute Material prim path; empty explicitly unbinds";
        PropertyInputHint.Text = "Set replaces only the direct binding target with one native Material prim. " +
            "Empty Set explicitly unbinds; Clear removes only the review targets and reveals weaker bindings. " +
            "Binding strength, collections and shader networks are not edited.";
    }

    private async Task CaptureMaterialBindingAsync()
    {
        ViewerRelationshipSnapshot binding = _binding ??
            throw new InvalidOperationException("Choose an existing direct material binding.");
        _bindingCapture = await _editor.CaptureMaterialBindingAsync(_primPath, binding.Name, _lifetime.Token);
        ShowMaterialBindingCapture();
        PropertyEditStatus.Text =
            "Only direct review targets are edited. Source, binding strength and shader networks remain unchanged. " +
            "Close to use shared document Undo/Redo.";
    }

    private void ShowMaterialBindingCapture()
    {
        ViewerAuthoredEditCapture capture = _bindingCapture ??
            throw new InvalidOperationException("Refresh the exact review target before editing.");
        UsdLayerEditValue value = capture.Snapshot.Opinions[0].Value;
        string state;
        if (value.Kind == UsdLayerEditValueKind.Absent)
        {
            state = "Absent (no authored review target list)";
            PropertyValue.Text = string.Empty;
        }
        else
        {
            UsdPathListEdit targets = value.AsPathList();
            state = $"explicit={targets.IsExplicit}; " +
                $"items=[{string.Join(", ", targets.ExplicitItems)}]; " +
                $"add=[{string.Join(", ", targets.AddedItems)}]; " +
                $"prepend=[{string.Join(", ", targets.PrependedItems)}]; " +
                $"append=[{string.Join(", ", targets.AppendedItems)}]; " +
                $"delete=[{string.Join(", ", targets.DeletedItems)}]; " +
                $"order=[{string.Join(", ", targets.OrderedItems)}]";
            PropertyValue.Text = targets.IsExplicit && targets.ExplicitItems.Count == 1
                ? targets.ExplicitItems[0] : string.Empty;
        }
        PropertyComposedValue.Text = $"Composed display snapshot (read-only): {_binding!.Targets}";
        PropertyTargetState.Text = $"Review target: {capture.LayerIdentifier}\n" +
            $"Revision {capture.Snapshot.Revision}; targets: {state}";
    }

    private async Task ApplyMaterialBindingAsync(UsdLayerEditOperation operation)
    {
        ViewerAuthoredEditCapture capture = _bindingCapture ??
            throw new InvalidOperationException("Refresh the exact review target before editing.");
        UsdLayerEditAddress address = capture.Snapshot.Addresses[0];
        UsdLayerEdit edit;
        if (operation == UsdLayerEditOperation.Set)
        {
            string path = (PropertyValue.Text ?? string.Empty).Trim();
            var targets = new UsdPathListEdit(true, explicitItems: path.Length == 0 ? [] : new[] { path });
            edit = UsdLayerEdit.Set(address, UsdLayerEditValue.FromPathList(targets));
        }
        else if (operation == UsdLayerEditOperation.Clear)
        {
            edit = UsdLayerEdit.Clear(address);
        }
        else
        {
            throw new NotSupportedException("Binding targets support Set or Clear, not value blocks.");
        }
        ViewerAuthoredEditResult result = await _editor.ApplyAsync(capture, [edit],
            $"Material binding {_primPath}", gestureId: Guid.NewGuid(), cancellationToken: _lifetime.Token);
        PropertyEditStatus.Text = result.Message;
        if (result.Outcome == UsdLayerEditOutcome.Applied)
        {
            _bindingCapture = capture with { Snapshot = result.After! };
            ShowMaterialBindingCapture();
        }
        else
        {
            _bindingCapture = null;
            PropertyEditStatus.Text += " Refresh before retrying; no conflicting opinion was overwritten.";
        }
    }
}
