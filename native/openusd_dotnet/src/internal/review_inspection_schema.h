// Copyright (c) marcschier. Licensed under the MIT License.
#pragma once

#include "pxr/usd/sdf/types.h"

#include <typeinfo>

namespace OpenUsdReview
{
void ValidateInspectionField(const pxr::TfToken& field, const pxr::VtValue& value, pxr::SdfSpecType type);
const std::type_info* InspectionValueType(const pxr::TfToken& name);
}
