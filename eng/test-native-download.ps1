#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if (-not ('OpenUsd.Tooling.DownloadFixtureServer' -as [type]))
{
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenUsd.Tooling
{
    public sealed class DownloadFixtureServer : IDisposable
    {
        private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly ConcurrentDictionary<string, int> counts = new ConcurrentDictionary<string, int>();
        private readonly Task worker;
        public string BaseUri { get; }
        public DownloadFixtureServer()
        {
            listener.Start();
            BaseUri = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
            worker = Task.Run(RunAsync);
        }
        public int Count(string path) => counts.TryGetValue(path, out int count) ? count : 0;
        private async Task RunAsync()
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    using TcpClient client = await listener.AcceptTcpClientAsync(cancellation.Token);
                    using NetworkStream stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                    string request = await reader.ReadLineAsync(cancellation.Token);
                    string path = request.Split(' ')[1];
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellation.Token))) { }
                    int count = counts.AddOrUpdate(path, 1, (_, previous) => previous + 1);
                    byte[] body = Encoding.ASCII.GetBytes(path == "/hash" ? "wrong bytes" : "pinned archive fixture");
                    bool truncated = path == "/always" || (path == "/recover" && count == 1);
                    string status = path switch
                    {
                        "/denied" => "403 Forbidden",
                        "/unauthorized" => "401 Unauthorized",
                        "/server-error" => "503 Service Unavailable",
                        _ => "200 OK"
                    };
                    string header = "HTTP/1.1 " + status + "\r\nContent-Length: " +
                        (body.Length + (truncated ? 64 : 0)) + "\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancellation.Token);
                    await stream.WriteAsync(body, cancellation.Token);
                    await stream.FlushAsync(cancellation.Token);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
        public void Dispose()
        {
            cancellation.Cancel();
            listener.Stop();
            worker.GetAwaiter().GetResult();
            cancellation.Dispose();
        }
    }
}
'@
}

$source = Join-Path $PSScriptRoot 'fetch-native.ps1'
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref]$null, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw "Native downloader has syntax errors: $parseErrors" }
$definition = $ast.Find({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -ceq 'Get-VerifiedDownload'
}, $false)
if ($null -eq $definition) { throw 'The actual native download function was not found.' }
Invoke-Expression $definition.Extent.Text
$CacheRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot (
    '..\artifacts\native-download-contract\' + [Guid]::NewGuid().ToString('N'))))
$Force = $false
New-Item -ItemType Directory -Path $CacheRoot | Out-Null
$server = [OpenUsd.Tooling.DownloadFixtureServer]::new()
$expected = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
    [Text.Encoding]::ASCII.GetBytes('pinned archive fixture')))

function Assert-Refused([string]$Route, [int]$ExpectedRequests, [string]$Message, [switch]$KeepCache)
{
    $refused = $false
    try
    {
        Get-VerifiedDownload -Uri ($server.BaseUri + $Route) -FileName ($Route.TrimStart('/') + '.tar.gz') `
            -Sha256 $expected | Out-Null
    }
    catch
    {
        if (-not $_.Exception.Message.Contains($Message, [StringComparison]::OrdinalIgnoreCase)) { throw }
        $refused = $true
    }
    if (-not $refused) { throw "Expected $Route to be refused." }
    if ($server.Count($Route) -ne $ExpectedRequests) { throw "Unexpected request count for $Route." }
    $path = Join-Path $CacheRoot ($Route.TrimStart('/') + '.tar.gz')
    if ((Test-Path -LiteralPath $path) -ne $KeepCache.IsPresent -or
        (Test-Path -LiteralPath "$path.partial"))
    {
        throw "Failed $Route published or retained partial bytes."
    }
}

try
{
    $path = Get-VerifiedDownload -Uri ($server.BaseUri + '/recover') -FileName 'recover.tar.gz' -Sha256 $expected
    if ($server.Count('/recover') -ne 2 -or (Get-FileHash -LiteralPath $path).Hash -cne $expected -or
        (Test-Path -LiteralPath "$path.partial"))
    {
        throw 'A truncated response must recover once and publish only the exact pinned bytes.'
    }
    $cached = Get-VerifiedDownload -Uri ($server.BaseUri + '/recover') -FileName 'recover.tar.gz' -Sha256 $expected
    if ($cached -cne $path -or $server.Count('/recover') -ne 2)
    {
        throw 'A verified cache entry must not be downloaded again.'
    }
    Assert-Refused '/always' 3 'response ended'
    Assert-Refused '/denied' 1 '403'
    Assert-Refused '/unauthorized' 1 '401'
    Assert-Refused '/server-error' 1 '503'
    Assert-Refused '/hash' 1 'Hash mismatch'
    [IO.File]::WriteAllText($path, 'changed cache')
    Assert-Refused '/recover' 2 'Hash mismatch' -KeepCache
    if ([IO.File]::ReadAllText($path) -cne 'changed cache') { throw 'A refused cache must be preserved.' }
    $Force = $true
    $path = Get-VerifiedDownload -Uri ($server.BaseUri + '/recover') -FileName 'recover.tar.gz' -Sha256 $expected
    if ($server.Count('/recover') -ne 3 -or (Get-FileHash -LiteralPath $path).Hash -cne $expected)
    {
        throw 'Explicit Force must repair a changed cache only after pinned-byte verification.'
    }
    Assert-Refused '/hash' 2 'Hash mismatch'
    Write-Output 'NATIVE_DOWNLOAD_OK: real truncated HTTP, bounded refusal, SHA256, cache and explicit Force'
}
catch
{
    Write-Host "Native download fixture failed: $($_.Exception)"
    throw
}
finally
{
    $server.Dispose()
    Remove-Item -LiteralPath $CacheRoot -Recurse -Force
}
