# Copyright (c) marcschier. Licensed under the MIT License.
#
# Normalizes an install prefix that reached CMake with shell quoting or padding
# still attached.
#
# Every path the native probe is given is built by concatenating onto its
# prefix, so one trailing space turns "<prefix>/lib" into a directory that does
# not exist, the probe's dependent libraries are never found, and on Windows the
# loader failure is a modal error box that hangs the test instead of failing it.
# A prefix configured as -DCMAKE_PREFIX_PATH='<path> ' produces exactly that.
#
# The normalization is deliberately narrow. It removes surrounding whitespace,
# and then at most one matching leading/trailing quote pair -- the pair a shell
# would have consumed -- and nothing else. Stripping every quote character in
# the string instead corrupts a legitimate path: a directory may contain an
# apostrophe, and a POSIX filename may contain a double quote, and neither is
# quoting.

function(openusd_normalize_prefix_path prefix_variable)
    set(_prefix "${${prefix_variable}}")
    string(STRIP "${_prefix}" _prefix)
    string(LENGTH "${_prefix}" _prefix_length)
    if(_prefix_length GREATER_EQUAL 2)
        string(SUBSTRING "${_prefix}" 0 1 _prefix_first)
        math(EXPR _prefix_last_index "${_prefix_length} - 1")
        string(SUBSTRING "${_prefix}" ${_prefix_last_index} 1 _prefix_last)
        if(_prefix_first STREQUAL _prefix_last)
            if(_prefix_first STREQUAL "\"" OR _prefix_first STREQUAL "'")
                math(EXPR _prefix_inner_length "${_prefix_length} - 2")
                string(SUBSTRING "${_prefix}" 1 ${_prefix_inner_length} _prefix)
                string(STRIP "${_prefix}" _prefix)
            endif()
        endif()
    endif()
    set(${prefix_variable} "${_prefix}" PARENT_SCOPE)
endfunction()
