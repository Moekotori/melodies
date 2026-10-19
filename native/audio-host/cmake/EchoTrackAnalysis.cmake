# C# library analysis entry point; kept separate from the imported audio engine.
add_executable(echo-track-analysis-tests EXCLUDE_FROM_ALL
  ../audio-engine/TrackAnalysis.cpp tests/track_analysis_tests.cpp)
target_compile_features(echo-track-analysis-tests PRIVATE cxx_std_20)
target_include_directories(echo-track-analysis-tests PRIVATE ../audio-engine)
if(MSVC)
  set_property(TARGET echo-track-analysis-tests PROPERTY
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()
add_dependencies(echo-audio-engine-tests echo-track-analysis-tests)
add_test(NAME echo-track-analysis-tests COMMAND echo-track-analysis-tests)

add_executable(echo-track-analysis
  tools/track-analysis/main.cpp
  ../audio-engine/TrackAnalysis.cpp)
target_compile_features(echo-track-analysis PRIVATE cxx_std_20)
target_include_directories(echo-track-analysis PRIVATE ../audio-engine)
if(WIN32 OR (APPLE AND ECHO_FFMPEG_ROOT))
  target_include_directories(echo-track-analysis PRIVATE "${ECHO_FFMPEG_ROOT}/include")
  target_link_directories(echo-track-analysis PRIVATE "${ECHO_FFMPEG_ROOT}/lib")
  target_link_libraries(echo-track-analysis PRIVATE avformat avcodec swresample avutil)
elseif(APPLE)
  target_link_libraries(echo-track-analysis PRIVATE PkgConfig::ECHO_MACOS_FFMPEG)
else()
  target_link_libraries(echo-track-analysis PRIVATE avformat avcodec swresample avutil)
endif()
if(MSVC)
  target_compile_definitions(echo-track-analysis PRIVATE NOMINMAX)
  set_property(TARGET echo-track-analysis PROPERTY
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()
if(APPLE AND ECHO_FFMPEG_ROOT)
  set_target_properties(echo-track-analysis PROPERTIES
    BUILD_WITH_INSTALL_RPATH TRUE
    INSTALL_RPATH "@loader_path/../tools-macos/lib;@loader_path/tools/lib")
elseif(APPLE)
  set_target_properties(echo-track-analysis PROPERTIES
    BUILD_WITH_INSTALL_RPATH TRUE
    INSTALL_RPATH "@loader_path")
endif()
if(WIN32)
  foreach(runtime_name avformat avcodec swresample avutil)
    file(GLOB analysis_runtime_dlls "${ECHO_FFMPEG_ROOT}/bin/${runtime_name}-*.dll")
    foreach(runtime_dll IN LISTS analysis_runtime_dlls)
      add_custom_command(TARGET echo-track-analysis POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different "${runtime_dll}"
          "$<TARGET_FILE_DIR:echo-track-analysis>")
    endforeach()
  endforeach()
endif()
if(APPLE AND ECHO_FFMPEG_ROOT)
  set_tests_properties(echo-libav-seek-tests PROPERTIES
    ENVIRONMENT "DYLD_LIBRARY_PATH=${ECHO_FFMPEG_ROOT}/lib")
endif()

