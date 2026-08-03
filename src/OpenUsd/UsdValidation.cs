// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.ObjectModel;
using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>Severity reported by UsdValidation.</summary>
public enum UsdValidationSeverity
{
    /// <summary>No error.</summary>
    None = 0,
    /// <summary>Error severity.</summary>
    Error = 1,
    /// <summary>Warning severity.</summary>
    Warning = 2,
    /// <summary>Informational severity.</summary>
    Info = 3
}

/// <summary>Metadata for one registered UsdValidation validator.</summary>
public sealed record UsdValidationValidatorInfo(
    string Name,
    string Documentation,
    string PluginName,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> SchemaTypes,
    bool IsSuite,
    bool IsTimeDependent)
{
    /// <inheritdoc />
    public bool Equals(UsdValidationValidatorInfo? other) =>
        other is not null &&
        Name == other.Name &&
        Documentation == other.Documentation &&
        PluginName == other.PluginName &&
        Keywords.SequenceEqual(other.Keywords) &&
        SchemaTypes.SequenceEqual(other.SchemaTypes) &&
        IsSuite == other.IsSuite &&
        IsTimeDependent == other.IsTimeDependent;

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(
            Name,
            Documentation,
            PluginName,
            RecordCollectionFormatting.SequenceHashCode(Keywords),
            RecordCollectionFormatting.SequenceHashCode(SchemaTypes),
            IsSuite,
            IsTimeDependent);

    /// <inheritdoc />
    public override string ToString() =>
        $"{nameof(UsdValidationValidatorInfo)} {{ {nameof(Name)} = {Name}, " +
        $"{nameof(Documentation)} = {Documentation}, {nameof(PluginName)} = {PluginName}, " +
        $"{nameof(Keywords)} = {RecordCollectionFormatting.FormatSequence(Keywords)}, " +
        $"{nameof(SchemaTypes)} = {RecordCollectionFormatting.FormatSequence(SchemaTypes)}, " +
        $"{nameof(IsSuite)} = {IsSuite}, {nameof(IsTimeDependent)} = {IsTimeDependent} }}";
}

/// <summary>One UsdValidation error detached from native storage.</summary>
public sealed record UsdValidationError(
    UsdValidationSeverity Severity,
    string ValidatorName,
    string ErrorName,
    string Message,
    IReadOnlyList<string> Sites)
{
    /// <inheritdoc />
    public bool Equals(UsdValidationError? other) =>
        other is not null &&
        Severity == other.Severity &&
        ValidatorName == other.ValidatorName &&
        ErrorName == other.ErrorName &&
        Message == other.Message &&
        Sites.SequenceEqual(other.Sites);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(
            Severity,
            ValidatorName,
            ErrorName,
            Message,
            RecordCollectionFormatting.SequenceHashCode(Sites));

    /// <inheritdoc />
    public override string ToString() =>
        $"{nameof(UsdValidationError)} {{ {nameof(Severity)} = {Severity}, " +
        $"{nameof(ValidatorName)} = {ValidatorName}, {nameof(ErrorName)} = {ErrorName}, " +
        $"{nameof(Message)} = {Message}, {nameof(Sites)} = " +
        $"{RecordCollectionFormatting.FormatSequence(Sites)} }}";
}

/// <summary>Provides access to the OpenUSD validation registry and validation runs.</summary>
public static class UsdValidation
{
    /// <summary>Enumerates validators registered in the OpenUSD validation registry.</summary>
    public static IReadOnlyList<UsdValidationValidatorInfo> GetRegisteredValidators()
    {
        OpenUsdNativeValidationMetadata[] metadata = OpenUsdNativeRuntime.GetValidationMetadata();
        var result = new UsdValidationValidatorInfo[metadata.Length];
        for (int i = 0; i < result.Length; i++)
        {
            OpenUsdNativeValidationMetadata item = metadata[i];
            result[i] = new UsdValidationValidatorInfo(
                item.Name,
                item.Documentation,
                item.PluginName,
                Array.AsReadOnly(item.Keywords),
                Array.AsReadOnly(item.SchemaTypes),
                item.IsSuite,
                item.IsTimeDependent);
        }
        return Array.AsReadOnly(result);
    }

    /// <summary>Runs all registered validators against a stage and returns all reported errors.</summary>
    public static IReadOnlyList<UsdValidationError> Validate(UsdStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return Convert(stage.Native.ValidateStage());
    }

    /// <summary>Runs all registered prim validators against one prim and returns all reported errors.</summary>
    public static IReadOnlyList<UsdValidationError> Validate(UsdPrim prim)
    {
        return Convert(prim.Stage.Native.ValidatePrim(prim.Path));
    }

    private static ReadOnlyCollection<UsdValidationError> Convert(OpenUsdNativeValidationError[] errors)
    {
        var result = new UsdValidationError[errors.Length];
        for (int i = 0; i < result.Length; i++)
        {
            OpenUsdNativeValidationError error = errors[i];
            result[i] = new UsdValidationError(
                (UsdValidationSeverity)error.Severity,
                error.ValidatorName,
                error.ErrorName,
                error.Message,
                Array.AsReadOnly(error.Sites));
        }
        return Array.AsReadOnly(result);
    }
}
