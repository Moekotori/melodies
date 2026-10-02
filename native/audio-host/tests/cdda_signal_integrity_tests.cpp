#include "WindowsCddaReader.h"
#include "CddaSectorStream.h"
#include "CddaPcmProcessor.h"
#include "LinuxCddaDevice.h"

#include <algorithm>
#include <cmath>
#include <complex>
#include <memory>
#include <numbers>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

namespace
{
constexpr std::uint32_t sectorBytes = 2352;

void require(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
}

struct Disc
{
    std::vector<CddaTrackInfo> tracks { { 1, 0, 18, true, false } };
    std::vector<std::uint8_t> pcm;
    int mismatchesRemaining = 0;
    bool alwaysMismatch = false;
    std::uint32_t maximumBatch = 16;
    bool mediaChangedAtBoundary = false;
    int reads = 0;
};

class RecordedDisc final : public CddaDevice
{
public:
    explicit RecordedDisc(std::shared_ptr<Disc> discToUse) : disc(std::move(discToUse)) {}
    std::vector<CddaTrackInfo> readToc() override { return disc->tracks; }
    std::vector<std::uint8_t> readAudioSectors(std::uint32_t start, std::uint32_t count) override
    {
        ++disc->reads;
        if (start == 15 && disc->mediaChangedAtBoundary)
            throw CddaFatalReadError("audio_cd_disc_changed", CddaReadFault::MediaChanged, 1110);
        if (count > disc->maximumBatch) throw std::runtime_error("audio_cd_test_batch_limit");
        const auto first = static_cast<std::size_t>(start) * sectorBytes;
        const auto last = first + static_cast<std::size_t>(count) * sectorBytes;
        if (last > disc->pcm.size()) throw std::runtime_error("audio_cd_test_overread");
        std::vector<std::uint8_t> bytes(disc->pcm.begin() + first, disc->pcm.begin() + last);
        if (start == 15 && (disc->alwaysMismatch || disc->mismatchesRemaining > 0))
        {
            if (disc->mismatchesRemaining > 0) --disc->mismatchesRemaining;
            // Return a displaced sample with successful status and unchanged byte count.
            bytes[0] ^= 0x40;
        }
        return bytes;
    }
private:
    std::shared_ptr<Disc> disc;
};

std::shared_ptr<Disc> makeDisc()
{
    auto disc = std::make_shared<Disc>();
    disc->pcm.resize(18 * sectorBytes);
    for (std::size_t sample = 0; sample < disc->pcm.size() / 2; ++sample)
    {
        const auto value = static_cast<std::uint16_t>((sample * 173 + 41) % 65536);
        disc->pcm[sample * 2] = static_cast<std::uint8_t>(value & 0xff);
        disc->pcm[sample * 2 + 1] = static_cast<std::uint8_t>(value >> 8);
    }
    return disc;
}

std::vector<float> play(const std::shared_ptr<Disc>& disc, double start = 0, int lastTrack = 0, int outputRate = 44100)
{
    WindowsCddaReader reader([disc](const auto&) { return std::make_unique<RecordedDisc>(disc); });
    std::vector<float> pcm;
    reader.streamTrackTo("D:", 1, [&pcm](const auto& chunk) {
        pcm.insert(pcm.end(), chunk.begin(), chunk.end());
    }, start, -1, -1, lastTrack, outputRate);
    return pcm;
}
} // namespace

