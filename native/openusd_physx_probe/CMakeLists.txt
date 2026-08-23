set(openusd_physx_root ${CMAKE_CURRENT_SOURCE_DIR}/../openusd_physx)

# Every MSVC target here compiles at /W4 /WX, which turns C4530 into an error the
# moment a translation unit uses try/catch or instantiates a standard container
# that can throw. The probes use both, and they link no import target that would
# hand them the standard C++ unwind model, so they ask for it themselves rather
# than relying on whatever default flags the surrounding build happens to set.
# The warning is never suppressed; the unwind semantics are actually enabled.
if(MSVC)
    set(openusd_physx_probe_msvc_options /W4 /WX /permissive- /EHsc)
endif()

# The page contract, the identity rules, and the argument rules of the retained
# world ABI are implemented in translation units that never include PhysX, so
# this probe compiles and runs them without the simulation SDK.
add_executable(openusd_physx_contract_probe
    contract_main.cpp
    page_builder.h
    ${openusd_physx_root}/src/openusd_physx_page.cpp
    ${openusd_physx_root}/src/openusd_physx_support.cpp)
target_compile_features(openusd_physx_contract_probe PRIVATE cxx_std_17)
target_compile_definitions(openusd_physx_contract_probe PRIVATE OPENUSD_PHYSX_BUILD)
target_include_directories(openusd_physx_contract_probe
    PRIVATE
        ${openusd_physx_root}/include
        ${openusd_physx_root}/src)
if(TARGET openusd_native_sanitizers)
    target_link_libraries(openusd_physx_contract_probe PRIVATE openusd_native_sanitizers)
endif()
if(MSVC)
    target_compile_options(openusd_physx_contract_probe PRIVATE ${openusd_physx_probe_msvc_options})
