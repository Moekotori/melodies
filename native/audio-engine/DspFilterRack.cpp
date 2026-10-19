#include "DspFilterRack.h"
#include "DspRackOrder.h"
#include "ConvolutionProcessor.h"
#include "SpatialDspProcessor.h"
#include <cmath>
#include <set>
#include <stdexcept>
namespace echo {
namespace {
using Json = nlohmann::json;
// Empty leaves first without a recursive call stack or new storage. Retirement
// runs outside the producer lock. nlohmann's normal destructor allocates a
// temporary stack for nonempty containers, even when destroying a JSON value.
void clearRetiredConfiguration(Json& value) noexcept {
    while (value.is_structured() && !value.empty()) {
        auto* leaf = &value;
        for (;;) {
            auto child = std::find_if(leaf->begin(), leaf->end(), [](const Json& item) {
                return item.is_structured() && !item.empty();
            });
            if (child == leaf->end()) break;
            leaf = &*child;
        }
        leaf->clear();
    }
}
double number(const Json& value, const char* key, double min, double max) {
    if (!value.contains(key) || !value[key].is_number()) throw std::runtime_error("Invalid filter number");
    const auto n = value[key].get<double>();
    if (!std::isfinite(n) || n < min || n > max) throw std::runtime_error("Filter parameter outside limits");
    return n;
}
bool boolean(const Json& value, const char* key) {
    if (!value.contains(key) || !value[key].is_boolean()) throw std::runtime_error("Invalid filter boolean");
    return value[key].get<bool>();
}
class Filter final : public DspFilterProcessor {
public:
    Filter(const Json& config, double rate, int block, int channels) : enabled(boolean(config, "enabled")), rate_(rate) {
        const auto kind = config.at("kind").get<std::string>();
        if (kind == "equalizer" || kind == "headphoneCorrection") {
            const bool correction = kind == "headphoneCorrection";
            eq = std::make_unique<EqProcessor>(correction);
            EqState s; s.enabled = enabled; s.bandEnabled.fill(false);
            const auto& v = config.at("eq");
            s.preampDb = number(v, "preampDb", correction ? -60 : -12, correction ? 24 : 6);
            const auto& bands = v.at("bands");
            if (!bands.is_array() || bands.size() > eqBandCount) throw std::runtime_error("Too many EQ bands");
            const std::array<std::string, 6> types {"peaking", "lowshelf", "highshelf", "lowpass", "highpass", "notch"};
            for (size_t i = 0; i < bands.size(); ++i) {
                const auto& b = bands[i];
                const auto type = b.at("filterType").get<std::string>();
                const auto found = std::find(types.begin(), types.end(), type);
                if (found == types.end()) throw std::runtime_error("Unknown EQ filter type");
                s.bandFilterTypes[i] = static_cast<EqFilterType>(found - types.begin());
                s.bandFrequenciesHz[i] = number(b, "frequencyHz", correction ? 1 : 20, 20000);
                s.bandGainsDb[i] = number(b, "gainDb", correction ? -60 : -24, correction ? 30 : 12);
                s.bandQ[i] = number(b, "q", correction ? 0.001 : 0.1, correction ? 1000 : type == "notch" ? 24 : 12);
                s.bandEnabled[i] = boolean(b, "enabled");
            }
            eq->setState(s); eq->prepare(rate, block, std::max(8, channels));
        } else if (kind == "crossfeed") {
            spatial = std::make_unique<SpatialDspProcessor>();
            const auto& c = config.at("crossfeed");
            const auto model = c.at("model").get<std::string>();
            if (model != "legacy" && model != "bs2b") throw std::runtime_error("Unknown crossfeed model");
            spatial->setCrossfeedState({enabled && boolean(c, "enabled"), static_cast<float>(number(c, "amount", 0, 1)), static_cast<float>(number(c, "cutoffHz", 100, 4000)), model == "bs2b", static_cast<float>(number(c, "feedDb", 1, 15))});
            spatial->prepare(rate, block, channels);
        } else if (kind == "procedural") {
            controller = std::make_unique<ProceduralDspController>();
            auto configState = config.at("procedural");
            configState["enabled"] = enabled && boolean(configState, "enabled");
            std::string error;
            if (!controller->setState(configState, error)) throw std::runtime_error(error);
            procedural = std::make_unique<ProceduralDspProcessor>(*controller);
            double delay = 0;
            for (const auto& op : configState["operations"]) if (op["kind"] == "delay") delay += op["delayMs"].get<double>() * op["channels"].size();
            procedural->prepare(rate, std::max(8, channels), delay);
        } else if (kind == "convolution") {
            convolution = std::make_unique<ConvolutionProcessor>();
            convolution->prepare(rate, block, channels);
            const auto& c = config.at("convolution");
            convolution->setTrimDb(number(c, "trimDb", -24, 6));
            if (!c.at("bankId").is_null()) {
                // Main resolves the bank ID into a validated private cache path.
                const auto path = c.at("nativePath").get<std::string>();
                if (!convolution->loadImpulseResponse(path, c.at("bankId").get<std::string>(), c.value("irName", std::string()))) throw std::runtime_error("Cannot load filter IR");
                const auto state = convolution->getState();
                if (state.outputChannels > 0 && state.outputChannels != channels) throw std::runtime_error("Instance IR must preserve the output channel layout");
            } else if (enabled) throw std::runtime_error("Missing filter IR");
            convolution->setEnabled(enabled);
        } else throw std::runtime_error("Unsupported filter kind");
    }
    template<typename Sample> void run(PlanarAudioBuffer<Sample>& b, int start, int count) noexcept {
        if (eq) eq->processBlock(b, start, count);
        else if (spatial) spatial->processCrossfeedBlock(b, start, count);
        else if (procedural) procedural->processBlock(b, start, count);
        else if (convolution) convolution->processBlock(b, start, count);
    }
    void process(PlanarAudioBuffer<float>& b, int s, int n) noexcept override { run(b,s,n); }
    void process(PlanarAudioBuffer<double>& b, int s, int n) noexcept override { run(b,s,n); }
    void reset() noexcept override { if(eq) eq->reset(); if(spatial) spatial->reset(); if(procedural) procedural->reset(); if(convolution) convolution->resetForHardReplace(); }
    bool active() const noexcept override { return (eq && eq->isEnabled()) || (spatial && spatial->isCrossfeedEnabled()) || (procedural && procedural->isActive()) || (convolution && convolution->isEnabled()); }
    int64_t tail() const noexcept override { return eq ? eq->tailFramesRemaining() : spatial ? spatial->tailFramesRemaining() : procedural ? procedural->tailFramesRemaining() : convolution->tailFramesRemaining(); }
    double latencyMs() const noexcept override { return procedural && active() ? -1 : convolution ? convolution->algorithmLatencySamples() * 1000.0 / rate_ : 0; }
    bool clippingRisk() const noexcept override { return eq ? eq->hasClippingRisk() : convolution ? convolution->hasClippingRisk() : false; }
private:
    bool enabled;
    double rate_;
    std::unique_ptr<EqProcessor> eq;
    std::unique_ptr<SpatialDspProcessor> spatial;
    std::unique_ptr<ProceduralDspController> controller;
    std::unique_ptr<ProceduralDspProcessor> procedural;
    std::unique_ptr<ConvolutionProcessor> convolution;
};
Json baseIds() {
    auto result = Json::array();
    for (auto m : DspRackOrder::defaultOrder()) result.push_back(DspRackOrder::moduleIdText(m));
    return result;
}
}
DspFilterRack::Graph::~Graph() {
    clearRetiredConfiguration(filters);
    clearRetiredConfiguration(sequence);
}
DspFilterRack::DspFilterRack() {
    graphs_[0] = std::make_unique<Graph>();
    graphs_[0]->sequence = baseIds();
    for(auto m : DspRackOrder::defaultOrder()) graphs_[0]->nodes.push_back({static_cast<int>(m), nullptr});
}
DspFilterRack::Lease DspFilterRack::acquire() const noexcept {
    for(int attempt=0; attempt<3; ++attempt) {
        const int i = active_.load(std::memory_order_seq_cst);
        readers_[i].fetch_add(1, std::memory_order_seq_cst);
        if(i == active_.load(std::memory_order_seq_cst)) return Lease(this, i, graphs_[i].get());
        readers_[i].fetch_sub(1, std::memory_order_seq_cst);
        readers_[i].notify_all();
    }
    return {};
}
bool DspFilterRack::publish(const Json& baseOrder, const Json& filters, const Json& sequence, bool rebuild, std::string& error) {
    try {
        const int current = active_.load(std::memory_order_seq_cst), next = 1-current;
        if (!rebuild && graphs_[current]->filters == filters && graphs_[current]->sequence == sequence) {
            auto projection = Json::array();
            for (const auto& id : sequence) { DspRackModuleId m; if (id.is_string() && DspRackOrder::parseModuleId(id.get<std::string>(),m)) projection.push_back(id); }
            if (projection == baseOrder) return true;
        }
        if(readers_[next].load(std::memory_order_seq_cst) != 0) { error="Filter rack busy; retry after this audio block"; return false; }
        std::set<std::string> ids;
        if (!filters.is_array() || filters.size()>maxInstances || !sequence.is_array() || !baseOrder.is_array() || baseOrder.size()!=DspRackOrder::moduleCount) throw std::runtime_error("Invalid filter rack size");
        auto graph = std::make_unique<Graph>(); graph->filters=filters; graph->sequence=sequence;
        for(const auto& id : baseOrder) { DspRackModuleId m; if(!id.is_string() || !DspRackOrder::parseModuleId(id.get<std::string>(), m) || !ids.insert(id.get<std::string>()).second) throw std::runtime_error("Invalid base rack order"); }
        size_t convolvers=0; double delayBudget=0;
        for(size_t i=0; i<filters.size(); ++i) {
            const auto& f=filters[i]; const auto id=f.at("id").get<std::string>();
            if(id.size()>64 || id.rfind("filter-",0)!=0 || !ids.insert(id).second || f.at("name").get<std::string>().size()>80) throw std::runtime_error("Invalid filter instance ID/name");
            const auto kind=f.at("kind").get<std::string>();
            if(kind=="convolution" && ++convolvers>2) throw std::runtime_error("At most two additional convolvers");
            if(kind=="procedural") for(const auto& op : f.at("procedural").at("operations")) if(op.value("kind",std::string())=="delay") delayBudget+=op.at("delayMs").get<double>()*op.at("channels").size();
            if(delayBudget>4000) throw std::runtime_error("Instance delay memory budget exceeded");
            std::shared_ptr<DspFilterProcessor> processor;
            if(!rebuild) for(size_t old=0; old<graphs_[current]->filters.size(); ++old) if(graphs_[current]->filters[old]==f) { processor=graphs_[current]->processors[old]; break; }
            if(!processor) processor=std::make_shared<Filter>(f,rate_,block_,channels_);
            graph->processors.push_back(std::move(processor));
        }
        if(sequence.size()!=ids.size()) throw std::runtime_error("Incomplete filter sequence");
        size_t basePosition=0;
        for(const auto& value : sequence) {
            const auto id=value.get<std::string>();
            if(ids.erase(id)!=1) throw std::runtime_error("Duplicate or unknown filter sequence node");
            DspRackModuleId m;
            if(DspRackOrder::parseModuleId(id,m)) {
                if(basePosition>=baseOrder.size() || baseOrder[basePosition++]!=id) throw std::runtime_error("Base order/sequence mismatch");
                graph->nodes.push_back({static_cast<int>(m),nullptr});
            } else {
                for(size_t i=0; i<filters.size(); ++i) if(filters[i]["id"]==id) { graph->nodes.push_back({-1,graph->processors[i].get()}); break; }
            }
        }
        graph->revision = ++revision_;
        graphs_[next]=std::move(graph); // retired objects are destroyed only here, on control thread.
        active_.store(next,std::memory_order_seq_cst);
        if (readers_[current].load(std::memory_order_seq_cst) == 0) graphs_[current].reset();
        return true;
    } catch(const std::exception& e) { error=e.what(); return false; }
}
bool DspFilterRack::setState(const Json& order, const Json* filters, const Json* sequence, std::string& error) {
    std::lock_guard lock(writer_);
    const auto& current=*graphs_[active_.load()];
    auto nodes=sequence ? *sequence : current.sequence;
    if(!sequence) {
        size_t i=0;
        for(auto& node : nodes) { DspRackModuleId m; if(node.is_string() && DspRackOrder::parseModuleId(node.get<std::string>(),m) && i<order.size()) node=order[i++]; }
        if(filters) { nodes=order; for(const auto& f:*filters) nodes.push_back(f.at("id")); }
    }
    return publish(order, filters ? *filters : current.filters, nodes, false, error);
}
nlohmann::json DspFilterRack::state() const { std::lock_guard lock(writer_); const auto& g=*graphs_[active_.load()]; auto filters=g.filters; for(auto& f:filters) if(f.contains("convolution")) f["convolution"].erase("nativePath"); return {{"filters",filters},{"sequence",g.sequence}}; }
void DspFilterRack::prepare(double rate,int block,int channels) {
    std::lock_guard lock(writer_);
    if(rate_==rate && block_==block && channels_==channels) return;
    const double oldRate = rate_; const int oldBlock = block_, oldChannels = channels_;
    rate_=rate; block_=block; channels_=channels;
    const auto& current=*graphs_[active_.load()]; auto order=Json::array();
    for(const auto& id:current.sequence) { DspRackModuleId m; if(DspRackOrder::parseModuleId(id.get<std::string>(),m)) order.push_back(id); }
    std::string error; if(!publish(order,current.filters,current.sequence,true,error)) {
        rate_=oldRate; block_=oldBlock; channels_=oldChannels;
        throw std::runtime_error(error);
    }
}
void DspFilterRack::reset() noexcept { auto lease=acquire(); if(lease) for(const auto& p:lease->processors) p->reset(); }
bool DspFilterRack::active() const noexcept { auto lease=acquire(); if(lease) for(const auto& p:lease->processors) if(p->active()) return true; return false; }
bool DspFilterRack::clippingRisk() const noexcept { auto lease=acquire(); if(lease) for(const auto& p:lease->processors) if(p->clippingRisk()) return true; return false; }
int64_t DspFilterRack::tail() const noexcept { int64_t tail=0; auto lease=acquire(); if(lease) for(const auto& p:lease->processors) tail=std::max(tail,p->tail()); return tail; }
double DspFilterRack::latencyMs() const noexcept { double result=0; auto lease=acquire(); if(lease) for(const auto& p:lease->processors) { const double latency=p->latencyMs(); if(latency<0) return -1; result+=latency; } return result; }
}
