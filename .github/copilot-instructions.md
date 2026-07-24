# Repository instructions

- Use .NET SDK 10.0.301 and `OpenUsd.slnx`.
- Keep production libraries compatible with `net8.0`, `net9.0`, and `net10.0`.
- Treat warnings, analyzer findings, formatting, trimming, and NativeAOT diagnostics as errors.
- Keep OpenUSD C++ types behind the project-owned C ABI.
- Do not introduce per-element P/Invoke on scene or render hot paths.
- Keep render backend logic separate from renderer-neutral and Hydra translation layers.
- Update documentation and package tests when ABI, runtime assets, or public APIs change.
