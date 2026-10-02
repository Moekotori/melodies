#include "RpcOutputWriter.h"

#include <algorithm>
#include <cerrno>
#include <limits>
#include <utility>

#ifdef _WIN32
#include <io.h>
#else
#include <unistd.h>
#endif

RpcOutputWriter::RpcOutputWriter(int fd, WriteFunction writeFunction, std::size_t maximumPending)
    : fd_(fd)
    , writeFunction_(std::move(writeFunction))
    , maximumPending_(std::max<std::size_t>(1, maximumPending))
{
    if (!writeFunction_)
        writeFunction_ = [fd](const char* data, std::size_t bytes) { return writeFd(fd, data, bytes); };
    worker_ = std::thread([this] { run(); });
}

RpcOutputWriter::~RpcOutputWriter()
{
    close();
}

bool RpcOutputWriter::enqueue(std::string line, MessageClass messageClass)
{
    if (line.empty())
        return true;
    if (line.back() != '\n')
        line.push_back('\n');

    std::unique_lock<std::mutex> lock(mutex_);
    if (!accepting_ || failed_)
        return false;

    if (messageClass != MessageClass::Reliable)
    {
        const auto existing = std::find_if(pending_.rbegin(), pending_.rend(), [messageClass](const PendingMessage& message) {
            return message.messageClass == messageClass;
        });
        if (existing != pending_.rend())
        {
            existing->line = std::move(line);
            return true;
        }
        if (pending_.size() >= maximumPending_)
            return false;
    }
    else
    {
        spaceAvailable_.wait(lock, [this] { return pending_.size() < maximumPending_ || !accepting_ || failed_; });
        if (!accepting_ || failed_)
            return false;
    }

    pending_.push_back({std::move(line), messageClass});
    workAvailable_.notify_one();
    return true;
}

void RpcOutputWriter::close()
{
    {
        std::lock_guard<std::mutex> lock(mutex_);
        accepting_ = false;
    }
    workAvailable_.notify_all();
    spaceAvailable_.notify_all();
    if (worker_.joinable())
        worker_.join();
}

bool RpcOutputWriter::failed() const
{
    std::lock_guard<std::mutex> lock(mutex_);
    return failed_;
}

std::ptrdiff_t RpcOutputWriter::writeFd(int fd, const char* data, std::size_t bytes)
{
#ifdef _WIN32
    return static_cast<std::ptrdiff_t>(_write(fd, data, static_cast<unsigned int>(
        std::min<std::size_t>(bytes, static_cast<std::size_t>(std::numeric_limits<unsigned int>::max())))));
#else
    return static_cast<std::ptrdiff_t>(write(fd, data, bytes));
#endif
}

bool RpcOutputWriter::writeAll(const std::string& line)
{
    std::size_t offset = 0;
    while (offset < line.size())
    {
        const auto written = writeFunction_(line.data() + offset, line.size() - offset);
        if (written > 0)
        {
            offset += std::min<std::size_t>(static_cast<std::size_t>(written), line.size() - offset);
            continue;
        }
        if (written < 0 && errno == EINTR)
            continue;
        return false;
    }
    return true;
}

void RpcOutputWriter::run()
{
    for (;;)
    {
        PendingMessage message;
        {
            std::unique_lock<std::mutex> lock(mutex_);
            workAvailable_.wait(lock, [this] { return !pending_.empty() || !accepting_; });
            if (pending_.empty())
                return;
            message = std::move(pending_.front());
            pending_.pop_front();
            spaceAvailable_.notify_all();
        }

        if (writeAll(message.line))
            continue;

        {
            std::lock_guard<std::mutex> lock(mutex_);
            failed_ = true;
            accepting_ = false;
            pending_.clear();
        }
        workAvailable_.notify_all();
        spaceAvailable_.notify_all();
        return;
    }
}
