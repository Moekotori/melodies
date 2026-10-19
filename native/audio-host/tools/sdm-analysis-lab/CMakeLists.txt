cmake_minimum_required(VERSION 3.24)

project(echo_sdm_analysis_lab VERSION 0.1.0 LANGUAGES CXX)
find_package(Threads REQUIRED)
enable_testing()

add_executable(echo-sdm-analysis-lab
  main.cpp SdmAnalysisLab.cpp SdmSpectrumAnalysis.cpp
  ../../../audio-engine/NativeFormatProcessor.cpp
  ../../../audio-engine/SdmNtfDesign.cpp
  ../../../audio-engine/SdmPreModulator.cpp)
target_include_directories(echo-sdm-analysis-lab PRIVATE ../../../audio-engine)
target_link_libraries(echo-sdm-analysis-lab PRIVATE Threads::Threads)

# Focused sound A/B targets are opt-in and do not increase the default lab
# build or alter playback defaults. They compile the actual product algorithm.
foreach(variant baseline envelope)
  set(target echo-sdm-sound-focus-${variant})
  add_executable(${target} EXCLUDE_FROM_ALL
    SdmSoundFocusLab.cpp SdmSoundCapture.cpp SdmSpectrumAnalysis.cpp ../sdm-realtime-lab/RoutePlan.cpp
    ../../../audio-engine/NativeFormatProcessor.cpp
    ../../../audio-engine/SdmNtfDesign.cpp ../../../audio-engine/SdmPreModulator.cpp)
  target_include_directories(${target} PRIVATE ../../../audio-engine)
  target_compile_features(${target} PRIVATE cxx_std_20)
  target_link_libraries(${target} PRIVATE Threads::Threads)
  if(variant STREQUAL "envelope")
    target_compile_definitions(${target} PRIVATE ECHO_SDM_EC_SUPER_ENVELOPE_GATED=1)
  else()
    target_compile_definitions(${target} PRIVATE ECHO_SDM_EC_SUPER_ENVELOPE_GATED=0)
  endif()
  if(MSVC)
    set_property(TARGET ${target} PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    target_compile_options(${target} PRIVATE /W4 /permissive- /utf-8)
  else()
    target_compile_options(${target} PRIVATE -Wall -Wextra -Wpedantic)
  endif()
endforeach()

add_executable(echo-sdm-spectrum-tests
  SdmSpectrumAnalysisTests.cpp SdmSpectrumAnalysis.cpp)
add_test(NAME echo-sdm-spectrum-tests COMMAND echo-sdm-spectrum-tests)

foreach(target echo-sdm-analysis-lab echo-sdm-spectrum-tests)
  target_compile_features(${target} PRIVATE cxx_std_20)
  if(MSVC)
    set_property(TARGET ${target} PROPERTY
      MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    target_compile_options(${target} PRIVATE /W4 /permissive- /utf-8)
  else()
    target_compile_options(${target} PRIVATE -Wall -Wextra -Wpedantic)
  endif()
endforeach()
