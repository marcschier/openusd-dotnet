// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

#include "pxr/base/tf/type.h"
#include "pxr/base/ts/spline.h"

struct openusd_ts_spline
{
    TsSpline value{TfType::Find<double>()};
    std::vector<openusd_ts_knot_record> snapshot;
};

namespace
{
TsInterpMode ToInterp(int32_t value)
{
    switch (value)
    {
    case OPENUSD_TS_INTERP_VALUE_BLOCK: return TsInterpValueBlock;
    case OPENUSD_TS_INTERP_HELD: return TsInterpHeld;
    case OPENUSD_TS_INTERP_LINEAR: return TsInterpLinear;
    case OPENUSD_TS_INTERP_CURVE: return TsInterpCurve;
    default: throw std::invalid_argument("Unsupported Ts interpolation mode.");
    }
}

TsCurveType ToCurve(int32_t value)
{
    switch (value)
    {
    case OPENUSD_TS_CURVE_BEZIER: return TsCurveTypeBezier;
    case OPENUSD_TS_CURVE_HERMITE: return TsCurveTypeHermite;
    default: throw std::invalid_argument("Unsupported Ts curve type.");
    }
}

TsExtrapMode ToExtrap(int32_t value)
{
    switch (value)
    {
    case OPENUSD_TS_EXTRAP_VALUE_BLOCK: return TsExtrapValueBlock;
    case OPENUSD_TS_EXTRAP_HELD: return TsExtrapHeld;
    case OPENUSD_TS_EXTRAP_LINEAR: return TsExtrapLinear;
    case OPENUSD_TS_EXTRAP_SLOPED: return TsExtrapSloped;
    case OPENUSD_TS_EXTRAP_LOOP_REPEAT: return TsExtrapLoopRepeat;
    case OPENUSD_TS_EXTRAP_LOOP_RESET: return TsExtrapLoopReset;
    case OPENUSD_TS_EXTRAP_LOOP_OSCILLATE: return TsExtrapLoopOscillate;
    default: throw std::invalid_argument("Unsupported Ts extrapolation mode.");
    }
}

TsTangentAlgorithm ToAlgorithm(int32_t value)
{
    switch (value)
    {
    case OPENUSD_TS_TANGENT_NONE: return TsTangentAlgorithmNone;
    case OPENUSD_TS_TANGENT_CUSTOM: return TsTangentAlgorithmCustom;
    case OPENUSD_TS_TANGENT_AUTO_EASE: return TsTangentAlgorithmAutoEase;
    default: throw std::invalid_argument("Unsupported Ts tangent algorithm.");
    }
}

openusd_ts_knot_record FromKnot(const TsKnot& knot)
{
    openusd_ts_knot_record record{};
    record.time = knot.GetTime();
    double value = 0.0;
    if (knot.GetValue(&value))
    {
        record.value = value;
    }
    double preValue = 0.0;
    if (knot.GetPreValue(&preValue))
    {
        record.pre_value = preValue;
        record.flags |= OPENUSD_TS_KNOT_HAS_PRE_VALUE;
    }
    record.pre_tangent_width = knot.GetPreTanWidth();
    knot.GetPreTanSlope(&record.pre_tangent_slope);
    record.post_tangent_width = knot.GetPostTanWidth();
    knot.GetPostTanSlope(&record.post_tangent_slope);
    record.next_interpolation = static_cast<int32_t>(knot.GetNextInterpolation());
    record.pre_tangent_algorithm = static_cast<int32_t>(knot.GetPreTanAlgorithm());
    record.post_tangent_algorithm = static_cast<int32_t>(knot.GetPostTanAlgorithm());
    return record;
}

TsKnot ToKnot(const openusd_ts_knot_record& record)
{
    if (!std::isfinite(record.time) || !std::isfinite(record.value) ||
        !std::isfinite(record.pre_value) || !std::isfinite(record.pre_tangent_width) ||
        !std::isfinite(record.pre_tangent_slope) || !std::isfinite(record.post_tangent_width) ||
        !std::isfinite(record.post_tangent_slope))
    {
        throw std::invalid_argument("Ts knot values must be finite.");
    }
    TsKnot knot(TfType::Find<double>());
    if (!knot.SetTime(record.time) || !knot.SetValue(record.value) ||
        !knot.SetNextInterpolation(ToInterp(record.next_interpolation)) ||
        !knot.SetPreTanWidth(record.pre_tangent_width) ||
        !knot.SetPreTanSlope(record.pre_tangent_slope) ||
        !knot.SetPostTanWidth(record.post_tangent_width) ||
        !knot.SetPostTanSlope(record.post_tangent_slope) ||
        !knot.SetPreTanAlgorithm(ToAlgorithm(record.pre_tangent_algorithm)) ||
        !knot.SetPostTanAlgorithm(ToAlgorithm(record.post_tangent_algorithm)))
    {
        throw std::invalid_argument("The Ts knot record is not valid for a double spline.");
    }
    if ((record.flags & OPENUSD_TS_KNOT_HAS_PRE_VALUE) != 0 &&
        !knot.SetPreValue(record.pre_value))
    {
        throw std::invalid_argument("The Ts knot pre-value is invalid.");
    }
    return knot;
}

void ResetTsView(openusd_ts_spline_data_view* view) noexcept
{
    if (view == nullptr)
    {
        return;
    }
    const uint32_t structSize = view->struct_size;
    const uint32_t version = view->version;
    std::memset(view, 0, sizeof(*view));
    view->struct_size = structSize;
    view->version = version == 0 ? OPENUSD_TS_SPLINE_DATA_VIEW_VERSION : version;
}

openusd_status ValidateTsView(const openusd_ts_spline_data_view* view, openusd_error_buffer* error)
{
    if (view == nullptr || !IsAligned(view) ||
        view->struct_size < sizeof(openusd_ts_spline_data_view) ||
        view->version != OPENUSD_TS_SPLINE_DATA_VIEW_VERSION)
    {
        WriteError(error, "A valid Ts spline data view version 1 is required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (view->knot_count > 0 && view->knots == nullptr)
    {
        WriteError(error, "A non-empty Ts spline data view requires knot records.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (view->knots_size != view->knot_count * sizeof(openusd_ts_knot_record))
    {
        WriteError(error, "The Ts spline knot buffer size is invalid.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    return OPENUSD_STATUS_OK;
}
}

openusd_status openusd_ts_spline_create(openusd_ts_spline** spline, openusd_error_buffer* error)
{
    if (spline != nullptr)
    {
        *spline = nullptr;
    }
    return Guard(error, [&]() -> openusd_status
    {
        if (spline == nullptr)
        {
            WriteError(error, "A spline output pointer is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        *spline = new openusd_ts_spline();
        return OPENUSD_STATUS_OK;
    });
}

void openusd_ts_spline_release(openusd_ts_spline* spline)
{
    delete spline;
}

openusd_status openusd_ts_spline_set_data(
    openusd_ts_spline* spline,
    const openusd_ts_spline_data_view* view,
    openusd_error_buffer* error)
{
    return Guard(error, [&]() -> openusd_status
    {
        if (spline == nullptr)
        {
            WriteError(error, "A valid Ts spline is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const openusd_status validation = ValidateTsView(view, error);
        if (validation != OPENUSD_STATUS_OK)
        {
            return validation;
        }
        TsSpline next(TfType::Find<double>());
        next.SetCurveType(ToCurve(view->curve_type));
        next.SetTimeValued(view->is_time_valued != 0);
        next.SetPreExtrapolation(TsExtrapolation(ToExtrap(view->pre_extrapolation.mode), view->pre_extrapolation.slope));
        next.SetPostExtrapolation(TsExtrapolation(ToExtrap(view->post_extrapolation.mode), view->post_extrapolation.slope));
        for (size_t index = 0; index < view->knot_count; ++index)
        {
            TsKnot knot = ToKnot(view->knots[index]);
            if (!next.SetKnot(knot))
            {
                WriteError(error, "A Ts knot could not be set on the spline.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
        }
        spline->value = next;
        spline->snapshot.clear();
        return OPENUSD_STATUS_OK;
    });
}

openusd_status openusd_ts_spline_get_data(
    const openusd_ts_spline* spline,
    openusd_ts_spline_data_view* view,
    openusd_error_buffer* error)
{
    return Guard(error, [&]() -> openusd_status
    {
        if (spline == nullptr || view == nullptr || !IsAligned(view) ||
            view->struct_size < sizeof(openusd_ts_spline_data_view) ||
            view->version != OPENUSD_TS_SPLINE_DATA_VIEW_VERSION)
        {
            ResetTsView(view);
            WriteError(error, "A valid Ts spline and data view version 1 are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        openusd_ts_spline* mutableSpline = const_cast<openusd_ts_spline*>(spline);
        mutableSpline->snapshot.clear();
        const TsKnotMap knots = spline->value.GetKnots();
        mutableSpline->snapshot.reserve(knots.size());
        for (const TsKnot& knot : knots)
        {
            mutableSpline->snapshot.push_back(FromKnot(knot));
        }
        const uint32_t structSize = view->struct_size;
        std::memset(view, 0, sizeof(*view));
        view->struct_size = structSize;
        view->version = OPENUSD_TS_SPLINE_DATA_VIEW_VERSION;
        view->curve_type = static_cast<int32_t>(spline->value.GetCurveType());
        view->is_time_valued = spline->value.IsTimeValued() ? 1 : 0;
        const TsExtrapolation pre = spline->value.GetPreExtrapolation();
        view->pre_extrapolation.mode = static_cast<int32_t>(pre.mode);
        view->pre_extrapolation.slope = pre.slope;
        const TsExtrapolation post = spline->value.GetPostExtrapolation();
        view->post_extrapolation.mode = static_cast<int32_t>(post.mode);
        view->post_extrapolation.slope = post.slope;
        view->knots = mutableSpline->snapshot.empty() ? nullptr : mutableSpline->snapshot.data();
        view->knots_size = mutableSpline->snapshot.size() * sizeof(openusd_ts_knot_record);
        view->knot_count = mutableSpline->snapshot.size();
        return OPENUSD_STATUS_OK;
    });
}

openusd_status openusd_ts_spline_eval(
    const openusd_ts_spline* spline,
    double time,
    double* value,
    int32_t* has_value,
    openusd_error_buffer* error)
{
    return Guard(error, [&]() -> openusd_status
    {
        if (value != nullptr)
        {
            *value = 0.0;
        }
        if (has_value != nullptr)
        {
            *has_value = 0;
        }
        if (spline == nullptr || value == nullptr || has_value == nullptr || !std::isfinite(time))
        {
            WriteError(error, "A valid Ts spline, finite time, value output, and has-value output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        double evaluated = 0.0;
        if (spline->value.Eval(time, &evaluated))
        {
            *value = evaluated;
            *has_value = 1;
        }
        return OPENUSD_STATUS_OK;
    });
}
