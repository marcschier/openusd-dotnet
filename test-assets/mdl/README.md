# MDL fixtures

Synthetic MDL modules and USD stages that exercise the optional `openusd_mdl_sdk`
adapter's module evaluation.

Everything here is written for this repository and carries its MIT licence. **No
NVIDIA Omniverse module source is copied, adapted, or paraphrased**: not
`OmniPBR.mdl`, not `OmniSurface.mdl`, not `OmniGlass.mdl`, not any other. The
parameter *names* these modules declare are the public interface names an
Omniverse-authored stage binds, which is data rather than source, and using them
is the whole point: the adapter's public-name mapping is exercised without
redistributing anyone's shader.

The modules use the MDL standard library only, and are compiled at test time by a
user-supplied MDL SDK runtime. Nothing here is packaged.

- `openusd_probe.mdl` — three materials, each pinning one behaviour of the
  SDK-backed adapter.
  - `openusd_probe_defaults` gives every accepted parameter a literal default and
    nothing else, so a stage that authors nothing must still shade and every
    value must be marked as coming from the module.
  - `openusd_probe_expressions` gives its defaults as constant *expressions*: a
    broadcast colour constructor, and an alias that forwards another parameter's
    default. Both must fold to a value.
  - `openusd_probe_unsupported` gives its defaults as real computations over a
    layered BSDF. It must distil nothing and report every affected parameter by
    name, because a plausible-looking value the module does not produce is worse
    than no value at all.
- `mdl-module-defaults.usda` — an MDL-only material bound to
  `openusd_probe_defaults`. It authors exactly one input and leaves the rest of
  the accepted subset unauthored, so the published record can only be right if
  the authored value won for its own input *and* the module defaults filled the
  others. That mixture is what makes it evidence of module evaluation rather
  than of authored values being echoed back.

The companion fixture `../omniverse/mdl-only-omnipbr.usda` covers the opposite
case: a module that is deliberately absent, pinning the reported-not-shaded
behaviour every base package ships with.
