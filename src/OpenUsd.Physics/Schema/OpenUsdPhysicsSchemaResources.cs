// Copyright (c) marcschier. Licensed under the MIT License.

using System.Reflection;

namespace OpenUsd.Physics.Schema;

/// <summary>
/// Carries the codeless <c>openUsdPhysics</c> schema plugin as embedded resources and lays it out
/// on disk in the directory shape OpenUSD plugin discovery expects.
/// </summary>
/// <remarks>
/// OpenUSD builds its schema registry once, lazily, from the plugin search path. A plugin that is
/// registered after the registry is built contributes no schema types. Extract the plugin with
/// <see cref="ExtractPluginTo"/> and put the returned directory on <c>PXR_PLUGINPATH_NAME</c>
/// before the hosting process touches a stage.
/// </remarks>
public static class OpenUsdPhysicsSchemaResources
{
    /// <summary>The embedded resource name of the codeless plugin registration.</summary>
    public const string PlugInfoResourceName = "OpenUsd.Physics.Schema.plugInfo.json";

    /// <summary>The embedded resource name of the flattened schema registry layer.</summary>
    public const string GeneratedSchemaResourceName = "OpenUsd.Physics.Schema.generatedSchema.usda";

    /// <summary>The directory name OpenUSD expects the plugin resources to live in.</summary>
    public const string ResourceDirectoryName = "resources";

    /// <summary>The plugin name registered by the embedded <c>plugInfo.json</c>.</summary>
    public const string PluginName = OpenUsdPhysicsTokens.PluginName;

    /// <summary>Opens the embedded <c>plugInfo.json</c>.</summary>
    /// <returns>A readable stream the caller owns.</returns>
    public static Stream OpenPlugInfo() => Open(PlugInfoResourceName);

    /// <summary>Opens the embedded <c>generatedSchema.usda</c>.</summary>
    /// <returns>A readable stream the caller owns.</returns>
    public static Stream OpenGeneratedSchema() => Open(GeneratedSchemaResourceName);

    /// <summary>Reads the embedded <c>plugInfo.json</c> as text.</summary>
    /// <returns>The verbatim resource content.</returns>
    public static string ReadPlugInfo() => Read(PlugInfoResourceName);

    /// <summary>Reads the embedded <c>generatedSchema.usda</c> as text.</summary>
    /// <returns>The verbatim resource content.</returns>
    public static string ReadGeneratedSchema() => Read(GeneratedSchemaResourceName);

    /// <summary>
    /// Writes the plugin into <paramref name="directory"/> as
    /// <c>&lt;directory&gt;/openUsdPhysics/resources/</c> and returns the resource directory.
    /// </summary>
    /// <param name="directory">The directory to lay the plugin out under.</param>
    /// <returns>The directory to put on <c>PXR_PLUGINPATH_NAME</c>.</returns>
    public static string ExtractPluginTo(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        string resources = Path.Combine(directory, PluginName, ResourceDirectoryName);
        Directory.CreateDirectory(resources);
        WriteResource(PlugInfoResourceName, Path.Combine(resources, "plugInfo.json"));
        WriteResource(GeneratedSchemaResourceName, Path.Combine(resources, "generatedSchema.usda"));
        return resources;
    }

    private static void WriteResource(string resourceName, string path)
    {
        using Stream source = Open(resourceName);
        using FileStream target = File.Create(path);
        source.CopyTo(target);
    }

    private static Stream Open(string resourceName)
    {
        Stream? stream = typeof(OpenUsdPhysicsSchemaResources).GetTypeInfo().Assembly
            .GetManifestResourceStream(resourceName);
        return stream ?? throw new InvalidOperationException(
            $"The embedded schema resource '{resourceName}' is missing from OpenUsd.Physics.");
    }

    private static string Read(string resourceName)
    {
        using Stream stream = Open(resourceName);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
