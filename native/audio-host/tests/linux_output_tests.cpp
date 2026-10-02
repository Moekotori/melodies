#include "LinuxAlsaPcmWrite.h"
#include "LinuxOutputFailure.h"
#include "LinuxAlsaDevicePolicy.h"

#include <iostream>
#include <stdexcept>
#include <thread>
#include <vector>

static void require(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
}

struct FakePcm
{
    std::vector<long> results;
    std::vector<const std::uint8_t*> pointers;
    std::vector<long> counts;
    std::size_t next = 0;
    int prepared = 0;
    int resumed = 0;
    int prepareResult = 0;
    int resumeResult = 0;
    std::atomic<bool>* cancelOnWait = nullptr;

    long write(const std::uint8_t* data, long frames)
    {
        pointers.push_back(data);
        counts.push_back(frames);
        return next < results.size() ? results[next++] : frames;
    }
    int wait(int milliseconds)
    {
        require(milliseconds <= 20, "ALSA wait must be bounded");
        if (cancelOnWait) cancelOnWait->store(false);
        return 0;
    }
    int prepare() { ++prepared; return prepareResult; }
    int resume() { ++resumed; return resumeResult; }
    void pause(int milliseconds) { wait(milliseconds); }
};

int main()
{
    try
    {
        for (const auto id : { ":3,0", ":CARD=Series,DEV=0", "hw:3,0" })
            require(isLinuxAlsaHardwareDevice(id), "hardware endpoints remain selectable");
        for (const auto id : { "", ":", "default", "pipewire", "null", "front:CARD=Series,DEV=0",
                "surround51:CARD=Series,DEV=0", "dmix:CARD=Series,DEV=0", "plughw:3,0" })
            require(!isLinuxAlsaHardwareDevice(id), "plugin aliases must not appear as exclusive hardware");
        int opens = 0;
        int waited = 0;
        auto wait = [&](int ms) { waited += ms; };
        require(openLinuxAlsaAfterIdleRelease([&] { return ++opens < 52 ? -EBUSY : 0; }, wait, -EBUSY) == 0,
            "opening succeeds after the server idle timeout");
        require(waited == 5100, "wait only while the device remains busy");
        opens = waited = 0;
        require(openLinuxAlsaAfterIdleRelease([&] { ++opens; return -EBUSY; }, wait, -EBUSY) == -EBUSY,
            "persistent ownership remains an error");
        require(opens == 61 && waited == 6000, "busy retry budget is bounded");
        opens = waited = 0;
        require(openLinuxAlsaAfterIdleRelease([&] { ++opens; return -ENODEV; }, wait, -EBUSY) == -ENODEV,
            "device removal is not retried");
        require(opens == 1 && waited == 0, "non-busy errors fail immediately");
        std::uint8_t samples[32] {};
        std::atomic<bool> running { true };
        FakePcm partial { { 1, -EAGAIN, 2, 1 } };
        require(writeLinuxAlsaPcmBlock(partial, samples, 4, 8, running) == 0, "short writes complete");
        require(partial.counts == std::vector<long>({4, 3, 3, 1}), "only unwritten frames are retried");
        require(partial.pointers == std::vector<const std::uint8_t*>({samples, samples + 8, samples + 8, samples + 24}), "retry offsets preserve DSD byte order");

        FakePcm underrun { { 1, -EPIPE, 3 } };
        require(writeLinuxAlsaPcmBlock(underrun, samples, 4, 8, running) == 0, "underrun recovery completes current block");
        require(underrun.prepared == 1 && underrun.pointers[1] == underrun.pointers[2], "underrun cannot skip unwritten audio");

        FakePcm suspended { { -ESTRPIPE, 4 } };
        require(writeLinuxAlsaPcmBlock(suspended, samples, 4, 8, running) == 0, "suspend resumes");
        require(suspended.resumed == 1, "suspend uses resume");

        FakePcm removed { { -ENODEV, 4 } };
        require(writeLinuxAlsaPcmBlock(removed, samples, 4, 8, running) == -ENODEV, "device removal stops the producer");
        require(removed.next == 1, "failed device is not written again");

        FakePcm recoveryFailed { { -EPIPE } };
        recoveryFailed.prepareResult = -ENODEV;
        require(writeLinuxAlsaPcmBlock(recoveryFailed, samples, 4, 8, running) == -ENODEV, "failed recovery is surfaced");

        FakePcm blocked { { -EAGAIN } };
        blocked.cancelOnWait = &running;
        require(writeLinuxAlsaPcmBlock(blocked, samples, 4, 8, running) == -ECANCELED, "stop cancels a blocked write");
        require(blocked.next == 1, "cancellation does not consume another block");

        running.store(true);
        FakePcm suspendedStop { { -ESTRPIPE } };
        suspendedStop.resumeResult = -EAGAIN;
        suspendedStop.cancelOnWait = &running;
        require(writeLinuxAlsaPcmBlock(suspendedStop, samples, 4, 8, running) == -ECANCELED, "stop cancels suspend recovery");

        LinuxOutputFailure failure;
        require(failure.take() == 0, "healthy output has no event");
        std::thread producer([&] { failure.record(ENODEV); failure.record(EIO); });
        producer.join();
        require(failure.failed() && failure.take() == ENODEV, "first failure survives across threads");
        require(failure.take() == 0 && failure.failed(), "failure is emitted once and remains terminal");
        failure.reset();
        require(!failure.failed(), "new output lifetime starts healthy");
        failure.record(EIO);
        require(failure.take() == EIO, "a reopened output can report a new failure");
        std::cout << "PASS Linux output short writes, recovery, cancellation and failure lifetime\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
