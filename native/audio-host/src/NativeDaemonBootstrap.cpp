#include "NativeDaemonBootstrap.h"
#include "NativePcmChannelPolicy.h"
#include "RpcOutputWriter.h"
#include "../../audio-engine/third_party/nlohmann_json.hpp"
#include <cerrno>
#include <iostream>
#include <string>
#ifdef _WIN32
#include <io.h>
#else
#include <unistd.h>
#endif

bool initializeNativeDaemonChannels(int inputFd, int outputFd, int& channels)
{
    using Json = nlohmann::json;
    RpcOutputWriter output(outputFd, {}, 4);
    std::cout << "{\"ready\":true,\"readyLevel\":\"bootstrap\",\"protocolVersion\":1,\"backendContractVersion\":2}"
              << std::endl;
    std::string line;
    line.reserve(256);
    for (int requests = 0; requests < 8; ++requests)
    {
        line.clear();
        char byte = 0;
        for (;;)
        {
#ifdef _WIN32
            const int count = _read(inputFd, &byte, 1);
#else
            const auto count = read(inputFd, &byte, 1);
#endif
            if (count < 0 && errno == EINTR) continue;
            if (count <= 0) return false;
            if (byte == '\n') break;
            if (line.size() >= 16 * 1024) return false;
            line.push_back(byte);
        }
        const Json request = Json::parse(line, nullptr, false);
        const Json id = request.is_object() && request.contains("id") ? request["id"] : Json(nullptr);
        const auto reject = [&](const char* message) {
            output.enqueue(Json{{"jsonrpc", "2.0"}, {"id", id},
                {"error", {{"code", -32602}, {"message", message}}}}.dump());
        };
        if (!request.is_object() || !request.contains("method") || !request["method"].is_string())
        {
            reject("invalid host initialization request");
            continue;
        }
        const auto method = request["method"].get<std::string>();
        if (method == "rpc.shutdown")
        {
            output.enqueue(Json{{"jsonrpc", "2.0"}, {"id", id}, {"result", "ok"}}.dump());
            return false;
        }
        if (method != "host.initialize")
        {
            reject("native host is awaiting host.initialize");
            continue;
        }
        const auto params = request.find("params");
        if (params == request.end() || !params->is_object() || !params->contains("channels")
            || !(*params)["channels"].is_number_integer())
        {
            reject("invalid native PCM channel count");
            continue;
        }
        const auto count = (*params)["channels"].get<double>();
        if (count < 2 || count > maxNativePcmChannels)
        {
            reject("invalid native PCM channel count");
            continue;
        }
        channels = static_cast<int>(count);
        output.enqueue(Json{{"jsonrpc", "2.0"}, {"id", id},
            {"result", {{"accepted", true}, {"channels", channels}}}}.dump());
        return true;
    }
    return false;
}