else()
    target_compile_options(openusd_physx_contract_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()
add_test(NAME openusd_physx_page_contract COMMAND openusd_physx_contract_probe)

# The deterministic event order, the bounded overflow policy, the runtime command
# rules, and the batched query rules also live in a PhysX free translation unit,
# so they are proven without the simulation SDK as well.
add_executable(openusd_physx_events_probe
    events_main.cpp
    ${openusd_physx_root}/src/openusd_physx_events.cpp
    ${openusd_physx_root}/src/openusd_physx_support.cpp)
target_compile_features(openusd_physx_events_probe PRIVATE cxx_std_17)
target_compile_definitions(openusd_physx_events_probe PRIVATE OPENUSD_PHYSX_BUILD)
target_include_directories(openusd_physx_events_probe
    PRIVATE
        ${openusd_physx_root}/include
        ${openusd_physx_root}/src)
if(TARGET openusd_native_sanitizers)
    target_link_libraries(openusd_physx_events_probe PRIVATE openusd_native_sanitizers)
endif()
if(MSVC)
    target_compile_options(openusd_physx_events_probe PRIVATE ${openusd_physx_probe_msvc_options})
else()
    target_compile_options(openusd_physx_events_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()
add_test(NAME openusd_physx_event_contract COMMAND openusd_physx_events_probe)

if(NOT TARGET openusd_physx)
    return()
endif()

if(WIN32)
    set(openusd_physx_probe_path
        "PATH=path_list_prepend:$<TARGET_FILE_DIR:openusd_physx>"
        "PATH=path_list_prepend:$ENV{OPENUSD_ROOT}/lib"
        "PATH=path_list_prepend:$ENV{OPENUSD_ROOT}/bin"
        "PATH=path_list_prepend:$ENV{VULKAN_SDK}/Bin")
endif()

add_executable(openusd_physx_probe main.cpp)
target_compile_features(openusd_physx_probe PRIVATE cxx_std_17)
target_link_libraries(openusd_physx_probe PRIVATE openusd_physx)
if(MSVC)
    target_compile_options(openusd_physx_probe PRIVATE ${openusd_physx_probe_msvc_options})
else()
    target_compile_options(openusd_physx_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

add_test(NAME openusd_physx_abi_probe COMMAND openusd_physx_probe)
if(WIN32)
    set_tests_properties(
        openusd_physx_abi_probe
        PROPERTIES
            ENVIRONMENT_MODIFICATION "${openusd_physx_probe_path}")
endif()

# Exercises the retained world ABI end to end against the real simulation SDK.
add_executable(openusd_physx_world_probe world_main.cpp page_builder.h)
target_compile_features(openusd_physx_world_probe PRIVATE cxx_std_17)
target_link_libraries(openusd_physx_world_probe PRIVATE openusd_physx)
if(MSVC)
    target_compile_options(openusd_physx_world_probe PRIVATE ${openusd_physx_probe_msvc_options})
else()
    target_compile_options(openusd_physx_world_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

add_test(NAME openusd_physx_world_probe COMMAND openusd_physx_world_probe)
if(WIN32)
    set_tests_properties(
        openusd_physx_world_probe
        PROPERTIES
            ENVIRONMENT_MODIFICATION "${openusd_physx_probe_path}")
endif()

# Exercises batched events, runtime force and impulse commands, and batched
# scene queries end to end against the real simulation SDK.
add_executable(openusd_physx_events_world_probe events_world_main.cpp page_builder.h)
target_compile_features(openusd_physx_events_world_probe PRIVATE cxx_std_17)
target_link_libraries(openusd_physx_events_world_probe PRIVATE openusd_physx)
find_package(Threads REQUIRED)
target_link_libraries(openusd_physx_events_world_probe PRIVATE Threads::Threads)
if(MSVC)
    target_compile_options(openusd_physx_events_world_probe PRIVATE ${openusd_physx_probe_msvc_options})
else()
    target_compile_options(openusd_physx_events_world_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

add_test(NAME openusd_physx_events_world_probe COMMAND openusd_physx_events_world_probe)
if(WIN32)
    set_tests_properties(
        openusd_physx_events_world_probe
        PROPERTIES
            ENVIRONMENT_MODIFICATION "${openusd_physx_probe_path}")
endif()

# Drives two retained worlds and the legacy scene ABI from several threads at
# once so the shared runtime, the shared factory lock, and the per thread error
# storage are exercised concurrently.
add_executable(openusd_physx_concurrency_probe concurrency_main.cpp page_builder.h)
target_compile_features(openusd_physx_concurrency_probe PRIVATE cxx_std_17)
target_link_libraries(openusd_physx_concurrency_probe PRIVATE openusd_physx)
find_package(Threads REQUIRED)
target_link_libraries(openusd_physx_concurrency_probe PRIVATE Threads::Threads)
if(MSVC)
    target_compile_options(openusd_physx_concurrency_probe PRIVATE ${openusd_physx_probe_msvc_options})
else()
    target_compile_options(openusd_physx_concurrency_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

add_test(NAME openusd_physx_concurrency_probe COMMAND openusd_physx_concurrency_probe)
if(WIN32)
    set_tests_properties(
        openusd_physx_concurrency_probe
        PROPERTIES
            ENVIRONMENT_MODIFICATION "${openusd_physx_probe_path}")
endif()

# Exercises the collision, rigid body, and joint domains the version 4 ABI adds
# against the real simulation SDK: analytic cylinder and cone geometry, scaled
# height fields, per body velocity budgets and axis locks, and per axis six
# degree of freedom limits and drives.
add_executable(openusd_physx_cpu_domains_probe cpu_domains_main.cpp page_builder.h)
target_compile_features(openusd_physx_cpu_domains_probe PRIVATE cxx_std_17)
target_link_libraries(openusd_physx_cpu_domains_probe PRIVATE openusd_physx)
if(MSVC)
    target_compile_options(openusd_physx_cpu_domains_probe PRIVATE ${openusd_physx_probe_msvc_options})
else()
    target_compile_options(openusd_physx_cpu_domains_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

add_test(NAME openusd_physx_cpu_domains_probe COMMAND openusd_physx_cpu_domains_probe)
if(WIN32)
    set_tests_properties(
        openusd_physx_cpu_domains_probe
        PROPERTIES
            ENVIRONMENT_MODIFICATION "${openusd_physx_probe_path}")
endif()

# Exercises the articulation coupling constructs the version 6 ABI adds against
# the real simulation SDK: fixed tendons, spatial tendons, and mimic joints, each
# compared against an identical uncoupled control chain in the same world.
add_executable(openusd_physx_coupling_probe coupling_main.cpp page_builder.h)
target_compile_features(openusd_physx_coupling_probe PRIVATE cxx_std_17)
target_link_libraries(openusd_physx_coupling_probe PRIVATE openusd_physx)
# The failure injection section measures resident memory so a leaked articulation
# on the refused build path cannot pass unnoticed.
if(WIN32)
    target_link_libraries(openusd_physx_coupling_probe PRIVATE psapi)
endif()
if(MSVC)
    target_compile_options(openusd_physx_coupling_probe PRIVATE ${openusd_physx_probe_msvc_options})
else()
    target_compile_options(openusd_physx_coupling_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

add_test(NAME openusd_physx_coupling_probe COMMAND openusd_physx_coupling_probe)
if(WIN32)
    set_tests_properties(
        openusd_physx_coupling_probe
        PROPERTIES
            ENVIRONMENT_MODIFICATION "${openusd_physx_probe_path}")
endif()

# Drives a real four wheeled engine drive vehicle against the real simulation
# SDK: it must accelerate, shift through its autobox, steer off its line, and
# brake, which is what makes the vehicle capability honest.
add_executable(openusd_physx_vehicle_probe vehicle_main.cpp page_builder.h)
target_compile_features(openusd_physx_vehicle_probe PRIVATE cxx_std_17)
target_link_libraries(openusd_physx_vehicle_probe PRIVATE openusd_physx)
if(MSVC)
    target_compile_options(openusd_physx_vehicle_probe PRIVATE ${openusd_physx_probe_msvc_options})
else()
    target_compile_options(openusd_physx_vehicle_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

add_test(NAME openusd_physx_vehicle_probe COMMAND openusd_physx_vehicle_probe)
if(WIN32)
    set_tests_properties(
        openusd_physx_vehicle_probe
        PROPERTIES
            ENVIRONMENT_MODIFICATION "${openusd_physx_probe_path}")
endif()

# Drives the optional CUDA accelerated domains against the real simulation SDK.
# The probe asserts one contract on both kinds of machine: with a device every
# particle system, surface, and volume must build, publish, and actually deform;
# without one every GPU object must be skipped individually while the rigid
# bodies of the same build keep simulating.
add_executable(openusd_physx_gpu_domains_probe gpu_domains_main.cpp page_builder.h)
target_compile_features(openusd_physx_gpu_domains_probe PRIVATE cxx_std_17)
target_link_libraries(openusd_physx_gpu_domains_probe PRIVATE openusd_physx)
if(MSVC)
    target_compile_options(openusd_physx_gpu_domains_probe PRIVATE ${openusd_physx_probe_msvc_options})
else()
    target_compile_options(openusd_physx_gpu_domains_probe PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

add_test(NAME openusd_physx_gpu_domains_probe COMMAND openusd_physx_gpu_domains_probe)
if(WIN32)
    set_tests_properties(
        openusd_physx_gpu_domains_probe
        PROPERTIES
            ENVIRONMENT_MODIFICATION "${openusd_physx_probe_path}")
endif()
