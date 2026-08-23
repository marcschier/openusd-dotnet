# OpenUSD MCP Tool

`OpenUsd.Mcp.Tool` installs the `openusd-mcp` .NET tool, an MCP stdio server for
inspecting, editing, rendering, and validating OpenUSD scenes.

```powershell
dotnet tool install --global OpenUsd.Mcp.Tool
openusd-mcp
```

The tool package contains the managed server and its complete managed dependency
closure. It deliberately does not contain a local OpenUSD build, Core or Imaging
runtime packages, hdSilk binaries, plug-in metadata, or other native assets.
Installation and MCP tool discovery therefore work without a native runtime.

Scene and rendering operations require verified, version-matched Core, Imaging, and
hdSilk runtime roots. Put their `bin` and `lib` directories on `PATH` (Windows),
`LD_LIBRARY_PATH` (Linux), or `DYLD_LIBRARY_PATH` (macOS), and set
`OPENUSD_PLUGIN_PATH` to the merged USD plug-in root before starting `openusd-mcp`.
Repository builds should use `eng/run-mcp.ps1`, which verifies
`native/install/<rid>` and `native/install/shim/<rid>` metadata before configuring
those variables. `eng/publish-mcp-bundle.ps1` remains the RID-specific,
self-contained distribution alternative.

Common server configuration:

| Variable | Purpose |
| --- | --- |
| `OPENUSD_MCP_SOURCE_ROOT` | Root containing scenes the server may open. |
| `OPENUSD_MCP_OUTPUT_ROOT` | Root for generated scenes, captures, and reports. |
| `OPENUSD_PLUGIN_PATH` | Verified OpenUSD and hdSilk plug-in root. |
| `OPENUSD_MCP_VIEWER_ROOT` | Optional root containing a compatible Viewer bundle. |
| `OPENUSD_MCP_VIEWER_PATH` | Optional path to the Viewer executable. |

The tool targets .NET 10. Install a .NET 10 runtime or SDK before installation.
