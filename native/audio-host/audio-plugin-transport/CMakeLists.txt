cmake_minimum_required(VERSION 3.24)
project(echo_audio_plugin_transport LANGUAGES CXX)

# Independent native preview target. Nothing is inserted into production
# playback until latency, host lifecycle and drain integration are implemented.
add_library(echo-audio-plugin-transport STATIC PcmBlockExchange.cpp SharedPcmMapping.cpp)
target_compile_features(echo-audio-plugin-transport PUBLIC cxx_std_17)
target_include_directories(echo-audio-plugin-transport PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})
if(WIN32)
  target_link_libraries(echo-audio-plugin-transport PRIVATE advapi32)
elseif(UNIX AND NOT APPLE)
  target_link_libraries(echo-audio-plugin-transport PRIVATE rt)
endif()

add_executable(echo-audio-plugin-transport-tests tests/PcmTransportTests.cpp)
target_link_libraries(echo-audio-plugin-transport-tests PRIVATE echo-audio-plugin-transport)
enable_testing()
add_test(NAME echo-audio-plugin-transport-tests COMMAND echo-audio-plugin-transport-tests)
set_tests_properties(echo-audio-plugin-transport-tests PROPERTIES TIMEOUT 10)
