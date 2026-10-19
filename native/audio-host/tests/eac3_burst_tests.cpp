#include "Eac3BurstReader.h"
#include "Eac3CarrierClock.h"
#include <array>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <vector>
extern "C" {
#include <libavformat/avformat.h>
}

void require(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
int main(int argc, char** argv) {
    try {
        require(argc == 2, "provide a generated local 48 kHz EAC3 fixture");
        require(!echo::eac3CarrierDrained(false, 0, 6144, 0.032), "demux input must be ended");
        require(!echo::eac3CarrierDrained(true, 1, 6144, 0.032), "padding must be drained");
        require(!echo::eac3CarrierDrained(true, 0, 6144, 0.03199), "native clock must play the final carrier frame");
        require(echo::eac3CarrierDrained(true, 0, 6144, 0.032), "both device drain facts must permit EOF");
        echo::Eac3BurstReader reader;
        const auto info = reader.open(argv[1]);
        require(info.channels > 0 && !info.atmosSignalled, "ordinary EAC3 must not be advertised as JOC");
        AVFormatContext* source = nullptr;
        require(avformat_open_input(&source, argv[1], nullptr, nullptr) == 0, "fixture open failed");
        AVPacket* packet = av_packet_alloc();
        std::array<uint8_t, echo::eac3BurstBytes> burst;
        int bursts = 0;
        while (reader.next(burst)) {
            require(burst[0] == 0x72 && burst[1] == 0xf8 && burst[2] == 0x1f && burst[3] == 0x4e,
                "IEC 61937 preamble must be correct");
            require(burst[4] == 0x15 && burst[5] == 0, "encoded data type must be EAC3, never PCM");
            require(av_read_frame(source, packet) == 0, "encoded source packet missing");
            const int payloadLength = burst[6] | (burst[7] << 8);
            require(payloadLength == packet->size, "length is encoded bytes, not bits");
            for (int i = 0; i < packet->size; ++i)
                require(burst[8 + (i ^ 1)] == packet->data[i], "every encoded payload byte must survive unchanged");
            for (std::size_t i = 8 + payloadLength; i < burst.size(); ++i)
                require(burst[i] == 0, "remaining carrier must be padded, not uninitialized data");
            ++bursts;
            av_packet_unref(packet);
        }
        require(bursts >= 4, "fixture must contain enough complete bursts");
        require(!reader.next(burst), "drained reader EOF must be stable");
        reader.seek(0.08);
        require(reader.next(burst), "seek must restart mux state");
        require(reader.positionSeconds() >= 0.08 && reader.positionSeconds() <= 0.144,
            "seek must report actual encoded boundary");
        reader.close();
        bool rejected = false;
        try { reader.next(burst); } catch (const std::exception&) { rejected = true; }
        require(rejected, "closed reader must not retain packets");
        rejected = false;
        try { reader.open("https://example.org/music.eac3"); } catch (const std::exception&) { rejected = true; }
        require(rejected, "network source must be rejected before FFmpeg open");
        av_packet_free(&packet); avformat_close_input(&source);
        std::cout << "EAC3 payload preservation, carrier padding, seek reset, EOF and local input boundary passed\n";
        return 0;
    } catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
