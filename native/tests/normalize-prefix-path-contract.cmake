# Copyright (c) marcschier. Licensed under the MIT License.
#
# Contract test for openusd_normalize_prefix_path, run through "cmake -P" so it
# exercises the same code the configure step uses.
#
# The cases that matter are the ones a blunt implementation gets wrong. Removing
# every quote character in the string -- which is what the first version did --
# passes the padded and quoted cases and silently corrupts any prefix that
# contains an apostrophe or a double quote of its own, turning a real directory
# into one that does not exist and reproducing the modal loader hang this
# normalization was written to prevent.

include("${CMAKE_CURRENT_LIST_DIR}/../cmake/OpenUsdNormalizePrefixPath.cmake")

set(_failures 0)

function(expect_normalized input expected label)
    set(_actual "${input}")
    openusd_normalize_prefix_path(_actual)
    if(NOT _actual STREQUAL expected)
        message(SEND_ERROR
            "${label}: normalized '${input}' to '${_actual}', expected '${expected}'")
    endif()
endfunction()

# Padding alone, which is the failure that hung the probe.
expect_normalized("D:/prefix/win-x64 " "D:/prefix/win-x64" "trailing space")
expect_normalized("  D:/prefix/win-x64" "D:/prefix/win-x64" "leading space")
expect_normalized("\t D:/prefix/win-x64 \t" "D:/prefix/win-x64" "tabs")

# Exactly one matching pair, with padding inside and outside it.
expect_normalized("'D:/prefix/win-x64 '" "D:/prefix/win-x64" "single quoted")
expect_normalized("\"D:/prefix/win-x64\"" "D:/prefix/win-x64" "double quoted")
expect_normalized(" 'D:/prefix/win-x64' " "D:/prefix/win-x64" "padded quoted")

# One pair and no more: a prefix that is itself quoted twice keeps the inner
# pair, because only the outer one was quoting.
expect_normalized("''D:/prefix''" "'D:/prefix'" "nested quotes")

# Embedded quote characters are part of the path and must survive. A directory
# called "Ann's Files" is ordinary on every platform, and a POSIX path may
# contain a double quote.
expect_normalized(
    "/home/ann/Ann's Files/usd"
    "/home/ann/Ann's Files/usd"
    "embedded apostrophe")
expect_normalized(
    "'/home/ann/Ann's Files/usd'"
    "/home/ann/Ann's Files/usd"
    "quoted embedded apostrophe")
expect_normalized("/home/ann/a\"b/usd" "/home/ann/a\"b/usd" "embedded double quote")
expect_normalized(
    "\"/home/ann/a\"b/usd\""
    "/home/ann/a\"b/usd"
    "quoted embedded double quote")

# A path that merely begins or ends with a quote is not a quoted path, and a
# mismatched pair is not one either.
expect_normalized("'/home/ann/usd" "'/home/ann/usd" "unterminated quote")
expect_normalized("/home/ann/usd'" "/home/ann/usd'" "trailing apostrophe only")
expect_normalized("'/home/ann/usd\"" "'/home/ann/usd\"" "mismatched pair")

# Degenerate inputs must not be mangled into something else.
expect_normalized("" "" "empty")
expect_normalized("'" "'" "single character")
expect_normalized("''" "" "empty quoted")

message(STATUS "openusd_normalize_prefix_path contract satisfied.")
