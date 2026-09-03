// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_MDL_H
#define OPENUSD_MDL_H

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#if defined(OPENUSD_MDL_BUILD)
#define OPENUSD_MDL_API __declspec(dllexport)
#else
#define OPENUSD_MDL_API __declspec(dllimport)
#endif
#else
#define OPENUSD_MDL_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/// Project-owned C ABI for the optional MDL material adapter.
///
/// The adapter is a separate shared library that hdSilk loads dynamically, if
/// it is present at all, while resolving a bound material and before any
/// material command reaches the hdSilk page wire format. Nothing in the base
/// runtime links it, and no shipped base package contains it: an MDL-only
/// material in a default install is reported by name, not shaded.
///
/// Only C scalar types, fixed-size structs and explicitly sized UTF-8 byte
/// ranges cross this boundary. No MDL SDK type, no OpenUSD type, and no C++
/// type appears in any signature, so the adapter can be rebuilt against a
/// different MDL SDK baseline -- or replaced entirely -- without rebuilding
/// hdSilk.
///
/// The call shape is deliberately bulk: one distill call carries every
/// authored parameter of one material and returns every distilled surface
/// input of that material. There is no per-node and no per-parameter call.
///
/// ABI v2 adds openusd_mdl_adapter_options, so an adapter that is backed by an
/// MDL SDK can be told which directories it may search for modules. Everything
/// v1 declared keeps its meaning and its layout; only openusd_mdl_adapter_create
/// changed shape, and it is versioned by this constant rather than overloaded.
#define OPENUSD_MDL_ABI_VERSION 2u

/// Bounds on the configuration a caller may hand an adapter. They exist so a
/// hostile or careless configuration cannot make the adapter walk an unbounded
/// tree or hold an unbounded string. A caller that exceeds one is refused with
/// OPENUSD_MDL_STATUS_INVALID_ARGUMENT rather than silently truncated.
#define OPENUSD_MDL_MAX_SEARCH_PATHS 16u
#define OPENUSD_MDL_MAX_PATH_BYTES 4096u

/// Status codes returned by every entry point and carried on a distilled
/// result. Anything other than OPENUSD_MDL_STATUS_OK means nothing was
/// distilled, and the caller must report the material by name rather than
/// shade it with a default.
#define OPENUSD_MDL_STATUS_OK 0u
/// A caller argument was null, mis-sized, or otherwise unusable.
#define OPENUSD_MDL_STATUS_INVALID_ARGUMENT 1u
/// The adapter understands the request shape but not this MDL module.
#define OPENUSD_MDL_STATUS_UNSUPPORTED_MODULE 2u
/// The module is accepted but this material name inside it is not.
#define OPENUSD_MDL_STATUS_UNSUPPORTED_MATERIAL 3u
/// The module and material are accepted, but the authored parameter set could
/// not be reduced to the renderer-neutral surface record.
#define OPENUSD_MDL_STATUS_DISTILLATION_FAILED 4u
/// The adapter would need an MDL SDK it was not built against. Returned, for
/// example, when a request needs module defaults or MDL expression evaluation
/// rather than authored USD values.
#define OPENUSD_MDL_STATUS_SDK_UNAVAILABLE 5u
/// The adapter ran out of memory building the result.
#define OPENUSD_MDL_STATUS_OUT_OF_MEMORY 6u
/// An SDK-backed adapter was asked for a module that no configured search path
/// contains. Distinct from UNSUPPORTED_MODULE, which means the module was found
/// or named but its material is outside what this adapter distils: a caller can
/// fix this one by supplying the module, and cannot fix the other.
#define OPENUSD_MDL_STATUS_MODULE_NOT_FOUND 7u
/// The module was found and loaded, but the MDL compiler rejected it. The
/// diagnostic carries the compiler's own messages.
#define OPENUSD_MDL_STATUS_MODULE_COMPILE_FAILED 8u
/// The material was compiled, but every accepted parameter resolved to an
/// expression this adapter does not evaluate -- a call, a layered BSDF, or a
/// texture the SDK could not resolve.
#define OPENUSD_MDL_STATUS_EXPRESSION_UNSUPPORTED 9u

/// Value kinds an authored MDL parameter can carry across this ABI. Anything
/// the caller cannot express as one of these is not sent, and the adapter
/// therefore never sees a partially converted value.
#define OPENUSD_MDL_VALUE_FLOAT 1u
#define OPENUSD_MDL_VALUE_FLOAT2 2u
#define OPENUSD_MDL_VALUE_FLOAT3 3u
#define OPENUSD_MDL_VALUE_FLOAT4 4u
#define OPENUSD_MDL_VALUE_BOOL 5u
#define OPENUSD_MDL_VALUE_INT 6u
#define OPENUSD_MDL_VALUE_ASSET 7u
#define OPENUSD_MDL_VALUE_STRING 8u

