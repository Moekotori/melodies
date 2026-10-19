# Regenerate at build time, not just configure time. Only the identity TU
# includes the generated header; unchanged contents do not trigger a rebuild.
set(ECHO_NATIVE_IDENTITY_DIR "${CMAKE_CURRENT_BINARY_DIR}/native-identity")
set(ECHO_NATIVE_IDENTITY_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../..")
set(ECHO_NATIVE_IDENTITY_SCRIPT "${CMAKE_CURRENT_LIST_DIR}/WriteNativeBuildIdentity.cmake")
if(ECHO_ENABLE_CUDA_DSP AND CMAKE_CUDA_COMPILER)
  set(ECHO_NATIVE_CUDA_COMPILED 1)
else()
  set(ECHO_NATIVE_CUDA_COMPILED 0)
endif()
get_property(identity_languages GLOBAL PROPERTY ENABLED_LANGUAGES)
string(REPLACE ";" "\n" identity_languages_text "${identity_languages}")
file(GENERATE OUTPUT "${ECHO_NATIVE_IDENTITY_DIR}/$<CONFIG>/configuration-languages.txt" CONTENT "${identity_languages_text}\n")
# Directory compile options depend on language. Capture each enabled language
# explicitly rather than producing conflicting contents at one output path.
file(GENERATE OUTPUT "${ECHO_NATIVE_IDENTITY_DIR}/$<CONFIG>/configuration-$<COMPILE_LANGUAGE>.txt" CONTENT
"schema=1
configuration=$<CONFIG>
language=$<COMPILE_LANGUAGE>
system=${CMAKE_SYSTEM_NAME}/${CMAKE_SYSTEM_PROCESSOR}/${CMAKE_SIZEOF_VOID_P}
cxx=${CMAKE_CXX_COMPILER_ID}/${CMAKE_CXX_COMPILER_VERSION}
cuda=${CMAKE_CUDA_COMPILER_ID}/${CMAKE_CUDA_COMPILER_VERSION}
windowsSdk=${CMAKE_VS_WINDOWS_TARGET_PLATFORM_VERSION}
cxxFlags=${CMAKE_CXX_FLAGS}
cxxDebug=${CMAKE_CXX_FLAGS_DEBUG}
cxxRelease=${CMAKE_CXX_FLAGS_RELEASE}
cxxRelWithDebInfo=${CMAKE_CXX_FLAGS_RELWITHDEBINFO}
cxxMinSizeRel=${CMAKE_CXX_FLAGS_MINSIZEREL}
cudaFlags=${CMAKE_CUDA_FLAGS}
cudaDebug=${CMAKE_CUDA_FLAGS_DEBUG}
cudaRelease=${CMAKE_CUDA_FLAGS_RELEASE}
cudaRelWithDebInfo=${CMAKE_CUDA_FLAGS_RELWITHDEBINFO}
cudaMinSizeRel=${CMAKE_CUDA_FLAGS_MINSIZEREL}
hostDefinitions=$<TARGET_PROPERTY:echo-audio-host,COMPILE_DEFINITIONS>
hostOptions=$<TARGET_PROPERTY:echo-audio-host,COMPILE_OPTIONS>
hostRuntime=$<TARGET_PROPERTY:echo-audio-host,MSVC_RUNTIME_LIBRARY>
hostLinkOptions=$<TARGET_PROPERTY:echo-audio-host,LINK_OPTIONS>
linkFlags=${CMAKE_EXE_LINKER_FLAGS}
linkRelease=${CMAKE_EXE_LINKER_FLAGS_RELEASE}
cudaDefinitions=$<TARGET_PROPERTY:echo-cuda-dsp,COMPILE_DEFINITIONS>
cudaOptions=$<TARGET_PROPERTY:echo-cuda-dsp,COMPILE_OPTIONS>
cudaArchitectures=$<TARGET_PROPERTY:echo-cuda-dsp,CUDA_ARCHITECTURES>
cudaRuntime=$<TARGET_PROPERTY:echo-cuda-dsp,CUDA_RUNTIME_LIBRARY>
cudaCompiled=${ECHO_NATIVE_CUDA_COMPILED}
asio=${ECHO_ENABLE_ASIO}
ffmpegRoot=${ECHO_FFMPEG_ROOT}
")
add_custom_target(echo-native-build-identity
  COMMAND "${CMAKE_COMMAND}" "-DROOT=${ECHO_NATIVE_IDENTITY_ROOT}"
    "-DOUTPUT_DIR=${ECHO_NATIVE_IDENTITY_DIR}/$<CONFIG>" -DMODE=GENERATE
    -P "${ECHO_NATIVE_IDENTITY_SCRIPT}"
  BYPRODUCTS "${ECHO_NATIVE_IDENTITY_DIR}/$<CONFIG>/NativeBuildIdentity.generated.h"
  VERBATIM)
# Snapshot before either CUDA or host C++ compilation starts.
add_dependencies(echo-cuda-dsp echo-native-build-identity)

function(configure_echo_native_identity target)
  add_dependencies(${target} echo-native-build-identity)
  target_include_directories(${target} PRIVATE "${ECHO_NATIVE_IDENTITY_DIR}/$<CONFIG>")
  target_compile_definitions(${target} PRIVATE ECHO_NATIVE_IMAGE_ROLE="${target}")
  add_custom_command(TARGET ${target} POST_BUILD
    COMMAND "${CMAKE_COMMAND}" "-DROOT=${ECHO_NATIVE_IDENTITY_ROOT}"
      "-DOUTPUT_DIR=${ECHO_NATIVE_IDENTITY_DIR}/$<CONFIG>" -DMODE=VERIFY
      "-DBINARY=$<TARGET_FILE:${target}>" "-DIMAGE_ROLE=${target}"
      -P "${ECHO_NATIVE_IDENTITY_SCRIPT}"
    VERBATIM)
endfunction()
