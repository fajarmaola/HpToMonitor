cmake_minimum_required(VERSION 3.20)
project(SecondScreen.Native LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(SecondScreen.Native SHARED
    src/NativeApi.cpp
    src/DxgiCapture.cpp
    src/MediaFoundationH264Encoder.cpp
    src/SwDevice.cpp
)

target_include_directories(SecondScreen.Native PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/include
    ${CMAKE_CURRENT_SOURCE_DIR}/../../shared/protocol/cpp
)

# Windows media / graphics libraries.
target_link_libraries(SecondScreen.Native PRIVATE
    d3d11 dxgi dxguid
    mf mfplat mfuuid mfreadwrite
    # cfgmgr32 is used by the SwDevice virtual-display path.
    cfgmgr32
)

# Export the C ABI without name mangling/decoration mismatches.
set_target_properties(SecondScreen.Native PROPERTIES
    OUTPUT_NAME "SecondScreen.Native"
    PREFIX ""
)

if (MSVC)
    target_compile_definitions(SecondScreen.Native PRIVATE UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN)
endif()
