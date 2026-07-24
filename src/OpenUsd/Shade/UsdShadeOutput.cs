// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace OpenUsd.Shade;

/// <summary>A typed UsdShade output descriptor.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdShadeOutput : IUsdStageBound
{
    internal UsdShadeOutput(
        UsdStage stage,
        string primPath,
        string name,
        UsdShadeValueType valueType)
    {
        Stage = stage;
        PrimPath = primPath;
        Name = name;
        ValueType = valueType;
    }

    /// <summary>Gets the owning prim path.</summary>
    public string PrimPath { get; }

    /// <summary>Gets the base output name without the outputs namespace.</summary>
    public string Name { get; }

    /// <summary>Gets the declared USD value type.</summary>
    public UsdShadeValueType ValueType { get; }

    /// <summary>Connects this output to another source output.</summary>
    public void ConnectToSource(UsdShadeOutput source)
    {
        UsdShadeSchema.ValidateSameStage(Stage, source.Stage);
        Stage.Native.ConnectShade(
            PrimPath,
            Name,
            OpenUsd.Interop.OpenUsdNativeShadeAttributeType.Output,
            source.PrimPath,
            source.Name,
            OpenUsd.Interop.OpenUsdNativeShadeAttributeType.Output);
    }

    /// <summary>Connects this output to a source input.</summary>
    public void ConnectToSource(UsdShadeInput source)
    {
        UsdShadeSchema.ValidateSameStage(Stage, source.Stage);
        Stage.Native.ConnectShade(
            PrimPath,
            Name,
            OpenUsd.Interop.OpenUsdNativeShadeAttributeType.Output,
            source.PrimPath,
            source.Name,
            OpenUsd.Interop.OpenUsdNativeShadeAttributeType.Input);
    }

    /// <summary>Disconnects the current source.</summary>
    public void Disconnect() =>
        Stage.Native.DisconnectShade(
            PrimPath,
            Name,
            OpenUsd.Interop.OpenUsdNativeShadeAttributeType.Output);

    /// <summary>Gets the single connected source.</summary>
    public UsdShadeConnection GetConnectedSource() =>
        UsdShadeSchema.FromNative(
            Stage.Native.GetConnectedShadeSource(
                PrimPath,
                Name,
                OpenUsd.Interop.OpenUsdNativeShadeAttributeType.Output));

    /// <summary>Gets every connected source in authored order.</summary>
    public IReadOnlyList<UsdShadeConnection> GetConnectedSources() =>
        Stage.Native.GetConnectedShadeSources(
                PrimPath,
                Name,
                OpenUsd.Interop.OpenUsdNativeShadeAttributeType.Output)
            .Select(UsdShadeSchema.FromNative)
            .ToArray();

    internal UsdStage Stage { get; }
}
