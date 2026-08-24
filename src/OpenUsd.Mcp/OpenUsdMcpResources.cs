// Copyright (c) marcschier. Licensed under the MIT License.

using System.ComponentModel;
using System.Text;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace OpenUsd.Mcp;

[McpServerResourceType]
internal sealed class OpenUsdMcpResources(IArtifactResourceStore artifacts)
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    [McpServerResource(
        UriTemplate = "openusd://artifact/{id}",
        Name = "openusd_artifact",
        Title = "OpenUSD Artifact")]
    [Description(OpenUsdMcpDescriptions.ArtifactResource)]
    public async ValueTask<ResourceContents> ReadArtifactAsync(
        [Description(
            "Percent-decoded process-local artifact identifier copied from an " +
            "openusd://artifact/{id} tool result URI; 1-1024 characters with no control " +
            "characters or path separators.")] string id,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateArtifactId(id);
        Uri uri = ArtifactResourceUri.Create(id);
        ArtifactResourceContent? resource;
        try
        {
            resource = await artifacts.ReadAsync(uri, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ArtifactResourceReadLimitException exception)
        {
            throw new McpException(
                $"artifact_too_large: {exception.Message}",
                exception);
        }
        catch (ArtifactResourceIntegrityException exception)
        {
            throw new McpException(
                $"artifact_integrity_error: {exception.Message}",
                exception);
        }

        if (resource is null)
        {
            throw new McpException($"artifact_not_found: Resource '{uri}' does not exist.");
        }

        ArtifactResourceDescriptor descriptor = resource.Descriptor;
        ReadOnlyMemory<byte> content = resource.Content;
        if (IsText(descriptor.MediaType))
        {
            string text;
            try
            {
                text = StrictUtf8.GetString(content.Span);
            }
            catch (DecoderFallbackException exception)
            {
                throw new McpException(
                    $"artifact_invalid_text: Resource '{uri}' is not valid UTF-8.",
                    exception);
            }

            return new TextResourceContents
            {
                Uri = uri.AbsoluteUri,
                MimeType = descriptor.MediaType,
                Text = text,
            };
        }

        return BlobResourceContents.FromBytes(
            content,
            uri.AbsoluteUri,
            descriptor.MediaType);
    }

    private static bool IsText(string mediaType) =>
        mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
        mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
        mediaType.Equals("application/xml", StringComparison.OrdinalIgnoreCase) ||
        mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase) ||
        mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase);

    private static void ValidateArtifactId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            id.Length > OpenUsdMcpLimits.MaximumPathLength ||
            id.Any(char.IsControl) ||
            id.Contains('/') ||
            id.Contains('\\'))
        {
            throw new McpException("invalid_argument: The artifact identifier is invalid.");
        }
    }
}
