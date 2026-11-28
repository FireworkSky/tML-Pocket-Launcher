cmake_minimum_required(VERSION 3.22.1)
project(main LANGUAGES C CXX)

if(TML_MOBILE_TERRARIA_CONVERTER)
    add_subdirectory(mobile-terraria)
endif()

set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

set(LIBS_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../external")
set(DOTNETHOST_DIR "${CMAKE_CURRENT_SOURCE_DIR}/dotnethost")

include(FetchContent)
find_package(Git REQUIRED)
# Backport libsdl-org/SDL#13016 with the property names consumed by FNA3D.
set(SDL_VULKAN_PATCH "${CMAKE_CURRENT_SOURCE_DIR}/cmake/sdl-vulkan-optional-features.patch")
set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${SDL_VULKAN_PATCH}")
FetchContent_Declare(
    SDL3
    URL https://codeload.github.com/RotatingArtDev/SDL/zip/8cddfc209463f76072304c0425024fed47c69361
    URL_HASH SHA256=0eb11d4e57e48d4fdf595849f80b5a669369e3c1c6f1235546c67cc366f734db
    PATCH_COMMAND "${GIT_EXECUTABLE}" --git-dir=.unused-git-dir apply --ignore-space-change "${SDL_VULKAN_PATCH}"
)
if(DEFINED ENV{TML_SDL3_SOURCE_DIR} AND NOT "$ENV{TML_SDL3_SOURCE_DIR}" STREQUAL "")
    if(NOT EXISTS "$ENV{TML_SDL3_SOURCE_DIR}/CMakeLists.txt")
        message(FATAL_ERROR "TML_SDL3_SOURCE_DIR must point to an SDL source tree")
    endif()
    set(FETCHCONTENT_SOURCE_DIR_SDL3 "$ENV{TML_SDL3_SOURCE_DIR}" CACHE PATH "Local SDL3 source override" FORCE)
else()
    # An old local override must not silently replace the pinned source on later builds.
    unset(FETCHCONTENT_SOURCE_DIR_SDL3 CACHE)
endif()
set(SDL_SHARED ON CACHE BOOL "" FORCE)
set(SDL_STATIC OFF CACHE BOOL "" FORCE)
set(SDL_TESTS OFF CACHE BOOL "" FORCE)
set(SDL_GPU ON CACHE BOOL "" FORCE)
set(SDL_VULKAN ON CACHE BOOL "" FORCE)
set(SDL_VIDEO_OPENGL ON CACHE BOOL "" FORCE)
set(SDL_VIDEO_OPENGL_ES ON CACHE BOOL "" FORCE)
set(SDL_VIDEO_OPENGL_EGL ON CACHE BOOL "" FORCE)
set(SDL_OPENGLES ON CACHE BOOL "" FORCE)
FetchContent_MakeAvailable(SDL3)

set(SDL_JNI_CHECK "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/checks/check-sdl-jni.ps1")
file(GLOB SDL_JAVA_BINDINGS CONFIGURE_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/../app/src/main/java/org/libsdl/app/*.java")
set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS
    "${SDL_JNI_CHECK}"
    "${SDL3_SOURCE_DIR}/src/core/android/SDL_android.c"
    "${SDL3_SOURCE_DIR}/src/hidapi/android/hid.cpp"
    "${SDL3_SOURCE_DIR}/include/SDL3/SDL_version.h"
    ${SDL_JAVA_BINDINGS}
)
execute_process(
    COMMAND powershell -NoProfile -ExecutionPolicy Bypass -File "${SDL_JNI_CHECK}" -SdlSourcePath "${SDL3_SOURCE_DIR}"
    RESULT_VARIABLE SDL_JNI_CHECK_RESULT
    OUTPUT_VARIABLE SDL_JNI_CHECK_OUTPUT
    ERROR_VARIABLE SDL_JNI_CHECK_ERROR
)
if(NOT SDL_JNI_CHECK_RESULT EQUAL 0)
    message(FATAL_ERROR "SDL JNI compatibility check failed:\n${SDL_JNI_CHECK_OUTPUT}${SDL_JNI_CHECK_ERROR}")
endif()
message(STATUS "SDL source: ${SDL3_SOURCE_DIR}")
message(STATUS "${SDL_JNI_CHECK_OUTPUT}")

set(SDL3_INCLUDE_DIRS "${SDL3_SOURCE_DIR}/include")
set(SDL3_LIBRARIES SDL3-shared)

include(cmake/Sdl2CompatOptions.cmake)
include(cmake/FaudioOptions.cmake)
include(cmake/Fna3dOptions.cmake)

# Keep debug symbols while benchmarking the graphics stack at release optimization.
foreach(graphics_target SDL3-shared SDL2 FNA3D mojoshader)
    target_compile_options(${graphics_target} PRIVATE "$<$<CONFIG:Debug>:-O2>")
endforeach()

add_subdirectory("${DOTNETHOST_DIR}" "${CMAKE_BINARY_DIR}/dotnethost")

add_library(main SHARED
    src/android_bridge.cpp
    src/jni_entry.cpp
    src/logger.cpp
    src/dotnet/dotnet_launcher.cpp
    src/sdl/sdl_entry.cpp
)

find_library(ANDROID_LOG_LIB log REQUIRED)
find_library(ANDROID_LIB android REQUIRED)
find_library(CXX_SHARED_LIB c++_shared REQUIRED)

target_include_directories(main PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}/include"
    "${LIBS_DIR}/SDL/include"
    "${LIBS_DIR}/FNA3D/include"
    "${LIBS_DIR}/FAudio/include"
    "${DOTNETHOST_DIR}/include"
)

target_link_libraries(main PRIVATE
    dotnethost
    SDL3-shared
    ${ANDROID_LOG_LIB}
    ${ANDROID_LIB}
    ${CXX_SHARED_LIB}
)

# The stores process also loads main, before any game-library preloads. Keep the
# game's libraries as build dependencies, not DT_NEEDED entries on libmain.
add_dependencies(main FAudio-shared FNA3D SDL2)

set_target_properties(FNA3D PROPERTIES OUTPUT_NAME "FNA3D_sdl3" NO_SONAME TRUE)
target_link_options(FNA3D PRIVATE "-Wl,-soname,libFNA3D.so")
set_target_properties(FAudio-shared PROPERTIES OUTPUT_NAME "FAudio_sdl3" NO_SONAME TRUE)
target_link_options(FAudio-shared PRIVATE "-Wl,-soname,libFAudio.so")

set_target_properties(SDL2 PROPERTIES OUTPUT_NAME "SDL2_compat" NO_SONAME TRUE)
target_link_options(SDL2 PRIVATE "-Wl,-soname,libSDL2.so")
