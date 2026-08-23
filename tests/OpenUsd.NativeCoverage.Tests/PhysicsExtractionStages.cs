// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text;
using OpenUsd.Physics.Extraction;

namespace OpenUsd.NativeCoverage.Tests;

/// <summary>Builds small composed stages from USDA text for extraction coverage.</summary>
internal static class PhysicsExtractionStages
{
    internal static UsdStage Open(string testName, string usda)
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(testName);
        string path = Path.Combine(directory, "stage.usda");
        File.WriteAllText(path, usda, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return UsdStage.Open(path);
    }

    internal static string Write(string testName, string usda)
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(testName);
        string path = Path.Combine(directory, "stage.usda");
        File.WriteAllText(path, usda, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    internal static UsdPhysicsExtractionPage Extract(string testName, string usda)
    {
        using UsdStage stage = Open(testName, usda);
        return UsdPhysicsStageExtractor.Extract(stage, UsdPhysicsExtractionOptions.Default);
    }

    internal static UsdPhysicsExtractionPage Extract(
        string testName, string usda, UsdPhysicsExtractionOptions options)
    {
        using UsdStage stage = Open(testName, usda);
        return UsdPhysicsStageExtractor.Extract(stage, options);
    }

    internal static UsdPhysicsExtractionObject Find(
        UsdPhysicsExtractionPage page, string path, UsdPhysicsExtractionObjectKind kind)
    {
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject candidate = page.GetObject(index);
            if (candidate.Path == path && candidate.Kind == kind)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"The page has no {kind} at {path}. It has: {Describe(page)}");
    }

    internal static bool TryFindProperty(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        out UsdPhysicsExtractionProperty property)
    {
        for (int offset = 0; offset < item.PropertyCount; offset++)
        {
            UsdPhysicsExtractionProperty candidate = page.GetProperty(item.PropertyStart + offset);
            if (candidate.Key == key)
            {
                property = candidate;
                return true;
            }
        }

        property = default;
        return false;
    }

    internal static UsdPhysicsExtractionProperty Property(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key) =>
        TryFindProperty(page, item, key, out UsdPhysicsExtractionProperty property)
            ? property
            : throw new InvalidOperationException($"{item.Path} has no {key} property.");

    internal static bool HasDiagnostic(
        UsdPhysicsExtractionPage page, UsdPhysicsExtractionCode code)
    {
        for (int index = 0; index < page.DiagnosticCount; index++)
        {
            if (page.GetDiagnostic(index).Code == code)
            {
                return true;
            }
        }
        return false;
    }

    internal static string Describe(UsdPhysicsExtractionPage page)
    {
        var text = new StringBuilder();
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            text.Append(CultureInfo.InvariantCulture, $"[{item.Kind} {item.Path}]");
        }
        return text.ToString();
    }
}
