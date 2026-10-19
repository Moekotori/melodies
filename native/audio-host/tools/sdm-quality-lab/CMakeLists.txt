cmake_minimum_required(VERSION 3.24)

project(echo_sdm_quality_lab VERSION 0.1.0 LANGUAGES CXX)

add_executable(echo-sdm-quality-lab
  main.cpp
  ../sdm-analysis-lab/SdmSpectrumAnalysis.cpp
  ../../../audio-engine/NativeFormatProcessor.cpp
  ../../../audio-engine/SdmNtfDesign.cpp
  ../../../audio-engine/SdmPreModulator.cpp
)

target_compile_features(echo-sdm-quality-lab PRIVATE cxx_std_20)
target_include_directories(echo-sdm-quality-lab PRIVATE ../../../audio-engine)

# Lab-only overrides for the Horizon weight sweep and the extended
# compensation sweep (scripts/run-sdm-horizon-weight-sweep.mjs,
# scripts/run-sdm-ec-sweep.mjs). Unset means release defaults.
foreach(_echo_sdm_weight
    ECHO_SDM_EXTRA_QUANT_ERROR_WEIGHT
    ECHO_SDM_HORIZON_FUTURE_WEIGHT
    ECHO_SDM_EC_ALPHA
    ECHO_SDM_EC_BIAS_WEIGHT
    ECHO_SDM_EC_SUPER_ALPHA
    ECHO_SDM_EC_SUPER_BIAS_WEIGHT
    ECHO_SDM_EC_SUPER_ACTIVATION_LIMIT
    ECHO_SDM_EC_SUPER_MAX_BIAS
    ECHO_SDM_EC_REFERENCE_ALPHA
    ECHO_SDM_EC_REFERENCE_BIAS_WEIGHT
    ECHO_SDM_EC_REFERENCE_ACTIVATION_LIMIT
    ECHO_SDM_EC_REFERENCE_MAX_BIAS)
  if(DEFINED ${_echo_sdm_weight} AND NOT "${${_echo_sdm_weight}}" STREQUAL "")
    target_compile_definitions(echo-sdm-quality-lab PRIVATE
      ${_echo_sdm_weight}=${${_echo_sdm_weight}})
  endif()
endforeach()

if(MSVC)
  set_property(TARGET echo-sdm-quality-lab PROPERTY
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
  target_compile_options(echo-sdm-quality-lab PRIVATE /W4 /permissive-)
else()
  target_compile_options(echo-sdm-quality-lab PRIVATE -Wall -Wextra -Wpedantic)
endif()
