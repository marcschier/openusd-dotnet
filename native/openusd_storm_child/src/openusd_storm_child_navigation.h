// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_STORM_CHILD_NAVIGATION_H
#define OPENUSD_STORM_CHILD_NAVIGATION_H

#include "openusd_storm_child.h"

#include <cmath>
#include <cstdint>
#include <cstring>
#include <mutex>

constexpr uint32_t OPENUSD_STORM_CHILD_COMMAND_KEY_FRAME_SELECTED = 0x1u;
constexpr uint32_t OPENUSD_STORM_CHILD_COMMAND_KEY_RESET_AUTOMATIC = 0x2u;
constexpr uint32_t OPENUSD_STORM_CHILD_COMMAND_KEY_TOGGLE_PROJECTION = 0x4u;
constexpr uint32_t OPENUSD_STORM_CHILD_COMMAND_KEY_ORBIT_LEFT = 0x8u;
constexpr uint32_t OPENUSD_STORM_CHILD_COMMAND_KEY_ORBIT_RIGHT = 0x10u;
constexpr uint32_t OPENUSD_STORM_CHILD_COMMAND_KEY_ORBIT_UP = 0x20u;
constexpr uint32_t OPENUSD_STORM_CHILD_COMMAND_KEY_ORBIT_DOWN = 0x40u;

struct OpenUsdStormChildNavigationState
{
    std::mutex gate;
    uint64_t sequence = 0;
    int32_t pointer_x = 0;
    int32_t pointer_y = 0;
    uint32_t buttons = OPENUSD_STORM_CHILD_POINTER_BUTTON_NONE;
    uint32_t modifiers = OPENUSD_STORM_CHILD_MODIFIER_NONE;
    double cumulative_wheel_delta = 0;
    uint64_t frame_selected_press_count = 0;
    uint64_t reset_automatic_press_count = 0;
    uint64_t toggle_projection_press_count = 0;
    uint64_t orbit_left_press_count = 0;
    uint64_t orbit_right_press_count = 0;
    uint64_t orbit_up_press_count = 0;
    uint64_t orbit_down_press_count = 0;
    uint32_t command_keys_down = 0;
    uint32_t state = OPENUSD_STORM_CHILD_NAVIGATION_STATE_NONE;
};

inline void OpenUsdStormChildNavigationAdvance(
    OpenUsdStormChildNavigationState* navigation) noexcept
{
    ++navigation->sequence;
}

inline void OpenUsdStormChildNavigationSetFocus(
    OpenUsdStormChildNavigationState* navigation,
    bool focused)
{
    std::lock_guard lock(navigation->gate);
    if (focused)
    {
        navigation->state |= OPENUSD_STORM_CHILD_NAVIGATION_STATE_FOCUSED;
    }
    else
    {
        navigation->state &= ~OPENUSD_STORM_CHILD_NAVIGATION_STATE_FOCUSED;
        navigation->buttons = OPENUSD_STORM_CHILD_POINTER_BUTTON_NONE;
        navigation->modifiers = OPENUSD_STORM_CHILD_MODIFIER_NONE;
        navigation->command_keys_down = 0;
    }
    OpenUsdStormChildNavigationAdvance(navigation);
}

inline void OpenUsdStormChildNavigationSetInside(
    OpenUsdStormChildNavigationState* navigation,
    bool inside)
{
    std::lock_guard lock(navigation->gate);
    if (inside)
    {
        navigation->state |= OPENUSD_STORM_CHILD_NAVIGATION_STATE_INSIDE;
    }
    else
    {
        navigation->state &= ~OPENUSD_STORM_CHILD_NAVIGATION_STATE_INSIDE;
    }
    OpenUsdStormChildNavigationAdvance(navigation);
}

inline void OpenUsdStormChildNavigationUpdatePointer(
    OpenUsdStormChildNavigationState* navigation,
    int32_t pointer_x,
    int32_t pointer_y,
    uint32_t buttons,
    uint32_t modifiers)
{
    std::lock_guard lock(navigation->gate);
    navigation->pointer_x = pointer_x;
    navigation->pointer_y = pointer_y;
    navigation->buttons = buttons;
    navigation->modifiers = modifiers;
    navigation->state |= OPENUSD_STORM_CHILD_NAVIGATION_STATE_INSIDE;
    OpenUsdStormChildNavigationAdvance(navigation);
}

