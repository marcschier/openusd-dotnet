// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

/// <summary>
/// The Viewer's persisted colour-management choice, resolved into a renderer-neutral
/// <see cref="RenderDisplayTransform"/> or into one bounded reason it could not be.
/// </summary>
/// <remarks>
/// <para>
/// Only paths and names are ever held or persisted here. There is no credential, token,
/// or user content anywhere in this model, and the settings store writes exactly these
/// fields and nothing else.
/// </para>
/// <para>
/// The config path may be left empty, in which case the standard <c>OCIO</c> environment
/// variable is honoured the way every other OpenColorIO-aware application honours it.
/// That keeps the Viewer's own control surface to a single toggle and a single file
/// chooser instead of a colour-management panel.
/// </para>
/// </remarks>
internal sealed record ViewerColorManagement
{
    /// <summary>Gets the environment variable OpenColorIO applications read.</summary>
    internal const string EnvironmentVariable = "OCIO";

    /// <summary>Gets the disabled default.</summary>
    internal static ViewerColorManagement Default { get; } = new();

    /// <summary>Gets whether the display transform should be applied.</summary>
    internal bool Enabled { get; init; }

    /// <summary>Gets the authored config path, or an empty string to use the environment.</summary>
    internal string ConfigPath { get; init; } = string.Empty;

    /// <summary>Gets the colour space the renderer's linear scene colour is in.</summary>
    internal string SourceColorSpace { get; init; } = "linear";

    /// <summary>Gets the display name, or an empty string for the config default.</summary>
    internal string Display { get; init; } = string.Empty;

    /// <summary>Gets the view name, or an empty string for the display default.</summary>
    internal string View { get; init; } = string.Empty;

    /// <summary>Gets the look expression, or an empty string for none.</summary>
    internal string Look { get; init; } = string.Empty;

    /// <summary>Gets whether every persisted field is within its supported bounds.</summary>
    internal bool IsValid() =>
        ConfigPath.Length <= RenderDisplayTransform.MaximumConfigPathLength &&
        !ConfigPath.Contains('\n', StringComparison.Ordinal) &&
        !ConfigPath.Contains('\r', StringComparison.Ordinal) &&
        IsValidName(SourceColorSpace, required: true) &&
        IsValidName(Display, required: false) &&
        IsValidName(View, required: false) &&
        IsValidName(Look, required: false);

    /// <summary>
    /// Resolves the persisted choice into a display transform, or explains why it could
    /// not be resolved.
    /// </summary>
    /// <param name="transform">
    /// The resolved transform, or <see langword="null"/> when colour management is
    /// disabled or unresolvable.
    /// </param>
    /// <param name="diagnostic">
    /// <see langword="null"/> when the result is exactly what was asked for, otherwise a
    /// bounded human-readable reason. A disabled setting resolves to a null transform
    /// with no diagnostic, because that is not a failure.
    /// </param>
    /// <returns><see langword="true"/> when a transform was produced.</returns>
    internal bool TryResolve(
        out RenderDisplayTransform? transform,
        out string? diagnostic)
    {
        transform = null;
        diagnostic = null;
        if (!Enabled)
        {
            return false;
        }

        string configPath = ResolveConfigPath();
        if (configPath.Length == 0)
        {
            diagnostic =
                "No OpenColorIO config is configured and the OCIO environment variable " +
                "is not set, so no display transform was applied.";
            return false;
        }

        try
        {
            transform = new RenderDisplayTransform(
                configPath,
                SourceColorSpace,
                Display.Length == 0 ? null : Display,
                View.Length == 0 ? null : View,
                Look.Length == 0 ? null : Look);
        }
        catch (Exception exception) when (
            exception is ArgumentException or ArgumentOutOfRangeException)
        {
            diagnostic =
                $"The configured OpenColorIO display transform is not usable: " +
                exception.Message;
            return false;
        }

        return true;
    }

    /// <summary>Gets the config path in effect, preferring the authored value.</summary>
    internal string ResolveConfigPath()
    {
        if (ConfigPath.Length != 0)
        {
            return ConfigPath;
        }

        string? environment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        return string.IsNullOrWhiteSpace(environment) ||
            environment.Length > RenderDisplayTransform.MaximumConfigPathLength
            ? string.Empty
            : environment.Trim();
    }

    private static bool IsValidName(string value, bool required)
    {
        if (value.Length == 0)
        {
            return !required;
        }
        return value.Length <= RenderDisplayTransform.MaximumNameLength &&
            !value.Contains('\n', StringComparison.Ordinal) &&
            !value.Contains('\r', StringComparison.Ordinal) &&
            value.Trim().Length == value.Length;
    }
}
