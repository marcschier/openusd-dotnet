// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Package.Tests;

internal static class PortableReviewConsumerSource
{
    internal const string Text =
        """"
        using System;
        using System.IO;
        using System.Linq;
        using System.Text;
        using OpenUsd;
        using OpenUsd.Editing;

        namespace PackageExecutionConsumer;

        internal static class PortableReviewConsumer
        {
            internal static bool Run()
            {
                string directory = Path.Combine(AppContext.BaseDirectory, "portable-review-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                string sourcePath = Path.Combine(directory, "source.usda");
                string documentPath = Path.Combine(directory, "review.urd");
                byte[] original = Encoding.UTF8.GetBytes(
                    "#usda 1.0\n(\n    defaultPrim = \"World\"\n)\ndef Xform \"World\"\n{\n    double weight = 7\n}\n");
                File.WriteAllBytes(sourcePath, original);
                UsdReviewDocument captured;
                using (UsdStage stage = UsdStage.OpenForReview(sourcePath))
                {
                    UsdReviewSourceBinding source = stage.CaptureReviewSourceBinding();
                    using UsdLayer review = stage.GetUserReviewLayer();
                    var address = new UsdLayerEditAddress("/World.weight", UsdLayerEditField.Default);
                    UsdLayerEditResult edit = review.CompareAndApply(review.CaptureAuthored([address]),
                        [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(47), "double")]);
                    if (edit.Outcome != UsdLayerEditOutcome.Applied)
                    {
                        return false;
                    }
                    captured = review.CaptureReviewDocument(source, documentPath);
                    if (File.Exists(documentPath) || captured.ByteLength == 0 || source.Dependencies.Count == 0)
                    {
                        return false;
                    }
                    File.WriteAllBytes(documentPath, captured.CopyBytes());
                    if (!review.AcknowledgeSaved(captured) || review.GetEditingState().IsDirty)
                    {
                        return false;
                    }
                }
                byte[] documentBytes = File.ReadAllBytes(documentPath);
                UsdReviewDocumentInfo inspection = UsdReviewDocument.Inspect(documentBytes);
                if (inspection.DocumentByteLength != documentBytes.Length ||
                    !string.Equals(Path.GetFullPath(inspection.SourceRootPath), Path.GetFullPath(sourcePath),
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    return false;
                }
                UsdReviewDocument read = UsdReviewDocument.Read(documentBytes, sourcePath);
                using (UsdStage reopened = UsdStage.OpenForReview(sourcePath))
                {
                    UsdReviewDocumentImportResult imported = reopened.ImportReviewDocument(
                        read, reopened.CaptureReviewSourceBinding());
                    using UsdLayer review = reopened.GetUserReviewLayer();
                    if (imported.Outcome != UsdLayerEditOutcome.Applied ||
                        imported.AfterState?.Identity != review.GetEditingState().Identity ||
                        !captured.HasSamePayload(read) || review.AcknowledgeSaved(read) ||
                        reopened.GetPrim("/World").GetDouble("weight") != 47 ||
                        reopened.GetDefaultPrim().Path != "/World")
                    {
                        return false;
                    }
                }
                return File.ReadAllBytes(sourcePath).SequenceEqual(original);
            }
        }
        """";
}