/// Surface inputs a distilled record can drive. The numeric values are chosen
/// to match the UsdPreviewSurface input the entry feeds, because distillation
/// targets the renderer-neutral PreviewSurface-compatible record rather than a
/// separate MDL shading model. hdSilk still maps each id explicitly, so a
/// future divergence is a compile error rather than a silent mis-binding.
#define OPENUSD_MDL_SURFACE_DIFFUSE_COLOR 1u
#define OPENUSD_MDL_SURFACE_EMISSIVE_COLOR 2u
#define OPENUSD_MDL_SURFACE_METALLIC 4u
#define OPENUSD_MDL_SURFACE_ROUGHNESS 5u
#define OPENUSD_MDL_SURFACE_OPACITY 8u
#define OPENUSD_MDL_SURFACE_OPACITY_THRESHOLD 9u
#define OPENUSD_MDL_SURFACE_IOR 10u
#define OPENUSD_MDL_SURFACE_NORMAL 11u
#define OPENUSD_MDL_SURFACE_OCCLUSION 13u

/// Texture wrap modes. The values match the hdSilk wire enumeration so a
/// distilled texture needs no remapping table beyond the explicit mapping
/// hdSilk performs on every entry.
#define OPENUSD_MDL_WRAP_BLACK 0u
#define OPENUSD_MDL_WRAP_CLAMP 1u
#define OPENUSD_MDL_WRAP_REPEAT 2u
#define OPENUSD_MDL_WRAP_MIRROR 3u

/// Sampled output channel of a distilled texture.
#define OPENUSD_MDL_CHANNEL_R 0u
#define OPENUSD_MDL_CHANNEL_G 1u
#define OPENUSD_MDL_CHANNEL_B 2u
#define OPENUSD_MDL_CHANNEL_A 3u
#define OPENUSD_MDL_CHANNEL_RGB 4u

/// Colour space a distilled texture must be decoded in.
#define OPENUSD_MDL_COLOR_SPACE_AUTO 0u
#define OPENUSD_MDL_COLOR_SPACE_RAW 1u
#define OPENUSD_MDL_COLOR_SPACE_SRGB 2u

/// A counted UTF-8 byte range. Never NUL terminated and never owned by the
/// receiver; the producer states how long the bytes stay valid.
typedef struct openusd_mdl_string
{
    const char* data;
    uint32_t size;
} openusd_mdl_string;

/// One authored MDL shader input, exactly as the scene authored it. The caller
/// converts USD value types into these fields; it never evaluates MDL.
typedef struct openusd_mdl_parameter
{
    openusd_mdl_string name;
    uint32_t kind;
    uint32_t component_count;
    float value[4];
    int32_t integer_value;
    openusd_mdl_string text;
} openusd_mdl_parameter;

/// One material to distil. `module_uri` is the authored `info:mdl:sourceAsset`
/// as written, resolved or not, and `material_name` is the authored
/// `info:mdl:sourceAsset:subIdentifier`. Both are identity only: an adapter
/// built without an MDL SDK must not open the module.
typedef struct openusd_mdl_material_request
{
    uint32_t struct_size;
    openusd_mdl_string module_uri;
    openusd_mdl_string material_name;
    openusd_mdl_string material_path;
    const openusd_mdl_parameter* parameters;
    uint32_t parameter_count;
} openusd_mdl_material_request;

/// One distilled constant surface input.
typedef struct openusd_mdl_distilled_scalar
{
    uint32_t surface_input;
    uint32_t component_count;
    float value[4];
    /// OPENUSD_MDL_ORIGIN_*: where this value came from.
    uint32_t origin;
} openusd_mdl_distilled_scalar;

/// One distilled texture-driven surface input.
typedef struct openusd_mdl_distilled_texture
{
    uint32_t surface_input;
    uint32_t component_count;
    uint32_t output_channel;
    uint32_t wrap_s;
    uint32_t wrap_t;
    uint32_t color_space;
    float scale[4];
    float bias[4];
    openusd_mdl_string asset;
    /// OPENUSD_MDL_ORIGIN_*: where this texture reference came from.
    uint32_t origin;
} openusd_mdl_distilled_texture;

/// The distilled form of one material. Every pointer stays valid until the
/// matching openusd_mdl_adapter_release_result call.
///
/// `unsupported_parameters` names the authored inputs the adapter understood
/// as belonging to the material but deliberately did not distil. It exists so
/// the caller can report each dropped input by name instead of letting an
/// authored value silently vanish into a default.
typedef struct openusd_mdl_distilled_material
{
    uint32_t struct_size;
    uint32_t status;
    openusd_mdl_string diagnostic;
    const openusd_mdl_distilled_scalar* scalars;
    uint32_t scalar_count;
    const openusd_mdl_distilled_texture* textures;
    uint32_t texture_count;
    const openusd_mdl_string* unsupported_parameters;
    uint32_t unsupported_parameter_count;
} openusd_mdl_distilled_material;

/// Opaque adapter instance.
typedef struct openusd_mdl_adapter openusd_mdl_adapter;

/// How one distilled entry was obtained. Carried per entry so a caller can tell
/// a value the stage authored from one the adapter read out of an MDL module,
/// which is the difference between "the author said this" and "the module
/// defaults to this".
#define OPENUSD_MDL_ORIGIN_AUTHORED 0u
#define OPENUSD_MDL_ORIGIN_MODULE_DEFAULT 1u
#define OPENUSD_MDL_ORIGIN_MODULE_EXPRESSION 2u

