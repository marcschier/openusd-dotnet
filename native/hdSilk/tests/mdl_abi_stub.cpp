// Copyright (c) marcschier. Licensed under the MIT License.
//
// A deliberately wrong openusd_mdl adapter, used by the hdSilk probe to prove
// two loader properties at once.
//
// It exports the whole project-owned C ABI but reports an ABI version this
// build does not understand, so a loader that checks the version refuses it
// with AbiMismatch rather than calling into it.
//
// It also links a support library that is staged beside it in a private
// directory that is on no search path. Reaching the ABI check at all therefore
// requires the loader to resolve the stub's dependency from the directory the
// stub was loaded from -- LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR on Windows, the
// $ORIGIN/@loader_path run path elsewhere. A loader that searched the process
// or current directory instead would fail to load the stub and the probe would
// observe LoadFailed.

#include "openusd_mdl.h"

#include "mdl_stub_support.h"

extern "C" {

uint32_t
openusd_mdl_abi_version(void)
{
    // Deliberately not OPENUSD_MDL_ABI_VERSION, and deliberately derived from
    // the support library so the dependency cannot be optimized away.
    return HdSilkMdlStubSupportValue();
}

uint32_t
openusd_mdl_describe(char* buffer, uint32_t capacity)
{
    (void)buffer;
    (void)capacity;
    return 0;
}

uint32_t
openusd_mdl_adapter_create(
    const openusd_mdl_adapter_options* options,
    openusd_mdl_adapter** adapter)
{
    (void)options;
    (void)adapter;
    return OPENUSD_MDL_STATUS_INVALID_ARGUMENT;
}

uint32_t
openusd_mdl_adapter_configure(
    openusd_mdl_adapter* adapter,
    const openusd_mdl_adapter_options* options)
{
    (void)adapter;
    (void)options;
    return OPENUSD_MDL_STATUS_INVALID_ARGUMENT;
}

uint32_t
openusd_mdl_capabilities(void)
{
    return OPENUSD_MDL_CAPABILITY_AUTHORED_SUBSET;
}

void
openusd_mdl_adapter_destroy(openusd_mdl_adapter* adapter)
{
    (void)adapter;
}

uint32_t
openusd_mdl_adapter_distill(
    openusd_mdl_adapter* adapter,
    const openusd_mdl_material_request* request,
    const openusd_mdl_distilled_material** result)
{
    (void)adapter;
    (void)request;
    (void)result;
    return OPENUSD_MDL_STATUS_INVALID_ARGUMENT;
}

void
openusd_mdl_adapter_release_result(
    openusd_mdl_adapter* adapter,
    const openusd_mdl_distilled_material* result)
{
    (void)adapter;
    (void)result;
}
}