void runCddaSignalIntegrityTests()
{
    // Byte-varying input catches lost/duplicated overlap and displaced reads that constant PCM hides.
    const auto disc = makeDisc();
    CddaPcmProcessor reference(disc->tracks);
    const auto expected = reference.convert(disc->pcm, 0);
    require(play(disc) == expected, "overlap verification lost, duplicated, or altered unmarked CD samples");
    disc->mismatchesRemaining = 1;
    require(play(disc) == expected && disc->mismatchesRemaining == 0,
        "temporary successful-but-displaced read was not retried before PCM output");
    disc->maximumBatch = 1;
    require(play(disc) == expected, "single-sector drive verification changed PCM length/content");
    disc->maximumBatch = 16;
    disc->alwaysMismatch = true;
    RecordedDisc device(disc);
    CddaReadPolicy policy;
    CddaSectorStream stream(device, policy, 0, 18);
    const auto prefix = stream.next();
    require(prefix && prefix->bytes.size() == 15 * sectorBytes, "unverified overlap was emitted prematurely");
    bool failed = false;
    try { (void) stream.next(); }
    catch (const CddaOverlapMismatch&) { failed = true; }
    require(failed && policy.snapshot().overlapMismatches == 2 && policy.snapshot().readCalls == 3,
        "persistent content mismatch was hidden or retried without a limit");
    require(policy.snapshot().failureLba == 15 && policy.snapshot().failureSectorCount == 3
        && policy.snapshot().failureKind == CddaReadFault::Overlap, "overlap failure lost native read location");

    // A same-layout replacement may be readable on the next attempt. Never clear its media-change error by retrying.
    const auto changedDisc = makeDisc();
    changedDisc->mediaChangedAtBoundary = true;
    RecordedDisc changedDevice(changedDisc);
    CddaReadPolicy changedPolicy;
    CddaSectorStream changedStream(changedDevice, changedPolicy, 0, 18);
    require(changedStream.next()->bytes.size() == 15 * sectorBytes, "media-change fixture prefix mismatch");
    bool mediaChanged = false;
    try { (void) changedStream.next(); }
    catch (const CddaFatalReadError& error) { mediaChanged = error.fault == CddaReadFault::MediaChanged; }
    require(mediaChanged && changedDisc->reads == 2 && changedPolicy.snapshot().retries == 0
        && changedPolicy.snapshot().batchReductions == 0, "media-change error was retried as an ordinary bad sector");
    require(changedPolicy.snapshot().failureLba == 15 && changedPolicy.snapshot().nativeError == 1110,
        "media-change diagnostic lost its location or native cause");

    const std::vector<std::uint8_t> fixed { 0x70, 0, 6, 0, 0, 0, 0, 6, 0, 0, 0, 0, 0x28, 0 };
    const std::vector<std::uint8_t> descriptor { 0x72, 2, 0x3a, 0, 0, 0, 0, 0 };
    for (std::size_t length = 0; length < fixed.size(); ++length)
        require(!parseCddaSense(std::span(fixed).first(length)).valid, "truncated fixed sense was treated as valid");
    require(parseCddaSense(fixed).key == 6 && parseCddaSense(fixed).asc == 0x28, "fixed sense parsing mismatch");
    require(parseCddaSense(descriptor).key == 2 && parseCddaSense(descriptor).asc == 0x3a,
        "descriptor sense parsing mismatch");
    bool removed = false;
    try { validateLinuxCddaTransfer(2, 0, 8, 0, descriptor); }
    catch (const CddaFatalReadError& error) { removed = error.fault == CddaReadFault::NoMedia; }
    require(removed, "reported medium absence was treated as a retriable SCSI failure");
    bool unitAttention = false;
    try { validateLinuxCddaTransfer(2, 0, 8, 0, fixed); }
    catch (const CddaFatalReadError& error) { unitAttention = error.fault == CddaReadFault::MediaChanged; }
    require(unitAttention, "reported unit attention could be cleared by retrying data from a new disc");
    auto mediumError = descriptor;
    mediumError[1] = 3;
    mediumError[2] = 0x11;
    bool retriableMediumError = false;
    try { validateLinuxCddaTransfer(2, 0, 8, 0, mediumError); }
    catch (const CddaReadError& error) {
        retriableMediumError = dynamic_cast<const CddaFatalReadError*>(&error) == nullptr
            && error.fault == CddaReadFault::Io && error.sense.valid && error.sense.key == 3;
    }
    require(retriableMediumError, "ordinary medium-read errors lost their bounded recovery path");

    // Zero input in the other channel must stay zero; DC gain must converge to unity.
    CddaDeemphasis dc;
    float output = 0;
    for (int sample = 0; sample < 1000; ++sample)
    {
        output = dc.process(0.5f, 0);
        require(dc.process(0, 1) == 0, "CD deemphasis leaked between channels");
    }
    require(std::abs(output - 0.5f) < 1e-6f, "CD deemphasis changed steady DC gain");
    dc.reset();
    require(dc.process(0, 0) == 0, "CD deemphasis reset retained the old track's filter state");

    // Measure the product's impulse response against the analog target, not its own coefficients.
    CddaDeemphasis impulseFilter;
    std::vector<double> impulse(256);
    for (std::size_t sample = 0; sample < impulse.size(); ++sample)
        impulse[sample] = impulseFilter.process(sample == 0 ? 1.0f : 0.0f, 0);
    double worstErrorDb = 0;
    for (int point = 0; point <= 160; ++point)
    {
        const double frequency = 20 * std::pow(1000.0, point / 160.0);
        const double omega = 2 * std::numbers::pi * frequency;
        std::complex<double> response {};
        for (std::size_t sample = 0; sample < impulse.size(); ++sample)
            response += impulse[sample] * std::polar(1.0, -omega * static_cast<double>(sample) / 44100);
        const double target = std::sqrt((1 + std::pow(omega * 15e-6, 2)) / (1 + std::pow(omega * 50e-6, 2)));
        worstErrorDb = std::max(worstErrorDb, std::abs(20 * std::log10(std::abs(response) / target)));
    }
    require(worstErrorDb < 0.02, "CD deemphasis exceeds 0.02dB analog-target magnitude tolerance");

    // A real reader seek must prime the filter while emitting exactly the requested remaining frames.
    const auto emphasized = makeDisc();
    emphasized->tracks[0].preEmphasis = true;
    const auto full = play(emphasized);
    const auto sought = play(emphasized, 7.0 / 75);
    require(sought.size() == full.size() - 7 * sectorBytes / 2, "deemphasis seek added or removed audio frames");
    for (std::size_t index = 0; index < sought.size(); ++index)
        require(std::abs(sought[index] - full[index + 7 * sectorBytes / 2]) < 1e-6f,
            "deemphasis seek did not restore the source filter history");

    emphasized->tracks = { { 1, 0, 9, true, true }, { 2, 9, 18, true, true } };
    require(play(emphasized, 0, 2) == full, "same-emphasis CD boundary reset filter history");
    emphasized->tracks[1].preEmphasis = false;
    const auto mixed = play(emphasized, 0, 2);
    require(std::equal(mixed.begin() + 9 * sectorBytes / 2, mixed.end(), expected.begin() + 9 * sectorBytes / 2),
        "CD deemphasis continued into an unmarked track");

    const auto oneSecond = makeDisc();
    oneSecond->tracks[0].endLba = 75;
    oneSecond->pcm.resize(75 * sectorBytes);
    for (std::size_t frame = 0; frame < 44100; ++frame)
    {
        const auto sample = static_cast<std::int16_t>(8000 * std::sin(2 * std::numbers::pi * 1000 * frame / 44100));
        const auto bits = static_cast<std::uint16_t>(sample);
        oneSecond->pcm[frame * 4] = static_cast<std::uint8_t>(bits & 0xff);
        oneSecond->pcm[frame * 4 + 1] = static_cast<std::uint8_t>(bits >> 8);
        oneSecond->pcm[frame * 4 + 2] = oneSecond->pcm[frame * 4 + 3] = 0;
    }
    const auto resampled = play(oneSecond, 0, 0, 48000);
    require(resampled.size() == 48000 * 2, "44100-to-48000 conversion changed the one-second duration");
    std::complex<double> tone {};
    for (std::size_t frame = 1000; frame < 47000; ++frame)
    {
        require(resampled[frame * 2 + 1] == 0, "native CD resampling leaked into the other channel");
        tone += static_cast<double>(resampled[frame * 2])
            * std::polar(1.0, -2 * std::numbers::pi * 1000 * static_cast<double>(frame) / 48000);
    }
    require(std::abs(2 * std::abs(tone) / 46000 - 8000.0 / 32768) < 0.002,
        "native CD resampling changed reference frequency or gain");
    const auto resampledSeek = play(oneSecond, 7.0 / 75, 0, 48000);
    require(resampledSeek.size() == resampled.size() - 7 * 640 * 2, "native SRC seek added or removed frames");
    for (std::size_t sample = 0; sample < resampledSeek.size(); ++sample)
        require(std::abs(resampledSeek[sample] - resampled[sample + 7 * 640 * 2]) < 1e-5f,
            "native SRC seek failed to prime history");
}
