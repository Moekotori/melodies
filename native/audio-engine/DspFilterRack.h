#pragma once
#include "buffer.h"
#include "third_party/nlohmann_json.hpp"
#include <array>
#include <atomic>
#include <memory>
#include <mutex>
#include <string>
#include <vector>
namespace echo {
class DspFilterProcessor {
public:
    virtual ~DspFilterProcessor() = default;
    virtual void process(PlanarAudioBuffer<float>&, int, int) noexcept = 0;
    virtual void process(PlanarAudioBuffer<double>&, int, int) noexcept = 0;
    virtual void reset() noexcept = 0;
    virtual bool active() const noexcept = 0;
    virtual int64_t tail() const noexcept = 0;
    virtual double latencyMs() const noexcept = 0;
    virtual bool clippingRisk() const noexcept = 0;
};
class DspFilterRack final {
public:
    static constexpr size_t maxInstances = 8;
    struct Node { int baseModule = -1; DspFilterProcessor* filter = nullptr; };
    struct Graph {
        ~Graph();
        uint64_t revision = 0;
        nlohmann::json filters = nlohmann::json::array(), sequence = nlohmann::json::array();
        std::vector<std::shared_ptr<DspFilterProcessor>> processors;
        std::vector<Node> nodes;
    };
    class Lease final {
    public:
        Lease() = default;
        Lease(const Lease&) = delete;
        Lease& operator=(const Lease&) = delete;
        Lease(Lease&& other) noexcept : owner(other.owner), index(other.index), graph(other.graph) { other.owner = nullptr; other.graph = nullptr; }
        ~Lease() { if (owner) { owner->readers_[index].fetch_sub(1, std::memory_order_seq_cst); owner->readers_[index].notify_all(); } }
        explicit operator bool() const noexcept { return graph != nullptr; }
        Graph* operator->() const noexcept { return graph; }
    private:
        friend class DspFilterRack;
        Lease(const DspFilterRack* o, int i, Graph* g) : owner(o), index(i), graph(g) {}
        const DspFilterRack* owner = nullptr;
        int index = 0;
        Graph* graph = nullptr;
    };
    DspFilterRack();
    Lease acquire() const noexcept;
    bool setState(const nlohmann::json& baseOrder, const nlohmann::json* filters, const nlohmann::json* sequence, std::string& error);
    nlohmann::json state() const;
    void prepare(double rate, int block, int channels);
    void reset() noexcept;
    bool active() const noexcept;
    bool clippingRisk() const noexcept;
    int64_t tail() const noexcept;
    double latencyMs() const noexcept;
private:
    friend struct DspPreparedRuntime;
    friend class DspPreparedHandoff;
    bool publish(const nlohmann::json&, const nlohmann::json&, const nlohmann::json&, bool rebuild, std::string&);
    mutable std::mutex writer_;
    std::array<std::unique_ptr<Graph>, 2> graphs_;
    mutable std::array<std::atomic<unsigned>, 2> readers_ {};
    std::atomic<int> active_ { 0 };
    uint64_t revision_ = 0;
    double rate_ = 48000;
    int block_ = 4096, channels_ = 2;
};
}
