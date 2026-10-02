#pragma once

#include <condition_variable>
#include <cstddef>
#include <deque>
#include <functional>
#include <mutex>
#include <string>
#include <thread>

class RpcOutputWriter final
{
public:
    enum class MessageClass
    {
        Reliable,
        Position,
        LevelMeter,
    };

    using WriteFunction = std::function<std::ptrdiff_t(const char*, std::size_t)>;

    explicit RpcOutputWriter(int fd, WriteFunction writeFunction = {}, std::size_t maximumPending = 256);
    ~RpcOutputWriter();

    RpcOutputWriter(const RpcOutputWriter&) = delete;
    RpcOutputWriter& operator=(const RpcOutputWriter&) = delete;

    bool enqueue(std::string line, MessageClass messageClass = MessageClass::Reliable);
    void close();
    bool failed() const;

private:
    struct PendingMessage
    {
        std::string line;
        MessageClass messageClass = MessageClass::Reliable;
    };

    static std::ptrdiff_t writeFd(int fd, const char* data, std::size_t bytes);
    bool writeAll(const std::string& line);
    void run();

    int fd_ = -1;
    WriteFunction writeFunction_;
    const std::size_t maximumPending_;
    mutable std::mutex mutex_;
    std::condition_variable workAvailable_;
    std::condition_variable spaceAvailable_;
    std::deque<PendingMessage> pending_;
    bool accepting_ = true;
    bool failed_ = false;
    std::thread worker_;
};
