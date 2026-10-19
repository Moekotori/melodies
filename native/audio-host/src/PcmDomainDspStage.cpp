#include "PcmDomainDspStage.h"
#include "PcmDomainDspProcessors.h"
#include "../../audio-engine/DspChainConfiguration.h"
#include "../../audio-engine/DspPreparedRuntime.h"
#include <type_traits>

#include <algorithm>
#include <cstddef>

PcmDomainDspStage::~PcmDomainDspStage() = default;

PcmDomainDspStage::PcmDomainDspStage(std::unique_ptr<PcmDomainDspProcessors> processors)
    : ownedProcessors_(std::move(processors)), convolution_(ownedProcessors_->convolution),
      dspChain_(ownedProcessors_->eq, ownedProcessors_->convolution, ownedProcessors_->balance,
          ownedProcessors_->headroom, ownedProcessors_->replayGain, ownedProcessors_->compressor,
          ownedProcessors_->spatial, ownedProcessors_->rate, ownedProcessors_->meter,
          &ownedProcessors_->rack, &ownedProcessors_->workshop,
          &ownedProcessors_->multiband, &ownedProcessors_->deEsser)
{
}

std::shared_ptr<const PcmDomainDspStage::ConfigurationSnapshot> PcmDomainDspStage::configurationSnapshot() const
{
    return dspChain_.configurationSnapshot();
}

bool PcmDomainDspStage::configurationMatches(const ConfigurationSnapshot& snapshot) const
{
    return dspChain_.configurationMatches(snapshot);
}

bool PcmDomainDspStage::beginPreparedHandoff(PcmDomainDspStage& prepared,
    const ConfigurationSnapshot& expected, echo::DspPreparedHandoff& handoff, std::string& error)
{
    return handoff.acquire(dspChain_, prepared.dspChain_, expected, error);
}

void PcmDomainDspStage::activatePrepared(PcmDomainDspStage& prepared, echo::DspPreparedHandoff& handoff) noexcept
{
    handoff.activate();
    static_assert(std::is_nothrow_swappable_v<echo::DoubleAudioBuffer>);
    using std::swap;
    swap(buffer_, prepared.buffer_);
    swap(channels_, prepared.channels_);
    swap(sampleRate_, prepared.sampleRate_);
    swap(maximumBlockFrames_, prepared.maximumBlockFrames_);
    swap(prepared_, prepared.prepared_);
}

std::unique_ptr<PcmDomainDspStage> PcmDomainDspStage::createAdmissionCandidate(
    const ConfigurationSnapshot& snapshot, std::string& error)
{
    auto candidate = std::unique_ptr<PcmDomainDspStage>(new PcmDomainDspStage(std::make_unique<PcmDomainDspProcessors>()));
    if (!candidate->dspChain_.applyConfiguration(snapshot, error)) return nullptr;
    return candidate;
}

PcmDomainDspStage::PcmDomainDspStage(
    echo::EqProcessor& eq,
    echo::ConvolutionProcessor& convolution,
    echo::ChannelBalanceProcessor& channelBalance,
    echo::DspHeadroomProcessor& headroom,
    echo::ReplayGainProcessor& replayGain,
    echo::CompressorProcessor& compressor,
    echo::SpatialDspProcessor& spatialDsp,
    echo::PlaybackRateProcessor& playbackRate,
    echo::LevelMeterProcessor& meter,
    echo::DspRackOrder* rackOrder,
    echo::WorkshopAudioEffectController* workshopAudioEffectController,
    echo::MultibandCompressorProcessor* multiband,
    echo::DeEsserProcessor* deEsser)
    : convolution_(convolution), dspChain_(eq, convolution, channelBalance, headroom, replayGain, compressor, spatialDsp, playbackRate, meter,
          rackOrder, workshopAudioEffectController, multiband, deEsser)
{
}

void PcmDomainDspStage::prepare(double sampleRate, int maximumBlockFrames, int channels)
{
    channels_ = std::max(1, channels);
    convolution_.setDeviceOutputChannels(channels_);
    sampleRate_ = std::max(1, static_cast<int>(sampleRate));
    maximumBlockFrames_ = std::max(1, maximumBlockFrames);
    buffer_.setSize(std::max(8, channels_), maximumBlockFrames_);
    buffer_.setActiveChannels(channels_);
    dspChain_.prepare(static_cast<double>(sampleRate_), maximumBlockFrames_, channels_);
    prepared_ = true;
}

void PcmDomainDspStage::reset()
{
    if (prepared_)
        dspChain_.reset();
}

template <typename Sample>
bool PcmDomainDspStage::process(std::vector<Sample>& interleaved, std::string& error)
{
    if (interleaved.empty())
    {
        error.clear();
        return true;
    }
    if (!prepared_)
    {
        error = "native_pcm_dsp_not_prepared";
        return false;
    }
    if (interleaved.size() % static_cast<std::size_t>(channels_) != 0)
    {
        error = "native_pcm_dsp_invalid_interleaved_input";
        return false;
    }

    const int frames = static_cast<int>(interleaved.size() / static_cast<std::size_t>(channels_));
    for (int offset = 0; offset < frames;)
    {
        buffer_.setActiveChannels(channels_);
        buffer_.clear();
        const int framesThisBlock = std::min(maximumBlockFrames_, frames - offset);
        for (int channel = 0; channel < channels_; ++channel)
        {
            double* destination = buffer_.getWritePointer(channel);
            for (int frame = 0; frame < framesThisBlock; ++frame)
            {
                destination[frame] = interleaved[
                    static_cast<std::size_t>(offset + frame) * static_cast<std::size_t>(channels_)
                    + static_cast<std::size_t>(channel)];
            }
        }

        dspChain_.processBlock(buffer_, 0, framesThisBlock);

        for (int channel = 0; channel < channels_; ++channel)
        {
            const double* source = buffer_.getReadPointer(channel);
            for (int frame = 0; frame < framesThisBlock; ++frame)
            {
                interleaved[
                    static_cast<std::size_t>(offset + frame) * static_cast<std::size_t>(channels_)
                    + static_cast<std::size_t>(channel)] = source[frame];
            }
        }
        offset += framesThisBlock;
    }

    error.clear();
    return true;
}

template bool PcmDomainDspStage::process<float>(std::vector<float>&, std::string&);
template bool PcmDomainDspStage::process<double>(std::vector<double>&, std::string&);
