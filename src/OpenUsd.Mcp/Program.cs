// Copyright (c) marcschier. Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenUsd.Mcp;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
McpHostConfiguration.ConfigureLogging(builder.Logging);
OpenUsdMcpApplicationOptions options = McpHostConfiguration.LoadOptions();

builder.Services.AddOpenUsdMcpServices(options);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithOpenUsdTools();

await builder.Build().RunAsync();
