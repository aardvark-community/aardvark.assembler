cmake_minimum_required(VERSION 3.9)
include("cmake/Aardvark.cmake")

project(jitmem)

set(CMAKE_BUILD_TYPE, "Release")
if(UNIX)
    set(CMAKE_C_FLAGS "${CMAKE_C_FLAGS} -O3 -ffunction-sections -fdata-sections -fvisibility=hidden")
    set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} -O3 -ffunction-sections -fdata-sections -fvisibility=hidden")
endif()



set(CMAKE_CXX_STANDARD 14)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
if(APPLE)
    set(CMAKE_SHARED_LINKER_FLAGS "-O3 -flto=thin -dead_strip")
elseif(UNIX)
    set(CMAKE_SHARED_LINKER_FLAGS "-O3 -Wl,--gc-sections -Wl,--strip-all")
endif()

add_library(jitmem SHARED Executable.h Executable.cpp)
target_link_libraries(jitmem pthread)

SET(CMAKE_INSTALL_PREFIX "${CMAKE_CURRENT_SOURCE_DIR}/../../libs/Native/Arm64/") 
install(TARGETS jitmem DESTINATION ${OS}/${ARCH})