/// Capability bits an adapter reports, so a caller can state what it is about
/// to rely on rather than infer it from whether a call happened to succeed.
/// AUTHORED_SUBSET is always set. The rest are set only by an adapter built
/// against an MDL SDK and only for what it actually implements.
#define OPENUSD_MDL_CAPABILITY_AUTHORED_SUBSET 0x1u
#define OPENUSD_MDL_CAPABILITY_MODULE_DEFAULTS 0x2u
#define OPENUSD_MDL_CAPABILITY_CONSTANT_EXPRESSIONS 0x4u
#define OPENUSD_MDL_CAPABILITY_TEXTURE_RESOLUTION 0x8u

/// Bounded adapter configuration.
///
/// `module_search_paths` are the only directories an SDK-backed adapter may
/// resolve a module from. Every entry must be an absolute path; a relative one
/// is refused, because it would be resolved against the process working
/// directory, and neither the working directory nor any implicit system
/// location is ever added by the adapter. An adapter that needs no search path
/// -- the dependency-free authored-value one -- ignores the field.
///
/// `cache_generation` invalidates whatever the adapter cached about modules and
/// materials. A caller that changes the search paths, or learns that a module
/// on disk changed, passes a value it has not passed before; the adapter then
/// discards its caches rather than answering from a stale compile.
typedef struct openusd_mdl_adapter_options
{
    uint32_t struct_size;
    const openusd_mdl_string* module_search_paths;
    uint32_t module_search_path_count;
    uint64_t cache_generation;
} openusd_mdl_adapter_options;

/// Returns OPENUSD_MDL_ABI_VERSION as the loaded library implements it. A
/// loader that sees any other value must refuse the library rather than call
/// into it.
typedef uint32_t (*openusd_mdl_abi_version_fn)(void);
OPENUSD_MDL_API uint32_t openusd_mdl_abi_version(void);

/// Returns the OPENUSD_MDL_CAPABILITY_* bits this adapter implements.
typedef uint32_t (*openusd_mdl_capabilities_fn)(void);
OPENUSD_MDL_API uint32_t openusd_mdl_capabilities(void);

/// Writes a NUL-terminated provenance string naming the adapter build and
/// whether an MDL SDK is linked. Returns the number of bytes required
/// including the terminator; `capacity` may be zero to query the size.
typedef uint32_t (*openusd_mdl_describe_fn)(char* buffer, uint32_t capacity);
OPENUSD_MDL_API uint32_t openusd_mdl_describe(char* buffer, uint32_t capacity);

/// Creates an adapter instance. `options` may be null, which means "no search
/// path and generation zero". Instances are not thread safe; the caller
/// serializes access or creates one per thread.
typedef uint32_t (*openusd_mdl_adapter_create_fn)(
    const openusd_mdl_adapter_options* options,
    openusd_mdl_adapter** adapter);
OPENUSD_MDL_API uint32_t openusd_mdl_adapter_create(
    const openusd_mdl_adapter_options* options,
    openusd_mdl_adapter** adapter);

/// Replaces an existing instance's configuration. Returns
/// OPENUSD_MDL_STATUS_OK when the adapter accepted it; the caller must treat
/// every result obtained before the call as stale.
typedef uint32_t (*openusd_mdl_adapter_configure_fn)(
    openusd_mdl_adapter* adapter,
    const openusd_mdl_adapter_options* options);
OPENUSD_MDL_API uint32_t openusd_mdl_adapter_configure(
    openusd_mdl_adapter* adapter,
    const openusd_mdl_adapter_options* options);

/// Destroys an adapter instance. Null is accepted and ignored.
typedef void (*openusd_mdl_adapter_destroy_fn)(openusd_mdl_adapter* adapter);
OPENUSD_MDL_API void openusd_mdl_adapter_destroy(openusd_mdl_adapter* adapter);

/// Distils one material. On any status other than OPENUSD_MDL_STATUS_OK the
/// result still carries a diagnostic naming the material, and its tables are
/// empty. A non-null result must always be released.
typedef uint32_t (*openusd_mdl_adapter_distill_fn)(
    openusd_mdl_adapter* adapter,
    const openusd_mdl_material_request* request,
    const openusd_mdl_distilled_material** result);
OPENUSD_MDL_API uint32_t openusd_mdl_adapter_distill(
    openusd_mdl_adapter* adapter,
    const openusd_mdl_material_request* request,
    const openusd_mdl_distilled_material** result);

/// Releases a result previously returned by openusd_mdl_adapter_distill.
typedef void (*openusd_mdl_adapter_release_result_fn)(
    openusd_mdl_adapter* adapter,
    const openusd_mdl_distilled_material* result);
OPENUSD_MDL_API void openusd_mdl_adapter_release_result(
    openusd_mdl_adapter* adapter,
    const openusd_mdl_distilled_material* result);

#ifdef __cplusplus
}
#endif

#endif