inline void OpenUsdStormChildNavigationUpdateModifiers(
    OpenUsdStormChildNavigationState* navigation,
    uint32_t modifiers)
{
    std::lock_guard lock(navigation->gate);
    navigation->modifiers = modifiers;
    OpenUsdStormChildNavigationAdvance(navigation);
}

inline uint32_t OpenUsdStormChildNavigationGetModifiers(
    OpenUsdStormChildNavigationState* navigation)
{
    std::lock_guard lock(navigation->gate);
    return navigation->modifiers;
}

inline void OpenUsdStormChildNavigationResetButtons(
    OpenUsdStormChildNavigationState* navigation)
{
    std::lock_guard lock(navigation->gate);
    navigation->buttons = OPENUSD_STORM_CHILD_POINTER_BUTTON_NONE;
    OpenUsdStormChildNavigationAdvance(navigation);
}

inline void OpenUsdStormChildNavigationAddWheel(
    OpenUsdStormChildNavigationState* navigation,
    double delta,
    int32_t pointer_x,
    int32_t pointer_y,
    uint32_t buttons,
    uint32_t modifiers)
{
    std::lock_guard lock(navigation->gate);
    navigation->pointer_x = pointer_x;
    navigation->pointer_y = pointer_y;
    navigation->buttons = buttons;
    navigation->modifiers = modifiers;
    navigation->state |= OPENUSD_STORM_CHILD_NAVIGATION_STATE_INSIDE;
    if (std::isfinite(delta))
    {
        navigation->cumulative_wheel_delta += delta;
    }
    OpenUsdStormChildNavigationAdvance(navigation);
}

inline void OpenUsdStormChildNavigationUpdateCommandKeyLocked(
    OpenUsdStormChildNavigationState* navigation,
    uint32_t key,
    bool pressed,
    bool repeat,
    bool count_repeats,
    uint64_t OpenUsdStormChildNavigationState::* counter)
{
    const bool was_pressed = (navigation->command_keys_down & key) != 0;
    if (pressed)
    {
        navigation->command_keys_down |= key;
        if (((!repeat && !was_pressed) || (repeat && count_repeats)) &&
            navigation->modifiers == OPENUSD_STORM_CHILD_MODIFIER_NONE)
        {
            ++(navigation->*counter);
        }
    }
    else
    {
        navigation->command_keys_down &= ~key;
    }
}

inline bool OpenUsdStormChildPrepareNavigationOutput(
    openusd_storm_child_navigation_input* input,
    uint32_t* requested_size,
    uint32_t* requested_version) noexcept
{
    if (input == nullptr)
    {
        return false;
    }
    *requested_size = input->struct_size;
    *requested_version = input->version;
    std::memset(input, 0, sizeof(*input));
    return *requested_size == sizeof(*input) &&
        *requested_version == OPENUSD_STORM_CHILD_NAVIGATION_INPUT_VERSION;
}

inline void OpenUsdStormChildCopyNavigationInput(
    OpenUsdStormChildNavigationState* navigation,
    openusd_storm_child_navigation_input* input)
{
    std::lock_guard lock(navigation->gate);
    input->struct_size = sizeof(*input);
    input->version = OPENUSD_STORM_CHILD_NAVIGATION_INPUT_VERSION;
    input->sequence = navigation->sequence;
    input->pointer_x = navigation->pointer_x;
    input->pointer_y = navigation->pointer_y;
    input->buttons = navigation->buttons;
    input->modifiers = navigation->modifiers;
    input->cumulative_wheel_delta = navigation->cumulative_wheel_delta;
    input->frame_selected_press_count =
        navigation->frame_selected_press_count;
    input->reset_automatic_press_count =
        navigation->reset_automatic_press_count;
    input->toggle_projection_press_count =
        navigation->toggle_projection_press_count;
    input->state = navigation->state;
    input->orbit_left_press_count = navigation->orbit_left_press_count;
    input->orbit_right_press_count = navigation->orbit_right_press_count;
    input->orbit_up_press_count = navigation->orbit_up_press_count;
    input->orbit_down_press_count = navigation->orbit_down_press_count;
}

#endif
