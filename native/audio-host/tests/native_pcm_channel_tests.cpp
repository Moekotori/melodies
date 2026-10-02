#include "NativePcmChannelPolicy.h"
#include "NativeDaemonBootstrap.h"
#include <stdexcept>
#include <string>
#include <iostream>
#ifdef _WIN32
#include <io.h>
#include <fcntl.h>
#define testRead _read
#define testWrite _write
#define testClose _close
#else
#include <unistd.h>
#define testRead read
#define testWrite write
#define testClose close
#endif

void testNativePcmDecodeLayouts(const std::string& root);

int main(int argc, char** argv)
{
    try
    {
        const auto require = [](bool value) { if (!value) throw std::runtime_error("native PCM channel regression"); };
        require(argc == 2);
        testNativePcmDecodeLayouts(argv[1]);
        require(validDaemonPcmChannels(2) && validDaemonPcmChannels(8));
        require(!validDaemonPcmChannels(1) && !validDaemonPcmChannels(9));
        require(!requiresPcmChannelReopen(2, 1) && !requiresPcmChannelReopen(2, 2));
        require(!requiresPcmChannelReopen(6, 6) && !requiresPcmChannelReopen(8, 8));
        require(requiresPcmChannelReopen(2, 6) && requiresPcmChannelReopen(6, 8));
        require(requiresPcmChannelReopen(8, 2) && requiresPcmChannelReopen(6, 1));
        require(asioPcmOutputRangeFits(8, 0, 8));
        require(asioPcmOutputRangeFits(18, 10, 8));
        require(!asioPcmOutputRangeFits(18, 11, 8));
        require(!asioPcmOutputRangeFits(2, 0, 6));
        require(!asioPcmOutputRangeFits(8, -1, 6));
        int input[2], output[2];
#ifdef _WIN32
        require(_pipe(input, 65536, _O_BINARY) == 0 && _pipe(output, 65536, _O_BINARY) == 0);
#else
        require(pipe(input) == 0 && pipe(output) == 0);
#endif
        const std::string requests = "{\"id\":1,\"method\":\"host.initialize\",\"params\":{\"channels\":9}}\n"
            "{\"id\":2,\"method\":\"host.initialize\",\"params\":{\"channels\":8}}\n";
        require(testWrite(input[1], requests.data(), static_cast<unsigned int>(requests.size())) == static_cast<int>(requests.size()));
        testClose(input[1]);
        int channels = 2;
        require(initializeNativeDaemonChannels(input[0], output[1], channels) && channels == 8);
        testClose(input[0]);
        testClose(output[1]);
        std::string replies;
        char byte;
        while (testRead(output[0], &byte, 1) > 0) replies.push_back(byte);
        testClose(output[0]);
        require(replies.find("invalid native PCM channel count") != std::string::npos);
        require(replies.find("\"channels\":8") != std::string::npos);
        std::cout << "PASS PCM channel ranges and real bootstrap RPC\n";
        return 0;
    }
    catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
