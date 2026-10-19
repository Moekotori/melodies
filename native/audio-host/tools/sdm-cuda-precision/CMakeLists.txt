cmake_minimum_required(VERSION 3.24)
project(echo_sdm_cuda_precision LANGUAGES CXX)
find_package(CUDAToolkit REQUIRED)
find_package(Threads REQUIRED)
set(ECHO_CUDA_DSP_LIBRARY "" CACHE FILEPATH "Already built current-source echo-cuda-dsp library")
if(NOT EXISTS "${ECHO_CUDA_DSP_LIBRARY}")
  message(FATAL_ERROR "Build echo-cuda-dsp and set ECHO_CUDA_DSP_LIBRARY")
endif()
add_executable(echo-sdm-cuda-precision main.cpp ../sdm-realtime-lab/RoutePlan.cpp
  ../sdm-analysis-lab/SdmSpectrumAnalysis.cpp
  ../../../audio-engine/NativeFormatProcessor.cpp ../../../audio-engine/SdmNtfDesign.cpp
  ../../../audio-engine/SdmPreModulator.cpp)
target_compile_features(echo-sdm-cuda-precision PRIVATE cxx_std_20)
target_include_directories(echo-sdm-cuda-precision PRIVATE ../../src ../../../audio-engine)
target_link_libraries(echo-sdm-cuda-precision PRIVATE "${ECHO_CUDA_DSP_LIBRARY}" CUDA::cudart_static Threads::Threads)
if(MSVC)
  set_property(TARGET echo-sdm-cuda-precision PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
  target_compile_options(echo-sdm-cuda-precision PRIVATE /W4 /utf-8)
endif()
