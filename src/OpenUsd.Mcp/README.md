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
| `OPENUSD_SILK_MESH_RESERVATION_BYTES` | Optional logical coarse-mesh reservation ceiling; requires the page ceiling. |
| `OPENUSD_SILK_MAX_PAGE_BYTES` | Optional positive `Int32` serialized-page byte ceiling; requires the mesh ceiling. |
| `OPENUSD_SILK_GPU_BUFFER_BYTES` | Optional shared logical RHI buffer payload ceiling; independent of native limits. |

Session ceilings also apply to legacy preview and separate product captures, and are
forwarded to a launched Viewer. Storm cannot bypass them. Both unset preserves defaults;
partial/invalid configuration fails startup. These are not total process/GPU memory limits.

Managed hdSilk sessions require the explicit native page-acknowledgement extension;
matching session/page ABI versions alone do not make an older runtime compatible.

GPU buffer reservations precede native creation and survive while submissions retain
the buffer. Textures, backend staging, driver overhead and source/managed memory are
not included. A launched Viewer receives the maximum for its own independent pool.

The tool targets .NET 10. Install a .NET 10 runtime or SDK before installation.
