// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

void openusd_string_list_release(openusd_string_list* list)
{
    try
    {
        delete list;
    }
    catch (...)
    {
    }
}

void openusd_payload_arc_list_release(openusd_payload_arc_list* list)
{
    try
    {
        delete list;
    }
    catch (...)
    {
    }
}
